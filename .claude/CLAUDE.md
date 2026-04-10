# CLAUDE.md
<!-- Thin shim — all rules and structure live in the docs below. Do not duplicate here. -->

**agent-tools** — Claude Code skills and standalone .NET 10 agents for AI-assisted development workflows.

## Rules & structure

- **[context/RULES.md](context/RULES.md)** — Technical constraints, coding patterns, M.E.AI conventions, rejected patterns.
- **[context/STRUCTURE.md](context/STRUCTURE.md)** — Project architecture, directory tree, file map, project descriptions.

Read both before making changes to anything under `agents/dotnet/`.

## Build & test

```bash
cd agents/dotnet
dotnet build
dotnet test
```

## Model context

Model capability profiles live in `context/models/`. The planner reads these when assigning models to scanners. See [context/models/](context/models/) for existing profiles.
