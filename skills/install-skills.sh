#!/usr/bin/env bash
set -euo pipefail

REPO="https://github.com/screaming-in-space/agent-tools.git"
TMPDIR=$(mktemp -d)

# Default: install into current repo. --global: install into ~/.claude/skills
if [ "${1:-}" = "--global" ]; then
  DEST="$HOME/.claude/skills"
  echo "Installing Claude skills globally (~/.claude/skills)..."
else
  DEST="$(pwd)/.claude/skills"
  echo "Installing Claude skills into current repo (.claude/skills)..."
fi

# Sparse checkout just the skills directory
git clone --depth 1 --filter=blob:none --sparse "$REPO" "$TMPDIR" 2>/dev/null
cd "$TMPDIR"
git sparse-checkout set skills 2>/dev/null

# Copy each skill into the destination (skip install script and README)
for skill_dir in skills/*/; do
  skill=$(basename "$skill_dir")
  mkdir -p "$DEST/$skill"
  cp -R "$skill_dir"* "$DEST/$skill/"
  echo "  installed: $skill"
done

# Clean up
rm -rf "$TMPDIR"

echo "Done. Restart Claude Code for new skills to register."
