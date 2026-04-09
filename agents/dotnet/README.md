# Agents

Standalone .NET agents for AI-assisted development workflows. Each agent is a console app with a narrow tool set and a specific job.

## Architecture

```
agents/dotnet/
├── global.json                    SDK version pin
├── nuget.config                   Package source mapping
├── .editorconfig                  Code style enforcement
├── AgentTools.slnx                Solution manifest
└── src/
    ├── Directory.Build.props      Shared project settings (TFM, nullability, test auto-wiring)
    ├── Directory.Packages.props   Central Package Management - all versions here
    ├── Test.Build.props           Auto-imported by *.Tests projects (xUnit, NSubstitute, coverlet)
    ├── Agent.SDK/                 Shared: logging, telemetry, model config, file tools (Markdig)
    ├── Agent.SDK.Tests/           Unit + integration tests for Agent.SDK
    ├── CrimeSceneInvestigator/    Markdown directory → structured context map
    └── CrimeSceneInvestigator.Tests/
```

## Conventions

Follows the same patterns as [Continuum Engine](https://github.com/screaming-in-space/continuum-engine):

- **.NET 10 / C# 14.0** - file-scoped namespaces, primary constructors, braces always required
- **Central Package Management** - versions in `Directory.Packages.props`, never in individual `.csproj` files
- **Agent.SDK** - shared library for logging (`AgentLogging`), telemetry (`AgentTrace`, `ActivityExtensions`), model config (`AgentModelOptions`), endpoint health checks (`EndpointHealthCheck`), and reusable file tools (`FileTools` with Markdig-based markdown parsing). The one exception to "one agent, one project".
- **`appsettings.json`** - Serilog overrides and runtime configuration. No hardcoded log-level overrides.
- **xUnit + NSubstitute** - `Assert.*` assertions, `Method_Condition_Behavior` naming, one test class per file
- **Test auto-wiring** - projects ending in `.Tests` automatically get test infrastructure via `Test.Build.props`

## Quick Start

```bash
# Build everything
dotnet build src/CrimeSceneInvestigator

# Run tests
dotnet test src/CrimeSceneInvestigator.Tests

# Run the agent (uses Models:default from appsettings.json)
dotnet run --project src/CrimeSceneInvestigator -- <directory>

# Run with a specific model config section
dotnet run --project src/CrimeSceneInvestigator -- <directory> --config-key openai

# Run with a custom output path
dotnet run --project src/CrimeSceneInvestigator -- <directory> --output ./my-context.md

# Publish as a single-file self-contained .exe
dotnet publish src/CrimeSceneInvestigator -c Release -r win-x64 -o publish/
# Result: publish/CrimeSceneInvestigator.exe + publish/appsettings.json
```

Model configuration is defined in `appsettings.json` under `Models:{key}`:

```json
{
  "Models": {
    "default": {
      "Endpoint": "http://localhost:1234/v1",
      "ApiKey": "no-key",
      "Model": "unsloth/nvidia-nemotron-3-nano-4b",
      "Temperature": 0.3,
      "MaxOutputTokens": 4096
    }
  }
}
```

Properties: `Endpoint`, `ApiKey`, `Model` (required), `Temperature`, `TopP`, `MaxOutputTokens` (optional - omit to use server defaults). The `--config-key` flag selects which section to use (default: `"default"`). The agent validates the endpoint and model availability via `GET /v1/models` before running.

Requires .NET 10 SDK and an OpenAI-compatible endpoint (e.g., [LM Studio](https://lmstudio.ai) at `http://localhost:1234/v1`).

## Adding a New Agent

1. Create `src/NewAgent/NewAgent.csproj` - `<ProjectReference>` to Agent.SDK, plus M.E.AI packages without versions (CPM owns them)
2. Add any new package versions to `src/Directory.Packages.props`
3. Create `src/NewAgent/appsettings.json` - Serilog overrides, `<Content CopyToOutputDirectory="PreserveNewest" />`
4. Create `src/NewAgent.Tests/NewAgent.Tests.csproj` - only needs `<ProjectReference>` to the agent
5. Test infrastructure (xUnit, NSubstitute, coverlet) is auto-imported by `Test.Build.props`
