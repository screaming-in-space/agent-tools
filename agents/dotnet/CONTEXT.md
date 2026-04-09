# Context Map

> Source: dotnet
> Files: 3 markdown files

## Overview

This directory contains documentation for .NET agents in the screaming-in-space project. The README provides comprehensive agent architecture and usage instructions, while copilot-instructions.md is a thin shim pointing to other docs.

## File Index

Every markdown file gets exactly one row. No file is omitted.

| File | Purpose | Key Topics |
|------|---------|------------|
| `README.md` | Comprehensive documentation for .NET agents including architecture, conventions, quick start, model configuration, and agent deployment. | Agents, Architecture, Conventions, Quick Start, Model Configuration, Adding Agent |
| `github/copilot-instructions.md` | Thin shim layer that points to other documentation files (RULES.md and STRUCTURE.md). All technical constraints live exclusively in those docs. | shim, docs |

## Themes

### Agents Documentation
Agents documentation covers the structure, conventions, setup, and deployment of .NET agents for AI-assisted development workflows.

Files: `README.md`
Files: `github/copilot-instructions.md`

### Project Guidelines Shim
Project guidelines are implemented as thin shim files that reference other documentation rather than containing technical details.

Files: `github/copilot-instructions.md`

## Cross-References

- `github/copilot-instructions.md` references `docs/RULES.md` and `docs/STRUCTURE.md` via "thin shim layers"
- `README.md` references [Continuum Engine](https://github.com/screaming-in-space/continuum-engine) and [LM Studio](https://lmstudio.ai)

## Reading Order

1. `README.md` - Foundational context for .NET agent architecture, conventions, and usage
2. `github/copilot-instructions.md` - Understanding the shim layer that points to other docs

## Boundaries

- This directory does not contain actual documentation files (RULES.md, STRUCTURE.md) which are referenced but not stored here.
- Technical constraints and project structure live exclusively in those two external docs.
