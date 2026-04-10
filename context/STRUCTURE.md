# Structure

Project architecture and file organization for agent-tools (.NET agents).

## Repo Root

```
agent-tools/
├── .claude/
│   └── CLAUDE.md                        # Thin shim → context/RULES.md, context/STRUCTURE.md
├── .github/
│   └── copilot-instructions.md          # Thin shim → context/RULES.md, context/STRUCTURE.md
├── context/
│   ├── models/
│   │   ├── nvidia-nemotron-3-nano-4b.md   # Model capability profile
│   │   ├── google-gemma-4-31b-it.md       # Model capability profile
│   │   ├── google-gemma-4-26b-a4b-it.md   # Model capability profile
│   │   ├── google-gemma-4-e4b-it.md       # Model capability profile
│   │   └── _registry.json                 # Compact planner manifest (models)
│   ├── gpu/
│   │   ├── nvidia-rtx-4090-mobile.md      # GPU capability profile
│   │   ├── nvidia-rtx-5090-msi-suprim.md  # GPU capability profile
│   │   └── _registry.json                 # Compact planner manifest (GPUs)
│   ├── RULES.md                           # Technical constraints, agent patterns, rejected patterns
│   └── STRUCTURE.md                       # This file
├── AGENTS.md                            # Thin shim → context/RULES.md, context/STRUCTURE.md
├── README.md                            # Skills installation, agent overview
└── agents/dotnet/                       # .NET agent workspace (see below)
```

All three shim files (CLAUDE.md, copilot-instructions.md, AGENTS.md) point to the canonical docs below. Do not duplicate rules or structure in the shims.

## Solution Layout

