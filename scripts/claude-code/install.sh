#!/bin/bash
set -e

# Dynamically resolve repo root from script location (works from any clone path)
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"   # Absolute path to this script
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"  # Up 2 levels = repo root

echo "=== AI-Plugins-And-Skills Plugin Installer ==="
echo ""
echo "Adding AI-Plugins-And-Skills marketplace from: $REPO_ROOT"

# Change to repo root and use relative path (claude expects ./path format)
cd "$REPO_ROOT"
claude plugin marketplace add "./"

echo ""
echo "Installing ai-plugins-and-skills plugin..."
claude plugin install ai-plugins-and-skills@ai-plugins-and-skills-ai-standards

echo ""
echo "Installing ai-plugins-and-skills-config-sync plugin..."
claude plugin install ai-plugins-and-skills-config-sync@ai-plugins-and-skills-ai-standards

echo ""
echo "=== Installation Complete ==="
echo "Restart Claude Code to activate plugins."
