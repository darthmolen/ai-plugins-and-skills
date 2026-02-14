#!/usr/bin/env bash
# detect-tools.sh - Detect if Claude Code and GitHub Copilot CLI are installed
# Returns JSON-style output with availability status for each tool.

set -euo pipefail

CLAUDE_INSTALLED=false
CLAUDE_VERSION=""
COPILOT_INSTALLED=false
COPILOT_VERSION=""

# --- Detect Claude Code ---
if command -v claude >/dev/null 2>&1; then
    CLAUDE_INSTALLED=true
    CLAUDE_VERSION=$(claude --version 2>/dev/null || echo "unknown")
fi

# --- Detect GitHub Copilot CLI (gh copilot extension) ---
if command -v gh >/dev/null 2>&1; then
    if gh extension list 2>/dev/null | grep -q "copilot"; then
        COPILOT_INSTALLED=true
        COPILOT_VERSION=$(gh copilot --version 2>/dev/null || echo "unknown")
    fi
fi

# --- Output results ---
echo "=== AI Code Reviewer: Tool Detection ==="
echo ""
echo "Claude Code installed: ${CLAUDE_INSTALLED}"
if [ "$CLAUDE_INSTALLED" = true ]; then
    echo "  Version: ${CLAUDE_VERSION}"
fi
echo ""
echo "Copilot CLI installed: ${COPILOT_INSTALLED}"
if [ "$COPILOT_INSTALLED" = true ]; then
    echo "  Version: ${COPILOT_VERSION}"
fi
echo ""

# --- Summary ---
if [ "$CLAUDE_INSTALLED" = true ] && [ "$COPILOT_INSTALLED" = true ]; then
    echo "STATUS: BOTH_AVAILABLE"
    echo "Both tools are installed. Cross-review is fully supported."
    exit 0
elif [ "$CLAUDE_INSTALLED" = true ]; then
    echo "STATUS: CLAUDE_ONLY"
    echo "Only Claude Code is available. Copilot CLI is not installed."
    echo "Install Copilot CLI: gh extension install github/gh-copilot"
    exit 1
elif [ "$COPILOT_INSTALLED" = true ]; then
    echo "STATUS: COPILOT_ONLY"
    echo "Only Copilot CLI is available. Claude Code is not installed."
    echo "Install Claude Code: npm install -g @anthropic-ai/claude-code"
    exit 1
else
    echo "STATUS: NONE_AVAILABLE"
    echo "Neither tool is installed. Cannot perform cross-review."
    echo "Install Claude Code: npm install -g @anthropic-ai/claude-code"
    echo "Install Copilot CLI: gh extension install github/gh-copilot"
    exit 2
fi
