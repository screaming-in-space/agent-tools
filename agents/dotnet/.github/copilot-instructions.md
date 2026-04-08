# Copilot Instructions - Agent Tools
<!-- last reviewed: 2026-06-01 -->

## Identity

Agent Tools is a collection of standalone .NET agents for AI-assisted development workflows.
Each agent is a console app with a narrow tool set, a system prompt, and a specific job.
Built on .NET 10, Microsoft.Extensions.AI, and OpenAI-compatible endpoints.
One developer. Minimal dependencies. Each agent should try and be self-contained.

## Philosophy

- Build on Microsoft.Extensions.AI abstractions (`IChatClient`, `AIFunctionFactory`) — not framework-level orchestration.
- Each agent should try to be a single console app. No shared host, no DI container, no Aspire orchestration
- At most, some type of storage or persistence layer will be allowed.
- Tools are pure functions with `[Description]` attributes. The LLM dispatches; the tool executes.
- System prompts are code — built by a static method, testable, parameterized.
- Agents connect to any OpenAI-compatible endpoint. No provider lock-in.
- Explicit over clever. Static methods over service classes. Direct file I/O over abstractions.

## Rule Sources

All coding rules, conventions, and patterns live in docs:

- **[docs/RULES.md](../docs/RULES.md)** — Technical constraints, M.E.AI patterns, tool design, agent conventions, rejected patterns.
- **[docs/STRUCTURE.md](../docs/STRUCTURE.md)** — Project architecture, directory tree, file map.

## Documentation Ownership

- **Technical constraints** live exclusively in `docs/RULES.md`. Don't duplicate elsewhere.
- **Project structure** lives exclusively in `docs/STRUCTURE.md`.
- **Agent README** — the workspace `README.md` covers quick start, conventions summary, and how to add new agents.
- **Repo-level README** — `../../README.md` covers the broader agent-tools repo (skills, installation).

## Development Environment

- Windows. Line endings: CRLF (`\r\n`).
- .NET 10 SDK / C# 14.0.
- Any OpenAI-compatible endpoint (LM Studio, Ollama, Azure OpenAI, OpenAI).
- `dotnet run --project src/ContextCartographer -- <directory> [--endpoint <url>]`
