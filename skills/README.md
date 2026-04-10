# Skills

Shared Claude Code skills for AI-assisted development workflows.

## Available Skills

| Skill | Description |
|-------|-------------|
| [`agentify`](agentify/) | Bootstrap a repo with ML context files (CLAUDE.md, copilot-instructions.md, AGENTS.md) as thin shims pointing to centralized docs |
| [`claude-api-qa-creator`](claude-api-qa-creator/) | Generate QA helpers from repo analysis |

## Installation

See the [repo README](../README.md) for install commands (`install-skills.sh`).

## Creating a Skill

Add a new directory under `skills/` with a `SKILL.md` file containing frontmatter:

```yaml
---
name: skill-name
description: One-line description of what the skill does.
user-invocable: true
---
```
