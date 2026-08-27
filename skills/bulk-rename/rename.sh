#!/usr/bin/env bash
# bulk-rename.sh — apply word-boundary safe symbol/string renames across a tree.
#
# Designed to collapse a refactor that would otherwise be ~50 Edit tool calls
# into a single bash invocation. Operates only on tracked files (`git ls-files`)
# so untracked junk and build artifacts are never touched.

set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  rename.sh [options] OLD1 NEW1 [OLD2 NEW2 ...]
  rename.sh [options] --pairs-from FILE

Options:
  --apply              Apply the renames in place. Default is --dry-run.
  --dry-run            Print a summary; do not modify files. (default)
  --scope GLOB         Restrict to tracked files matching GLOB (git pathspec, e.g.
                       '*.cs' or 'src/**'). Defaults to all tracked text files.
  --no-word-boundary   Disable \b word-boundary anchors. By default both sides of
                       each OLD token are anchored on word boundaries (safe for
                       identifiers; do NOT use for free-form text).
  --commit MSG         After applying, create a single commit with MSG. Implies
                       --apply. Stages only files this script touched.
  --help, -h           Show this message.

Pairs file format:
  One OLD<TAB>NEW per line. Blank lines and lines starting with # are ignored.

Order safety:
  Pairs are processed longest-OLD first so that prefix collisions (e.g.
  ChatScopeKey vs ChatScopeKeys) resolve correctly. The script ALSO warns
  if any NEW token appears as an OLD in another pair (transitive renames
  A->B then B->C will clobber each other; split into two invocations).

What this does NOT do:
  - Rename FILE PATHS. Use `git mv` separately for path renames; THEN run
    this script to fix references inside the renamed files (and the rest
    of the tree).
  - Touch binary files (filtered via `file(1)` text/empty check).
  - Touch untracked files. If you want them in scope, `git add -N` them first.

Examples:
  # Dry-run a five-pair rename across all tracked .cs files.
  rename.sh --scope '*.cs' \
    ChatScopeKey ChatScope \
    IChatScopeKey IChatScopeResolver \
    DefaultChatScopeKey StateBagChatScopeResolver \
    ChatScopeKeys ChatScopeStateBagKeys \
    AgentChatScopeExtractor AgentOptionsChatScopeResolver

  # Same, applied + committed in one shot.
  rename.sh --scope '*.cs' --commit 'refactor: rename ChatScope* type cluster' \
    --pairs-from rename-pairs.tsv
USAGE
}

apply=0
scope=""
boundary=1
pairs_file=""
commit_msg=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --apply) apply=1; shift ;;
    --dry-run) apply=0; shift ;;
    --scope) scope="$2"; shift 2 ;;
    --no-word-boundary) boundary=0; shift ;;
    --pairs-from) pairs_file="$2"; shift 2 ;;
    --commit) commit_msg="$2"; apply=1; shift 2 ;;
    --help|-h) usage; exit 0 ;;
    --*) echo "rename.sh: unknown option: $1" >&2; usage; exit 2 ;;
    *) break ;;
  esac
done

# --- Collect pairs into parallel arrays ---
olds=()
news=()

if [[ -n "$pairs_file" ]]; then
  if [[ ! -f "$pairs_file" ]]; then
    echo "rename.sh: pairs file not found: $pairs_file" >&2
    exit 2
  fi
  while IFS=$'\t' read -r o n; do
    [[ -z "${o:-}" ]] && continue
    [[ "${o#\#}" != "$o" ]] && continue
    if [[ -z "${n:-}" ]]; then
      echo "rename.sh: pairs file line missing TAB-separated NEW: '$o'" >&2
      exit 2
    fi
    olds+=("$o")
    news+=("$n")
  done < "$pairs_file"
fi