```
agents/dotnet/
├── src/
│   ├── Directory.Build.props          # Shared project settings (TFM, nullable, analyzers, TreatWarningsAsErrors)
│   ├── Directory.Packages.props       # Central Package Management - all NuGet versions here
│   ├── Test.Build.props               # Auto-imported by *.Tests projects (xUnit, NSubstitute, coverlet)
│   ├── Agent.SDK/                     # Shared library: logging, telemetry, model config, console, tools
│   │   ├── Configuration/
│   │   │   ├── AgentModelOptions.cs    # Lightweight model config bound from appsettings.json
│   │   │   ├── AgentScanOptions.cs     # Scanner enable/disable flags from config + CLI
│   │   │   ├── EndpointHealthCheck.cs  # GET /v1/models validation before agent run
│   │   │   └── ModelRegistry.cs        # Compact model + GPU registry from _registry.json files
│   │   ├── Console/
│   │   │   ├── IAgentOutput.cs         # IAgentOutput interface, AgentRunSummary record, AgentConsole static accessor
│   │   │   ├── PlainAgentOutput.cs     # Headless output: routes all output through Serilog
│   │   │   ├── SpectreAgentOutput.cs   # Interactive output: Spectre.Console live rendering with scanner tree
│   │   │   ├── AgentTheme.cs           # Kurzgesagt color palette, logo, Spectre helpers
│   │   │   ├── AgentFileLog.cs         # Abstract base for buffered log files with SemaphoreSlim sync
│   │   │   ├── AgentErrorLog.cs        # Singleton error log (agent_error.log)
│   │   │   ├── AgentDebugLog.cs        # Singleton streaming debug log (agent_streaming_debug.log)
│   │   │   ├── AgentReasoningLog.cs    # REASONING.md writer with scanner traces
│   │   │   ├── StreamingInterceptor.cs # DelegatingChatClient routing thinking/response/tool tokens
│   │   │   └── ToolProgressWrapper.cs  # AIFunction wrapper reporting tool invocation progress
│   │   ├── Logging/
│   │   │   └── AgentLogging.cs         # Serilog bootstrap (Configure + CreateLoggerFactory)
│   │   ├── Telemetry/
│   │   │   ├── ActivityExtensions.cs   # Null-safe fluent extensions on Activity?
│   │   │   └── AgentTrace.cs           # Instance-based ActivitySource factory
│   │   ├── Tools/
│   │   │   ├── FileTools.cs            # File system tools (list, read, extract, write) + ResolveSafePath
│   │   │   ├── CodeCommentTools.cs     # Multi-language comment extraction (C#, Python, SQL, shell)
│   │   │   ├── GitTools.cs             # LibGit2Sharp tools (log, diff, stats, journal check)
│   │   │   ├── StructureTools.cs       # .NET project analysis (list, read, dependencies, architecture)
│   │   │   └── QualityTools.cs         # Roslyn-based C# analysis (complexity, anti-patterns, health grades)
│   │   └── Agent.SDK.csproj
│   ├── Agent.SDK.Tests/                # Unit + integration tests for Agent.SDK
│   │   ├── Agent.SDK.Tests.csproj
│   │   ├── FileToolsTests.cs           # File system tool tests + ResolveSafePath + Markdig edge cases
│   │   ├── IntegrationFileToolsTests.cs # Integration tests against the real repo root
│   │   ├── ModelRegistryTests.cs       # Registry load, parse, CanFit, malformed JSON, integration
│   │   ├── QualityToolsTests.cs        # Roslyn analysis, anti-patterns, health grades, editorconfig
│   │   ├── CodeCommentToolsTests.cs    # Multi-language comment extraction, code patterns
│   │   ├── StructureToolsTests.cs      # Project listing, dependencies, architecture detection
│   │   ├── GitToolsTests.cs            # Git log, stats, diff (integration against real repo)
│   │   ├── StreamingInterceptorTests.cs # DelegatingChatClient construction and delegation
│   │   ├── AgentFileLogTests.cs        # Error log + debug log lifecycle, truncation
│   │   ├── AgentReasoningLogTests.cs   # REASONING.md output format and content
│   │   └── ToolProgressWrapperTests.cs # Truncation behavior
│   ├── CrimeSceneInvestigator/         # Agent: scans a codebase and produces context directory
│   │   ├── Telemetry/
│   │   │   └── CsiTelemetry.cs         # CsiTrace (AgentTrace) + CsiMetrics (Meter + instruments)
│   │   ├── AgentCommandSetup.cs        # System.CommandLine definitions (Argument, Options, RootCommand)
│   │   ├── AgentContext.cs             # Record: target/repo/output paths, model options, chat client factory
│   │   ├── AgentInCommand.cs           # Orchestrator: setup → plan → run scanners → done
│   │   ├── ScannerPlanner.cs           # LLM-based model-to-scanner assignment
│   │   ├── ScannerRunner.cs            # Single scanner execution with timeout, retry, fallback
│   │   ├── PlannerPrompt.cs            # Planner system prompt with scanner manifests + model list
│   │   ├── SystemPrompt.cs             # Markdown scanner prompt (MAP.md)
│   │   ├── RulesPrompt.cs              # Rules scanner prompt (RULES.md)
│   │   ├── StructurePrompt.cs          # Structure scanner prompt (STRUCTURE.md)
│   │   ├── QualityPrompt.cs            # Quality scanner prompt (QUALITY.md)
│   │   ├── JournalPrompt.cs            # Journal scanner prompt (JOURNAL.md)
│   │   ├── DonePrompt.cs               # Done scanner prompt (DONE.md)
│   │   ├── appsettings.json            # Serilog overrides, Models:{key} config sections
│   │   ├── CrimeSceneInvestigator.csproj
│   │   └── Program.cs                  # Thin bootstrap: config, logging, CLI invoke, flush
│   └── CrimeSceneInvestigator.Tests/   # Agent-specific tests
│       ├── CrimeSceneInvestigator.Tests.csproj
│       ├── EndpointHealthCheckTests.cs  # Fake HTTP health check scenarios
│       ├── FallbackValidatorTests.cs    # IsSubstantiveMarkdown chatbot preamble detection
│       ├── PlannerPromptTests.cs        # Scanner manifests, complexity ratings, model configs
│       └── SystemPromptTests.cs         # Prompt content: tool names, output format, paths
│   ├── ModelBoss/                       # Agent: benchmarks local LLMs and produces ranked scorecards
│   │   ├── Benchmarks/
│   │   │   ├── AccuracyResult.cs        # Accuracy assessment record with individual check breakdowns
│   │   │   ├── AccuracyScorer.cs        # Deterministic output scoring (substrings, structure, bigram similarity)
│   │   │   ├── BenchmarkPrompt.cs       # Prompt definition with expected output criteria
│   │   │   ├── BenchmarkResult.cs       # Raw timing result per inference request
│   │   │   ├── BenchmarkRunner.cs       # Warmup + measured iterations with streaming token counting
│   │   │   ├── BenchmarkSuites.cs       # Built-in prompt suites (instruction, extraction, markdown, reasoning)
│   │   │   ├── ModelScorecard.cs        # Aggregated scorecard with composite, speed, accuracy metrics
│   │   │   └── ScorecardBuilder.cs      # Pure computation: raw results → scorecard with percentiles
│   │   ├── Tools/
│   │   │   ├── BenchmarkTools.cs        # Agent-callable tools wrapping BenchmarkRunner + AccuracyScorer
│   │   │   ├── GpuTools.cs              # GPU discovery and model-fit compatibility matrix
│   │   │   ├── ModelTools.cs            # Model registry, config resolution, endpoint queries
│   │   │   └── ReportTools.cs           # File output tool for writing benchmark reports
│   │   ├── BossAgent.cs                 # Orchestrator: resolve → validate → benchmark → score → report
│   │   ├── BossCommandSetup.cs          # System.CommandLine definitions (--models, --iterations, --category)
│   │   ├── BossPrompt.cs               # System prompt for LLM-driven benchmark workflow
│   │   ├── ReportFormatter.cs           # Markdown report builder from ranked scorecards
│   │   ├── appsettings.json             # Serilog overrides, Models:{key} config sections
│   │   ├── ModelBoss.csproj
│   │   └── Program.cs                   # Thin bootstrap: config, logging, CLI invoke, flush
│   └── ModelBoss.Tests/                 # Unit tests for ModelBoss
│       ├── ModelBoss.Tests.csproj
│       ├── AccuracyScorerTests.cs       # Accuracy scoring: substrings, structure, length, similarity, pass/fail
│       ├── BenchmarkSuitesTests.cs      # Suite categories, unique names, prompt validation
│       └── ScorecardBuilderTests.cs     # Aggregation, composite formula, percentiles, pass rate
├── .github/
│   └── copilot-instructions.md         # Thin shim (workspace-scoped)
├── .editorconfig                       # Code style + analyzer severity overrides
├── AgentTools.slnx                     # Solution manifest
├── global.json                         # SDK pin
├── nuget.config                        # Package source mapping
└── README.md                           # Quick start, conventions, adding new agents
```

