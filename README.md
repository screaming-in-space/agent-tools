# Agent Tools — Shared Claude Skills

A collection of reusable [Claude Code](https://claude.ai/claude-code) skills for bootstrapping and maintaining AI-assisted repositories.

## Available Skills

| Skill | Description |
|-------|-------------|
| `agentify` | Bootstrap a repo with ML context files (CLAUDE.md, copilot-instructions.md, AGENTS.md) as thin shims pointing to centralized docs |

## Installation

### One-liner (recommended)

Fetch the latest skills directly into your user-level Claude config — no clone, no submodule:

```bash
bash <(curl -fsSL https://raw.githubusercontent.com/screaming-in-space/agent-tools/main/install.sh)
```

### Manual

Copy a specific skill into your global Claude skills directory:

```bash
mkdir -p ~/.claude/skills/agentify
curl -fsSL https://raw.githubusercontent.com/screaming-in-space/agent-tools/main/.claude/skills/agentify/SKILL.md \
  -o ~/.claude/skills/agentify/SKILL.md
```

### Update

Re-run the install script or manual curl to pull the latest versions. Existing files are overwritten.

## Usage

Once installed, skills are available as slash commands in any Claude Code session:

```
/agentify
```

## Adding Skills to a Specific Project

If you want skills scoped to a single repo instead of globally:

```bash
mkdir -p .claude/skills/agentify
curl -fsSL https://raw.githubusercontent.com/screaming-in-space/agent-tools/main/.claude/skills/agentify/SKILL.md \
  -o .claude/skills/agentify/SKILL.md
```

Then commit `.claude/skills/` to your repo.

## Contributing

Add new skills under `.claude/skills/<skill-name>/SKILL.md`. Each skill must include frontmatter:

```yaml
---
name: skill-name
description: One-line description of what the skill does.
user-invocable: true
---
```
