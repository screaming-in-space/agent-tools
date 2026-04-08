# Structure

Project architecture and file organization for agent-tools (.NET agents).

## Solution Layout

```
agents/dotnet/
├── docs/
│   ├── RULES.md                       # Technical constraints, agent patterns, rejected patterns
│   └── STRUCTURE.md                   # This file
├── src/
│   ├── Directory.Build.props          # Shared project settings (TFM, nullable, test auto-wiring)
│   ├── Directory.Packages.props       # Central Package Management — all NuGet versions here
│   ├── Test.Build.props               # Auto-imported by *.Tests projects (xUnit, NSubstitute, coverlet)
│   ├── ContextCartographer/           # Agent: markdown directory → structured context map
│   │   ├── Telemetry/
│   │   │   ├── ActivityExtensions.cs   # Null-safe fluent extensions on Activity?
│   │   │   ├── CartographerMetrics.cs  # Meter + counters/histograms for agent runs
│   │   │   └── CartographerTrace.cs   # Static ActivitySource for spans
│   │   ├── Tools/
│   │   │   └── FileTools.cs           # File system tools (list, read, extract, write)
│   │   ├── ContextCartographer.csproj
│   │   ├── Program.cs                 # System.CommandLine CLI, Serilog, agent pipeline
│   │   └── SystemPrompt.cs            # Parameterized system prompt builder
│   └── ContextCartographer.Tests/     # Unit tests for FileTools and SystemPrompt
│       ├── ContextCartographer.Tests.csproj
│       ├── FileToolsTests.cs
│       └── SystemPromptTests.cs
├── .github/
│   └── copilot-instructions.md        # AI agent project identity and philosophy
├── .editorconfig                      # Code style enforcement
├── ContextCartographer.slnx           # Solution manifest
├── global.json                        # SDK pin
├── nuget.config                       # Package source mapping
└── README.md                          # Quick start, conventions, adding new agents
```

## Projects

### ContextCartographer

Console agent. Scans a markdown directory and produces a structured context map (`CONTEXT.md`) using M.E.AI tool calling.

| File | Purpose |
|------|---------|
| `Program.cs` | System.CommandLine `RootCommand` definition, Serilog bootstrap, `ChatClientBuilder` pipeline with OTel + logging, `GetResponseAsync` call |
| `SystemPrompt.cs` | Static `Build(targetPath, outputPath)` — returns the full system prompt with workflow steps and output format |
| `Tools/FileTools.cs` | Four static tools: `ListMarkdownFiles`, `ReadFileContent`, `ExtractStructure`, `WriteOutput`. Path-sandboxed to `RootDirectory`. |
| `Telemetry/CartographerTrace.cs` | Static `ActivitySource("ContextCartographer")` with `StartSpan` helper — borrowed from Continuum Engine |
| `Telemetry/CartographerMetrics.cs` | Static `Meter("ContextCartographer")` — files discovered, files read, tool invocations, run duration |
| `Telemetry/ActivityExtensions.cs` | Null-safe fluent extensions: `WithTag`, `RecordError`, `SetSuccess` — borrowed from Continuum Engine |

**Depends on:** Microsoft.Extensions.AI, Microsoft.Extensions.AI.OpenAI, OpenAI, System.CommandLine, Serilog, Serilog.Extensions.Logging, Serilog.Sinks.Console

### ContextCartographer.Tests

Unit tests for tool methods and system prompt builder.

| File | Purpose |
|------|---------|
| `FileToolsTests.cs` | Tests all four tool methods — valid inputs, edge cases, path traversal rejection |
| `SystemPromptTests.cs` | Verifies built prompt contains tool names, output format, and runtime paths |

**Depends on:** ContextCartographer (project ref), xUnit, NSubstitute (auto-imported via `Test.Build.props`)

## Conventions

- **One agent, one project.** Each agent is a standalone console app in its own directory under `src/`.
- **Tools in `Tools/`.** Static classes with `[Description]` attributes. Registered via `AIFunctionFactory.Create`.
- **Telemetry in `Telemetry/`.** Static `ActivitySource` + `Meter` classes. Zero overhead when no listener is attached.
- **System prompt is testable code.** Static `Build` method with runtime parameters. Unit-tested for expected content.
- **Test auto-wiring.** Projects ending in `.Tests` automatically get test infrastructure via `Test.Build.props`.
- **No shared agent library.** If two agents need the same utility, duplicate it until the third agent proves a shared lib is needed (YAGNI).
