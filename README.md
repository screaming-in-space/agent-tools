# Agent Tools

Shared [Claude Code](https://claude.ai/claude-code) skills and standalone .NET 10 agents for AI-assisted development workflows.

## Layout

```
agent-tools/
├── .claude/CLAUDE.md              Thin shim → context/RULES.md, context/STRUCTURE.md
├── agents/dotnet/                 .NET agent workspace (solution, projects, tests)
├── context/
│   ├── RULES.md                   Technical constraints, coding patterns, rejected patterns
│   ├── STRUCTURE.md               Project architecture, directory tree, file map
│   └── models/                    Model capability profiles for planner evaluation
├── skills/                        Shared Claude Code skills
│   ├── agentify/                  Bootstrap repos with ML context files
│   ├── claude-api-qa-creator/     Generate QA helpers from repo analysis
│   └── install.sh                 Skill installer (local or global)
└── README.md                      This file
```

## Agents

| Agent | Description |
|-------|-------------|
| [`CrimeSceneInvestigator`](agents/dotnet/src/CrimeSceneInvestigator/) | Multi-scanner codebase intelligence agent. Scans a git repo and produces structured context files for LLM consumption. |

### Running an agent

```bash
cd agents/dotnet
dotnet run --project src/CrimeSceneInvestigator -- <directory> [--config-key <key>] [--model <name>] [--scan <scanners>] [--headless]
```

| Option | Description |
|--------|-------------|
| `<directory>` | Target directory to scan (must be inside a git repo) |
| `--config-key` | Model config section in `appsettings.json` (default: `"default"`) |
| `--model` | Override model name for all scanners (bypasses planner) |
| `--scan` | Comma-separated scanners: `markdown,rules,structure,quality,journal` |
| `--headless` | Disable rich terminal UI, use plain log output |

Requires .NET 10 SDK and an OpenAI-compatible endpoint (e.g., [LM Studio](https://lmstudio.ai), [Ollama](https://ollama.com)).

## Skills

| Skill | Description |
|-------|-------------|
| [`agentify`](skills/agentify/) | Bootstrap a repo with ML context files (CLAUDE.md, copilot-instructions.md, AGENTS.md) as thin shims pointing to centralized docs |
| [`claude-api-qa-creator`](skills/claude-api-qa-creator/) | Generate QA helpers from repo analysis |

## Installation

### Into current repo (default)

```bash
bash <(curl -fsSL https://raw.githubusercontent.com/screaming-in-space/agent-tools/main/skills/install-skills.sh)
```

Installs skills into `.claude/skills/` in your current directory. Commit them to share with your team.

### Global (all repos)

```bash
bash <(curl -fsSL https://raw.githubusercontent.com/screaming-in-space/agent-tools/main/skills/install-skills.sh) --global
```

Installs skills into `~/.claude/skills/` so they're available in every repo.

### Update

Re-run the install command. Existing files are overwritten with the latest versions.

## Usage

After installing or updating skills, restart Claude Code for new skills to register.

```
/agentify
```

## Contributing

Add new skills under `skills/<skill-name>/SKILL.md`. Each skill must include frontmatter:

```yaml
---
name: skill-name
description: One-line description of what the skill does.
user-invocable: true
---
```