while [[ $# -gt 0 ]]; do
  if [[ $# -lt 2 ]]; then
    echo "rename.sh: positional pairs must come in OLD NEW twos (odd count, missing NEW for '$1')" >&2
    exit 2
  fi
  olds+=("$1")
  news+=("$2")
  shift 2
done

if [[ ${#olds[@]} -eq 0 ]]; then
  echo "rename.sh: no pairs supplied" >&2
  usage
  exit 2
fi

# --- Verify we're inside a git tree ---
if ! git rev-parse --show-toplevel >/dev/null 2>&1; then
  echo "rename.sh: not inside a git repository" >&2
  exit 2
fi

# --- Verify git was built with PCRE (only matters when boundary anchors are on,
# but check unconditionally — easier to read the failure than to debug 0-match results
# silently in --no-word-boundary mode if PCRE is missing for some other reason).
if ! git grep -P -q -- '.' -- ':(top,glob)*' >/dev/null 2>&1; then
  # Distinguish "PCRE unsupported" from "no files matched". Re-run with explicit
  # error capture to read git's stderr.
  pcre_check=$(git grep -P -- 'x' -- ':(top,glob)*' 2>&1 >/dev/null || true)
  if [[ "$pcre_check" == *"Perl-compatible regular expressions are not supported"* ]]; then
    echo "rename.sh: this git was built without libpcre — git grep -P is unavailable." >&2
    echo "  Install a PCRE-enabled git build: 'apt install git' on Debian/Ubuntu, or" >&2
    echo "  rebuild with USE_LIBPCRE=YesPlease. The script's word-boundary anchoring" >&2
    echo "  depends on \\b in PCRE; without it, --no-word-boundary mode would still" >&2
    echo "  silently miss matches in the count phase." >&2
    exit 2
  fi
  # Otherwise the grep just found nothing — that's fine, real grep happens later.
fi

# --- Warn on transitive renames ---
for ((i=0; i<${#olds[@]}; i++)); do
  for ((j=0; j<${#olds[@]}; j++)); do
    if [[ $i -ne $j && "${news[$i]}" == "${olds[$j]}" ]]; then
      echo "rename.sh: WARNING — transitive rename: '${olds[$i]}' -> '${news[$i]}' THEN '${olds[$j]}' -> '${news[$j]}' will clobber the first pass. Split into separate invocations." >&2
    fi
  done
done

# --- Sort pair indexes longest-OLD first ---
sorted=()
while IFS=$'\t' read -r _ idx; do
  sorted+=("$idx")
done < <(
  for ((i=0; i<${#olds[@]}; i++)); do
    printf '%d\t%d\n' "${#olds[$i]}" "$i"
  done | sort -rn -k1,1
)

# --- Build the file list ---
files=()
if [[ -n "$scope" ]]; then
  while IFS= read -r f; do files+=("$f"); done < <(git ls-files -- "$scope")
else
  while IFS= read -r f; do files+=("$f"); done < <(git ls-files)
fi

if [[ ${#files[@]} -eq 0 ]]; then
  echo "rename.sh: no tracked files in scope (scope='$scope')" >&2
  exit 1
fi

# --- Filter to text files (skip binaries by extension blocklist) ---
# Per-file `file(1)` calls are pathologically slow on Git Bash (Windows process-spawn cost).
# Hardcoded blocklist is faster and good enough; if you hit a false positive, narrow --scope.
binary_re='\.(png|jpg|jpeg|gif|bmp|ico|svgz|webp|pdf|zip|gz|tgz|tar|7z|rar|bz2|xz|exe|dll|so|dylib|pdb|nupkg|class|jar|war|wasm|mp3|mp4|mov|avi|mkv|wav|flac|ogg|woff|woff2|ttf|otf|eot|sqlite|db|bin)$'
text_files=()
for f in "${files[@]}"; do
  [[ -f "$f" ]] || continue
  if [[ "$f" =~ $binary_re ]]; then
    continue
  fi
  text_files+=("$f")
done

if [[ ${#text_files[@]} -eq 0 ]]; then
  echo "rename.sh: no text files in scope after binary filter (scope='$scope')" >&2
  exit 1
fi

# --- Build the perl rename script ---
# Escape regex metachars in OLD; escape $ @ \ in NEW (perl substitution RHS).
perl_script=""
for i in "${sorted[@]}"; do
  o_esc=$(printf '%s' "${olds[$i]}" | perl -pe 's{([\\/().\[\]{}^\$*+?|])}{\\$1}g')
  n_esc=$(printf '%s' "${news[$i]}" | perl -pe 's{([\\\$\@])}{\\$1}g')
  if [[ $boundary -eq 1 ]]; then
    perl_script+="s/\\b${o_esc}\\b/${n_esc}/g; "
  else
    perl_script+="s/${o_esc}/${n_esc}/g; "
  fi
done

# --- Match counts (pre-rename) ---
# Use `git grep` so we get pcre support without the Git-Bash locale issues that
# break system grep -P, and so the scope filter is respected by git directly.
# `set -e`/`pipefail` would abort on git grep's exit-1 when a pair has zero matches,
# so disable just for this loop.
set +e
set +o pipefail

# Build the path-spec for git grep — same scope as we used to filter `files`.
gitgrep_pathspec=()
if [[ -n "$scope" ]]; then
  gitgrep_pathspec=(-- "$scope")
fi

echo "Match counts (pre-rename), ${#text_files[@]} text files in scope:"
total=0
for i in "${sorted[@]}"; do
  if [[ $boundary -eq 1 ]]; then
    count=$(git grep -P -c -- "\\b${olds[$i]}\\b" "${gitgrep_pathspec[@]}" 2>/dev/null \
      | awk -F: '{s+=$NF} END {print s+0}')
  else
    count=$(git grep -F -c -- "${olds[$i]}" "${gitgrep_pathspec[@]}" 2>/dev/null \
      | awk -F: '{s+=$NF} END {print s+0}')
  fi
  count=${count:-0}
  printf '  %5d  %s -> %s\n' "$count" "${olds[$i]}" "${news[$i]}"
  total=$((total + count))
done
echo "  -----"
printf '  %5d  total occurrences\n' "$total"
set -e
set -o pipefail

if [[ $total -eq 0 ]]; then
  echo "rename.sh: nothing to do — no matches in scope." >&2
  exit 0
fi

if [[ $apply -eq 0 ]]; then
  echo
  echo "(dry-run — re-run with --apply to perform the rename, or --commit MSG to apply+commit)"
  exit 0
fi

# --- Apply ---
echo
echo "Applying..."
printf '%s\0' "${text_files[@]}" | xargs -0 perl -pi -e "$perl_script"

# --- Optional commit ---
if [[ -n "$commit_msg" ]]; then
  # Stage only the files that actually changed (intersect text_files with `git diff --name-only`).
  changed=()
  while IFS= read -r f; do changed+=("$f"); done < <(git diff --name-only -- "${text_files[@]}")
  if [[ ${#changed[@]} -eq 0 ]]; then
    echo "rename.sh: nothing changed on disk — skipping commit."
    exit 0
  fi
  git add -- "${changed[@]}"
  git commit -m "$commit_msg"
  echo
  echo "Committed ${#changed[@]} file(s)."
fi

echo
echo "Suggested next steps:"
echo "  git status --short"
echo "  git diff --stat"
