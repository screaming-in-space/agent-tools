# AGENTS.md
<!-- Thin shim — all rules and structure live in the docs below. Do not duplicate here. -->

**CrimeSceneInvestigator** — Multi-scanner codebase intelligence agent. Scans a git repo and produces structured context files in `./context/` for consumption by other LLMs.

## Rules & structure

- **[../../../../context/RULES.md](../../../../context/RULES.md)** — Technical constraints, coding patterns, M.E.AI conventions, rejected patterns.
- **[../../../../context/STRUCTURE.md](../../../../context/STRUCTURE.md)** — Project architecture, directory tree, file map, project descriptions.

## Agent overview

CSI runs 6 sequential scanners, each with its own system prompt and tool set:

| Scanner | Output | Complexity |
|---------|--------|------------|
| Markdown | `MAP.md` | light |
| Rules | `RULES.md` | heavy |
| Structure | `STRUCTURE.md` | light |
| Quality | `QUALITY.md` | heavy |
| Journal | `JOURNAL.md` + `journal/*.md` | medium |
| Done | `DONE.md` | medium |

A planner step assigns the best loaded model to each scanner based on capability profiles in `context/models/`.

## Key files

| File | Purpose |
|------|---------|
| `AgentInCommand.cs` | Scanner orchestration, retry, timeout, fallback validation |
| `AgentContext.cs` | Chat client pipeline, model config, repo root resolution |
| `AgentCommandSetup.cs` | CLI definitions (`<directory>`, `--config-key`, `--model`, `--scan`, `--headless`) |
| `PlannerPrompt.cs` | Model-to-scanner assignment prompt |
| `SystemPrompt.cs` | Markdown scanner prompt |
| `RulesPrompt.cs`, `StructurePrompt.cs`, `QualityPrompt.cs`, `JournalPrompt.cs`, `DonePrompt.cs` | Scanner-specific prompts |
