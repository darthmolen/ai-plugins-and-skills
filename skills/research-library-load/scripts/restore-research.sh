#!/usr/bin/env bash
# restore-research.sh
# Called by the SessionStart hook to restore missing research repos from the manifest.
# Reads research/.manifest.yaml, clones any missing repos, and prints a status summary.
# Always exits 0 to never block session start.

set -euo pipefail

# Resolve repo root
REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null || echo "")"
if [[ -z "$REPO_ROOT" ]]; then
  echo "Warning: Not inside a git repository. Cannot restore research library."
  exit 0
fi

MANIFEST="$REPO_ROOT/research/.manifest.yaml"
RESEARCH_DIR="$REPO_ROOT/research"

if [[ ! -f "$MANIFEST" ]]; then
  echo "No research manifest found at $MANIFEST"
  exit 0
fi

# Check if repos list is empty
if grep -qE '^\s*repos:\s*\[\s*\]\s*$' "$MANIFEST"; then
  echo "Research manifest is empty — no repos to restore."
  exit 0
fi

# Parse the simple YAML manifest with bash
# Extracts: name, url, ref, sparse paths for each repo entry
mkdir -p "$RESEARCH_DIR"

current_name=""
current_url=""
current_ref=""
current_sparse=""
in_sparse=false
cloned=0
present=0
failed=0
status_lines=""

process_repo() {
  local name="$1" url="$2" ref="$3" sparse="$4"

  if [[ -z "$name" || -z "$url" ]]; then
    return
  fi

  local repo_dir="$RESEARCH_DIR/$name"

  if [[ -d "$repo_dir" ]]; then
    status_lines+="  [OK] $name (already present)"$'\n'
    ((present++)) || true
    return
  fi

  local ref_display=""
  [[ -n "$ref" ]] && ref_display=" @ $ref"

  # Clone with timeout to avoid blocking session start
  local clone_ok=true
  if [[ -n "$sparse" ]]; then
    # Sparse checkout
    if GIT_TERMINAL_PROMPT=0 timeout 60 git clone --filter=blob:none --sparse "$url" "$repo_dir" 2>/dev/null; then
      # Set sparse-checkout paths
      local IFS=','
      local sparse_args=()
      for p in $sparse; do
        p="$(echo "$p" | xargs)"  # trim whitespace
        [[ -n "$p" ]] && sparse_args+=("$p")
      done
      if [[ ${#sparse_args[@]} -gt 0 ]]; then
        (cd "$repo_dir" && git sparse-checkout set "${sparse_args[@]}" 2>/dev/null) || true
      fi
    else
      clone_ok=false
    fi
  else
    # Full clone
    if ! GIT_TERMINAL_PROMPT=0 timeout 120 git clone "$url" "$repo_dir" 2>/dev/null; then
      clone_ok=false
    fi
  fi

  if [[ "$clone_ok" = false ]]; then
    status_lines+="  [FAILED] $name ($url — clone failed, check URL/network/permissions)"$'\n'
    ((failed++)) || true
    return
  fi

  # Checkout specific ref if specified
  if [[ -n "$ref" ]]; then
    (cd "$repo_dir" && git checkout "$ref" 2>/dev/null) || true
  fi

  local sparse_note=""
  [[ -n "$sparse" ]] && sparse_note=" [sparse]"
  status_lines+="  [CLONED] $name ($url${ref_display})${sparse_note}"$'\n'
  ((cloned++)) || true
}

# Line-by-line YAML parser for our simple manifest format
while IFS= read -r line || [[ -n "$line" ]]; do
  # Skip comments and empty lines
  [[ "$line" =~ ^[[:space:]]*# ]] && continue
  [[ "$line" =~ ^[[:space:]]*$ ]] && continue

  # Detect new repo entry (- name:)
  if [[ "$line" =~ ^[[:space:]]*-[[:space:]]*name:[[:space:]]*(.*) ]]; then
    # Process previous repo if we have one
    process_repo "$current_name" "$current_url" "$current_ref" "$current_sparse"
    current_name="$(echo "${BASH_REMATCH[1]}" | xargs)"
    current_url=""
    current_ref=""
    current_sparse=""
    in_sparse=false
    continue
  fi

  # url field
  if [[ "$line" =~ ^[[:space:]]+url:[[:space:]]*(.*) ]]; then
    current_url="$(echo "${BASH_REMATCH[1]}" | xargs)"
    in_sparse=false
    continue
  fi

  # ref field
  if [[ "$line" =~ ^[[:space:]]+ref:[[:space:]]*(.*) ]]; then
    current_ref="$(echo "${BASH_REMATCH[1]}" | xargs)"
    in_sparse=false
    continue
  fi

  # sparse field (list header)
  if [[ "$line" =~ ^[[:space:]]+sparse:[[:space:]]*$ ]]; then
    in_sparse=true
    continue
  fi

  # sparse list items
  if [[ "$in_sparse" = true && "$line" =~ ^[[:space:]]+-[[:space:]]*(.*) ]]; then
    local_path="$(echo "${BASH_REMATCH[1]}" | xargs)"
    if [[ -n "$current_sparse" ]]; then
      current_sparse="$current_sparse,$local_path"
    else
      current_sparse="$local_path"
    fi
    continue
  fi

  # Any non-sparse-item line ends sparse parsing
  if [[ "$in_sparse" = true ]]; then
    in_sparse=false
  fi

done < "$MANIFEST"

# Process the last repo entry
process_repo "$current_name" "$current_url" "$current_ref" "$current_sparse"

# Print summary
total=$((present + cloned + failed))
if [[ $total -eq 0 ]]; then
  echo "Research manifest is empty — no repos to restore."
else
  echo "Research Library Status ($cloned cloned, $present present, $failed failed):"
  echo -n "$status_lines"
fi

exit 0
