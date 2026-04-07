# Agent Tools

Shared [Claude Code](https://claude.ai/claude-code) skills and standalone .NET agents for AI-assisted development workflows.

## Agents

| Agent | Description |
|-------|-------------|
| [`ContextCartographer`](agents/src/ContextCartographer/) | Scans a markdown directory and produces a structured context map (CONTEXT.md) using M.E.AI tool calling |

### Running an agent

```bash
cd agents/src/ContextCartographer
dotnet run -- <directory-path> [--endpoint <url>] [--model <name>]
```

Requires .NET 10 SDK and an OpenAI-compatible endpoint (e.g., [LM Studio](https://lmstudio.ai) at `http://localhost:1234/v1`).

## Skills

| Skill | Description |
|-------|-------------|
| `agentify` | Bootstrap a repo with ML context files (CLAUDE.md, copilot-instructions.md, AGENTS.md) as thin shims pointing to centralized docs |

## Installation

### Into current repo (default)

Run from the root of the repo you want to add skills to:

```bash
bash <(curl -fsSL https://raw.githubusercontent.com/screaming-in-space/agent-tools/main/install.sh)
```

This installs skills into `.claude/skills/` in your current directory. Commit them to share with your team.

### Global (all repos)

```bash
bash <(curl -fsSL https://raw.githubusercontent.com/screaming-in-space/agent-tools/main/install.sh) --global
```

This installs skills into `~/.claude/skills/` so they're available in every repo.

### Update

Re-run the install command. Existing files are overwritten with the latest versions.

## Usage

**Note:** After installing or updating skills, restart Claude Code (terminal or desktop app) for new skills to register.

Once installed, skills are available as slash commands in any Claude Code session:

```
/agentify
```

## Contributing

Add new skills under `.claude/skills/<skill-name>/SKILL.md`. Each skill must include frontmatter:

```yaml
---
name: skill-name
description: One-line description of what the skill does.
user-invocable: true
---
```
