# Agents

Standalone .NET agents for AI-assisted development workflows. Each agent is a console app with a narrow tool set and a specific job.

## Architecture

```
agents/
├── global.json                    SDK version pin
├── nuget.config                   Package source mapping
├── .editorconfig                  Code style enforcement
└── src/
    ├── Directory.Build.props      Shared project settings (TFM, nullability, test auto-wiring)
    ├── Directory.Packages.props   Central Package Management — all versions here
    ├── Test.Build.props           Auto-imported by *.Tests projects (xUnit, NSubstitute, coverlet)
    ├── ContextCartographer/       Markdown directory → structured context map
    └── ContextCartographer.Tests/ Unit tests for FileTools and SystemPrompt
```

## Conventions

Follows the same patterns as [Continuum Engine](https://github.com/screaming-in-space/continuum-engine):

- **.NET 10 / C# 14.0** — file-scoped namespaces, primary constructors, braces always required
- **Central Package Management** — versions in `Directory.Packages.props`, never in individual `.csproj` files
- **xUnit + NSubstitute** — `Assert.*` assertions, `Method_Condition_Behavior` naming, one test class per file
- **Test auto-wiring** — projects ending in `.Tests` automatically get test infrastructure via `Test.Build.props`

## Quick Start

```bash
# Build everything
dotnet build src/ContextCartographer

# Run tests
dotnet test src/ContextCartographer.Tests

# Run the agent
dotnet run --project src/ContextCartographer -- <directory> [--endpoint <url>]
```

Requires .NET 10 SDK and an OpenAI-compatible endpoint (e.g., [LM Studio](https://lmstudio.ai) at `http://localhost:1234/v1`).

## Adding a New Agent

1. Create `src/NewAgent/NewAgent.csproj` — reference packages without versions (CPM owns them)
2. Add any new package versions to `src/Directory.Packages.props`
3. Create `src/NewAgent.Tests/NewAgent.Tests.csproj` — only needs `<ProjectReference>` to the agent
4. Test infrastructure (xUnit, NSubstitute, coverlet) is auto-imported by `Test.Build.props`
