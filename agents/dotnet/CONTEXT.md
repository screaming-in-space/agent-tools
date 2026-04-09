# Context Map

> Source: .dotnet
> Files: 2 markdown files

## Overview

This directory contains the core documentation for the CrimeSceneInvestigator agent, including architecture details, conventions, quick start instructions, and a shim file that points to external docs. It defines how agents are structured and configured.

## File Index

| File | Purpose | Key Topics |
|------|---------|------------|
| `README.md` | Defines the overall structure of the .NET agents project, including architecture, conventions, quick start steps, model configuration, and adding new agents. | Architecture, Conventions, Quick Start, Model Configuration, Adding Agents |
| `copilot-instructions.md` | Contains thin guidance for Copilot usage, directing to external docs (RULES.md, STRUCTURE.md) which hold actual technical constraints. | Project Guidelines, Shim Files |

## Themes

### Project Architecture
This theme covers the overall structure of the .NET agents project as defined in README.md.

Files: `README.md`

### Agent Configuration & Quick Start
This theme details how to configure and run agents using README.md's instructions.

Files: `README.md`

## Cross-References

- `copilot-instructions.md` references `docs/RULES.md` and `docs/STRUCTURE.md` via external documentation links.

## Reading Order

1. `README.md` - Provides foundational context on project structure, conventions, quick start instructions.
2. `copilot-instructions.md` - Explains how Copilot should interact with the agent system using external docs.

## Boundaries

- This directory does not contain `docs/RULES.md` or `docs/STRUCTURE.md`; they are external docs referenced by copilot-instructions.md.
- For technical constraints and project structure details outside this dir, see `docs/RULES.md` and `docs/STRUCTURE.md`.