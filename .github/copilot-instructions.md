# Copilot Instructions
<!-- Thin shim — all rules and structure live in the docs below. Do not duplicate here. -->

## Project

**agent-tools** — Claude Code skills and standalone .NET 10 agents for AI-assisted codebase analysis using M.E.AI (Microsoft.Extensions.AI) with local LLMs via Ollama/LM Studio.

## Rules & structure

- **[context/RULES.md](../context/RULES.md)** — Technical constraints, coding patterns, M.E.AI conventions, rejected patterns. Read before any code change.
- **[context/STRUCTURE.md](../context/STRUCTURE.md)** — Project architecture, directory tree, file map, project descriptions.

## Quick reference

- .NET 10 / C# 14.0. File-scoped namespaces. Primary constructors. Braces always required.
- No DI container, no Aspire, no Semantic Kernel. Console apps with manual wiring.
- Tools are `public static` methods with `[Description]` attributes, return `string`, errors as strings not exceptions.
- `SemaphoreSlim` for all locking — never `lock` in code called from async contexts.
- `async` all the way — propagate `CancellationToken` on every public method.
- Central Package Management — versions in `Directory.Packages.props` only.
- Test naming: `MethodName_Condition_ExpectedBehavior`. xUnit + NSubstitute.
- Use `InternalsVisibleTo` for test projects instead of making internal methods public for testing.

## Build & test

```bash
dotnet build
dotnet test
