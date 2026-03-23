#!/usr/bin/env bash
set -euo pipefail

REPO="https://github.com/screaming-in-space/agent-tools.git"
DEST="$HOME/.claude/skills"
TMPDIR=$(mktemp -d)

echo "Installing Claude skills from screaming-in-space/agent-tools..."

# Sparse checkout just the skills directory
git clone --depth 1 --filter=blob:none --sparse "$REPO" "$TMPDIR" 2>/dev/null
cd "$TMPDIR"
git sparse-checkout set .claude/skills 2>/dev/null

# Copy each skill into the user's global skills directory
for skill_dir in .claude/skills/*/; do
  skill=$(basename "$skill_dir")
  mkdir -p "$DEST/$skill"
  cp -R "$skill_dir"* "$DEST/$skill/"
  echo "  installed: $skill"
done

# Clean up
rm -rf "$TMPDIR"

echo "Done. Skills available in your next Claude Code session."