## Projects

### Agent.SDK

Shared class library. Contains reusable infrastructure that every agent needs but that doesn't belong in each agent's codebase. This is the **one exception** to the "one agent, one project" rule — it exists because logging bootstrap, telemetry primitives, model configuration, console output, and file-system tools are pure plumbing with zero agent-specific logic.

| File | Purpose |
|------|---------|
| `Configuration/AgentModelOptions.cs` | Sealed record bound from `Models:{key}` in `appsettings.json`. Properties: `Endpoint`, `ApiKey`, `Model`, `Temperature?`, `TopP?`, `MaxOutputTokens?`. Static `Resolve(IConfiguration, configKey?)` does section lookup with `"default"` fallback. `ResolveAll` returns all non-embedding configs. |
| `Configuration/AgentScanOptions.cs` | Sealed record controlling which scanners are enabled. Bound from `AgentInCommand` section in `appsettings.json`. `FromCliOverride` parses `--scan` CLI option. |
| `Configuration/EndpointHealthCheck.cs` | Static `ValidateAsync` calls `GET /v1/models` on the configured endpoint. Verifies reachability and that the configured model is loaded. Returns `HealthCheckResult` record. |
| `Configuration/ModelRegistry.cs` | Compact model + GPU registry loaded from `context/models/_registry.json` and `context/gpu/_registry.json`. Records: `ModelRegistry`, `ModelEntry`, `GpuEntry`, `ScannerRatings`, `InferenceSettings`. Static `Load(repoRoot)` with graceful fallback to empty. |
| `Console/IAgentOutput.cs` | `IAgentOutput` interface (scanner lifecycle, tool progress, thinking, summary), `AgentRunSummary` record, `AgentConsole` static accessor. |
| `Console/PlainAgentOutput.cs` | Headless implementation of `IAgentOutput`. Routes all output through Serilog. No Spectre rendering. |
| `Console/SpectreAgentOutput.cs` | Interactive implementation of `IAgentOutput`. Spectre.Console live rendering with scanner tree, progress bar, spinner, and summary panel. |
| `Console/AgentTheme.cs` | Kurzgesagt color palette, Spectre.Console helpers (Logo, Divider), version info extraction. |
| `Console/AgentFileLog.cs` | Abstract base class for buffered log files with `SemaphoreSlim` sync and size-based rotation. |
| `Console/AgentErrorLog.cs` | Singleton error log (`agent_error.log`). Logs scanner failures with timestamps. |
| `Console/AgentDebugLog.cs` | Singleton streaming debug log (`agent_streaming_debug.log`) with 10MB rotation. |
| `Console/AgentReasoningLog.cs` | `ScannerTrace` data class and REASONING.md writer. Records tool calls, thinking, response per scanner. |
| `Console/StreamingInterceptor.cs` | `DelegatingChatClient` that intercepts streaming LLM content and routes thinking/response/tool tokens to `IAgentOutput`. |
| `Console/ToolProgressWrapper.cs` | `AIFunction` wrapper that reports tool invocation start/completion with friendly names and detail extraction. |
| `Logging/AgentLogging.cs` | Static `Configure(IConfiguration?)` bootstraps Serilog with console output. `CreateLoggerFactory()` bridges Serilog to `ILoggerFactory`. `suppressConsole` option for Spectre mode. |
| `Telemetry/AgentTrace.cs` | Instance-based `AgentTrace(string sourceName)` — each agent creates one with its own `ActivitySource` name. `StartSpan` helper returns `Activity?`. |
| `Telemetry/ActivityExtensions.cs` | Null-safe fluent extensions: `WithTag`, `RecordError`, `SetSuccess`. |
| `Tools/FileTools.cs` | Static tools: `ListMarkdownFiles`, `ReadFileContent`, `ExtractStructure`, `WriteOutput`, `FindRepoRoot`. Path-sandboxed via `ResolveSafePath`. Uses Markdig for AST-based markdown parsing. |
| `Tools/CodeCommentTools.cs` | Multi-language comment extraction: `ListSourceFiles`, `ExtractComments` (C#, Python, SQL, shell), `ExtractCodePatterns` (DI, base classes, interfaces, attributes). |
| `Tools/GitTools.cs` | LibGit2Sharp tools: `GetGitLog`, `GetGitDiff`, `GetGitStats`, `CheckJournalExists`. Path-sandboxed. |
| `Tools/StructureTools.cs` | .NET project analysis: `ListProjects`, `ReadProjectFile`, `MapDependencyGraph`, `DetectArchitecturePattern`. |
| `Tools/QualityTools.cs` | Roslyn-based C# analysis: `AnalyzeCSharpFile` (cyclomatic complexity, anti-patterns), `AnalyzeCSharpProject`, `AnalyzeSourceFile`, `CheckEditorConfig`. |

**Depends on:** LibGit2Sharp, Markdig, Microsoft.CodeAnalysis.CSharp, Microsoft.Extensions.AI, Spectre.Console, Microsoft.Extensions.Configuration.Abstractions, Microsoft.Extensions.Configuration.Binder, Serilog, Serilog.Extensions.Logging, Serilog.Settings.Configuration, Serilog.Sinks.Console

### CrimeSceneInvestigator

Console agent. Scans a codebase and produces a structured context directory (MAP.md, RULES.md, STRUCTURE.md, QUALITY.md, JOURNAL.md, DONE.md) using M.E.AI tool calling with LLM-planned model assignments.

| File | Purpose |
|------|---------|
| `Program.cs` | Thin bootstrap: builds `IConfiguration` from `appsettings.json` (CWD-based), configures logging + console, creates `AgentInCommand`, invokes CLI. |
| `AgentCommandSetup.cs` | System.CommandLine `RootCommand` factory — `--config-key`, `--output`, `--headless`, `--scan`, `--model` options. |
| `AgentContext.cs` | Record holding target/repo/output paths, model options, scan options. Builds OpenAI chat client pipeline with `StreamingInterceptor` and `FunctionInvocation` middleware. |
| `AgentInCommand.cs` | Orchestrator: setup → plan model assignments → run scanners → produce summary. Delegates to `ScannerPlanner` and `ScannerRunner`. Contains `IsSubstantiveMarkdown` fallback validation. |
| `ScannerPlanner.cs` | Asks the LLM to assign model config keys to scanners based on loaded models and scanner complexity. Parses JSON response with fallback. |
| `ScannerRunner.cs` | Runs a single scanner with timeout, retry (max 2 attempts), and fallback output handling. Saves substantive markdown as fallback if `WriteOutput` wasn't called. |
| `PlannerPrompt.cs` | Builds the planner system prompt: scanner manifests with complexity ratings, available model configs, expected JSON output format. |
| `SystemPrompt.cs` | Markdown scanner prompt — lists files, reads + extracts structure, composes MAP.md. |
| `RulesPrompt.cs` | Rules scanner prompt — extracts comments, code patterns, editorconfig into RULES.md. |
| `StructurePrompt.cs` | Structure scanner prompt — projects, dependencies, architecture into STRUCTURE.md. |
| `QualityPrompt.cs` | Quality scanner prompt — Roslyn analysis, hotspots, anti-patterns into QUALITY.md. |
| `JournalPrompt.cs` | Journal scanner prompt — git history into daily journal entries. |
| `DonePrompt.cs` | Done scanner prompt — completion checklist from prior scanner outputs into DONE.md. |
| `Telemetry/CsiTelemetry.cs` | `CsiTrace` — static `AgentTrace` instance named `"CrimeSceneInvestigator"`. `CsiMetrics` — `Meter` with `csi.*` instruments. |
| `appsettings.json` | Serilog overrides + `Models:{key}` sections (`default` = chat model, `embedding` = retrieval model). |

**Depends on:** Agent.SDK (project ref), Microsoft.Extensions.AI, Microsoft.Extensions.AI.OpenAI, Microsoft.Extensions.Configuration.Json, OpenAI, System.CommandLine

### Agent.SDK.Tests

Unit and integration tests for shared SDK components.

| File | Purpose |
|------|---------|
| `FileToolsTests.cs` | Tests all four tool methods plus `ResolveSafePath` — valid inputs, edge cases, path traversal rejection, Markdig correctness. |
| `IntegrationFileToolsTests.cs` | Integration tests running tools against the real agent-tools repo root. |
| `ModelRegistryTests.cs` | Registry load, model/GPU parsing, `CanFit`, malformed JSON fallback, real registry integration test. |
| `QualityToolsTests.cs` | Roslyn analysis: cyclomatic complexity, anti-patterns, health grades, editorconfig checking. |
| `CodeCommentToolsTests.cs` | Multi-language comment extraction (C#/Python/SQL/shell), code patterns, DI detection. |
| `StructureToolsTests.cs` | Project listing, .csproj parsing, dependency graph, architecture detection. |
| `GitToolsTests.cs` | Git log, stats, diff, journal check (integration against real repo). |
| `StreamingInterceptorTests.cs` | DelegatingChatClient construction and delegation verification. |
| `AgentFileLogTests.cs` | Error log + debug log lifecycle, directory creation, truncation. |
| `AgentReasoningLogTests.cs` | REASONING.md output format, tool calls, thinking/response content. |
| `ToolProgressWrapperTests.cs` | Truncation behavior via `AgentFileLog.Truncate`. |

**Depends on:** Agent.SDK (project ref), xUnit, NSubstitute (auto-imported via `Test.Build.props`)

### CrimeSceneInvestigator.Tests

Agent-specific tests for prompts, health check, and fallback validation.

| File | Purpose |
|------|---------|
| `EndpointHealthCheckTests.cs` | Tests health check against fake HTTP responses — model loaded, missing, endpoint errors, case-insensitive matching. |
| `FallbackValidatorTests.cs` | `IsSubstantiveMarkdown` — chatbot preamble detection, short content rejection, real scanner output acceptance. |
| `PlannerPromptTests.cs` | Scanner manifests, complexity ratings, disabled scanner exclusion, model config inclusion. |
| `SystemPromptTests.cs` | Verifies built prompt contains tool names, output format, and runtime paths. |

**Depends on:** CrimeSceneInvestigator (project ref), xUnit, NSubstitute (auto-imported via `Test.Build.props`)

### ModelBoss

Console agent. Benchmarks local LLM models against built-in prompt suites and produces ranked scorecards with composite scores combining speed, accuracy, and pass rate. No LLM-as-judge — all scoring is deterministic (substring matching, structure validation, bigram similarity).

| File | Purpose |
|------|---------|
| `Program.cs` | Thin bootstrap: builds `IConfiguration` from `appsettings.json`, configures logging + console, creates `BossAgent`, invokes CLI. Manual wiring, no DI container. |
| `BossCommandSetup.cs` | System.CommandLine `RootCommand` factory — `--models`, `--iterations`, `--category`, `--output`, `--repo-root`, `--headless` options. |
| `BossAgent.cs` | Record with `ILoggerFactory` + `IConfiguration`. Orchestrates: resolve CLI → load registries → validate endpoint → run benchmarks per model → score accuracy → build scorecards → write report. |
| `BossPrompt.cs` | System prompt for LLM-driven workflow: discover hardware, check loaded models, run benchmarks, compose and save report. |
| `ReportFormatter.cs` | Internal. Formats ranked markdown report with rankings table, hardware summary, per-model scorecards, recommendations, and methodology. |
| `Benchmarks/BenchmarkRunner.cs` | Executes prompts against a model endpoint with warmup + measured iterations. Streaming token counting with `Stopwatch`-based timing. |
| `Benchmarks/BenchmarkSuites.cs` | Built-in prompt suites: `InstructionFollowing` (format compliance), `Extraction` (structured data), `MarkdownGeneration` (clean markdown), `Reasoning` (multi-step analysis). |
| `Benchmarks/BenchmarkPrompt.cs` | Sealed record defining a benchmark prompt with `ExpectedOutput` criteria (required/forbidden substrings, structure, reference, length bounds, pass threshold). |
| `Benchmarks/BenchmarkResult.cs` | Raw timing result per inference request: duration, TTFT, token counts, raw output, success flag. |
| `Benchmarks/AccuracyScorer.cs` | Deterministic scoring: length, required substrings, forbidden substrings, structural elements, bigram similarity to reference. Weighted composite per check. |
| `Benchmarks/AccuracyResult.cs` | Accuracy assessment record with individual `AccuracyCheck` breakdowns. |
| `Benchmarks/ModelScorecard.cs` | Aggregated scorecard: median tok/s, P5 tok/s, TTFT, accuracy mean, pass rate, composite. `ModelSummary` for registry metadata. |
| `Benchmarks/ScorecardBuilder.cs` | Pure computation: raw results → percentile-based speed metrics → composite score `(accuracy × 0.6) + (normalized_speed × 0.3) + (pass_rate × 0.1)`. |
| `Tools/BenchmarkTools.cs` | Agent-callable tools wrapping `BenchmarkRunner` + `AccuracyScorer`. `RunSpeedBenchmarkAsync`, `RunAccuracyBenchmarkAsync`, `RunFullSuiteAsync`. |
| `Tools/ModelTools.cs` | Registry data, endpoint queries, config resolution. `ListRegisteredModels`, `ListConfiguredModels`, `GetLoadedModelsAsync`, `GetModelProfile`. |
| `Tools/GpuTools.cs` | GPU discovery. `ListGpus`, `CheckModelFit` (compatibility matrix at Q4/Q8 quantization). |
| `Tools/ReportTools.cs` | File output. `WriteReportAsync` with filename sanitization. |
| `appsettings.json` | Serilog overrides + `Models:{key}` sections (default, qwen, gemma, gemma-e4b, gemma-26b, glm, embedding). |

**Depends on:** Agent.SDK (project ref), Microsoft.Extensions.AI, Microsoft.Extensions.AI.OpenAI, Microsoft.Extensions.Configuration.Json, OpenAI, System.CommandLine

### ModelBoss.Tests

Unit tests for ModelBoss benchmark engine and scoring.

| File | Purpose |
|------|---------|
| `AccuracyScorerTests.cs` | Tests `Score` through public API: required/forbidden substrings, length validation, structural elements, reference similarity, composite pass/fail, null/empty edge cases. |
| `ScorecardBuilderTests.cs` | Aggregation: empty results, multi-prompt, failed benchmark exclusion, composite formula weights, speed normalization cap, pass rate, registry metadata preservation. |
| `BenchmarkSuitesTests.cs` | Suite integrity: all categories present, count matches sum-of-parts, unique names, non-empty messages, expected output, reasonable timeouts, minimum prompt counts. |

**Depends on:** ModelBoss (project ref), xUnit, NSubstitute (auto-imported via `Test.Build.props`)

## Conventions

- **One agent, one project.** Each agent is a standalone console app in its own directory under `src/`.
- **Agent.SDK is the exception.** Shared logging bootstrap, telemetry primitives, model configuration, console output, endpoint health checks, and reusable tools live here. Agent-specific logic never goes in the SDK — only plumbing that every agent would otherwise duplicate identically.
- **Shared tools in `Agent.SDK.Tools`.** Generic tools (`FileTools`, `CodeCommentTools`, `GitTools`, `StructureTools`, `QualityTools`) that any agent can reuse. Agent-specific tools go in the agent's own `Tools/` directory. Registered via `AIFunctionFactory.Create` in scanner orchestration.
- **Console output in `Agent.SDK.Console`.** `IAgentOutput` interface with headless (`PlainAgentOutput`) and interactive (`SpectreAgentOutput`) implementations. Log files, reasoning traces, and streaming interceptors live here.
- **Telemetry in `Telemetry/`.** Agent-specific `AgentTrace` instance + `Meter` class. Zero overhead when no listener is attached.
- **System prompt is testable code.** Static `Build` method with runtime parameters. Unit-tested for expected content.
- **Test auto-wiring.** Projects ending in `.Tests` automatically get test infrastructure via `Test.Build.props`.
- **Configuration via `appsettings.json`.** Log-level overrides and runtime settings live in config, not hardcoded in bootstrap code.
- **Analyzers enforced.** `TreatWarningsAsErrors` + `AnalysisLevel=latest-recommended` in `Directory.Build.props`. Style diagnostics as suggestions in `.editorconfig`.
