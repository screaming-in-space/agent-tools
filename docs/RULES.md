# Rules

Technical constraints, agent patterns, and rejected patterns for agent-tools.

---

## Design Principles

These apply everywhere. No exceptions.

- **Single Responsibility** — one agent, one job. One tool, one side effect. One class, one reason to change.
- **DRY** — one code path to each concern. If argument parsing exists in two places, one will drift.
- **KISS** — the simplest correct solution. If a static method works, don't introduce a service class.
- **YAGNI** — don't build it until you need it. Don't add DI, hosting, or Aspire until the agent genuinely requires them.
- **Explicit over clever** — static methods, direct file I/O, raw string building. No hidden behavior.

---

## Stack

- .NET 10 / C# 14.0 — `LangVersion` inherited from `Directory.Build.props`, nullable enabled, implicit usings
- Central Package Management (`Directory.Packages.props`) — all NuGet versions pinned centrally
- **Agent.SDK** — shared class library for logging bootstrap (`AgentLogging`) and telemetry primitives (`AgentTrace`, `ActivityExtensions`). The one exception to "one agent, one project".
- Microsoft.Extensions.AI 10.4.1 / Microsoft.Extensions.AI.OpenAI 10.4.1
- OpenAI SDK 2.10.0 (used for `OpenAIClient` + `ApiKeyCredential`)
- System.CommandLine 3.0.0-preview.2 for CLI argument parsing
- Serilog 4.3.1 + Serilog.Sinks.Console 6.1.1 + Serilog.Settings.Configuration 10.0.0 for structured logging
- Microsoft.Extensions.Configuration.Json 10.0.5 for `appsettings.json` loading
- `System.Diagnostics.ActivitySource` / `System.Diagnostics.Metrics.Meter` for telemetry (BCL, no OTel SDK export for CLI)
- xUnit 2.9.3 + NSubstitute 5.3.0 for testing
- No DI container — agents are console apps with manual wiring
- No Aspire — agents run standalone against any OpenAI-compatible endpoint

---

## M.E.AI / IChatClient

### Priority Order for AI Abstractions

1. **Microsoft.Extensions.AI** (`IChatClient`, `AIFunctionFactory`, `ChatClientBuilder`) — first-party abstractions. Use for all LLM work.
2. **OpenAI SDK** (`OpenAIClient`, `ApiKeyCredential`) — used only to create the underlying chat client. Never call OpenAI-specific APIs directly when M.E.AI abstractions exist.

### Do

- Build the client pipeline via `ChatClientBuilder` with `UseOpenTelemetry`, then `UseFunctionInvocation`.
- Pass a `LoggerFactory` created from Serilog to both `UseOpenTelemetry` and `UseFunctionInvocation`.
- Set `MaximumIterationsPerRequest` to a reasonable bound (default 50) to prevent runaway tool loops.
- Use `AIFunctionFactory.Create()` with static methods that have `[Description]` attributes.
- Use `ChatOptions.Tools` to pass the tool array — tools are per-request, not per-client.
- Pass model as `"local"` when no model is specified — `ChatClient` requires a non-null string, but LM Studio ignores it when only one model is loaded.
- Use `AsIChatClient()` to bridge from OpenAI's `ChatClient` to M.E.AI's `IChatClient`.

### Don't

- Don't add `FunctionInvokingChatClient` to the pipeline AND manually dispatch tools. Pick one.
- Don't hardcode provider-specific API URLs. Accept endpoint as a CLI argument or environment variable.
- Don't hold conversation state in the client. Build the message list fresh for each `GetResponseAsync` call.

---

## Agent Design

### Console App Pattern

Each agent follows the same structure:

```
1. Program.cs (thin bootstrap):
   a. Build IConfiguration from appsettings.json (base path: CWD)
   b. AgentLogging.Configure(configuration) — Serilog reads overrides from config
   c. Create ILoggerFactory + ILogger<AgentInCommand>
   d. Instantiate AgentInCommand(logger, configuration)
   e. AgentCommandSetup.CreateRootCommand(agent.RunAsync).Parse(args).InvokeAsync()
   f. Log.CloseAndFlushAsync() in finally

2. AgentCommandSetup.cs (CLI definitions):
   a. Positional Argument<DirectoryInfo> + named Option<string?> fields
   b. --config-key selects Models:{key} section from appsettings.json (default: "default")
   c. CreateRootCommand(action) — accepts the handler delegate, wires SetAction

3. AgentInCommand.cs (domain logic — record with ILogger<T> + IConfiguration):
   a. SetupAsync — resolves CLI options, binds AgentModelOptions from config,
      validates endpoint via EndpointHealthCheck, registers tools,
      returns AgentContext or exit code 1 on failure
   b. RunAsync — calls SetupAsync, builds IChatClient pipeline,
      builds system prompt, calls GetResponseAsync, returns exit code
```

- No `IHost`, no `IServiceProvider`, no `Program.CreateBuilder()`. Top-level statements with manual wiring.
- `AgentInCommand` is a `record` with `ILogger<T>` + `IConfiguration` via primary constructor.
- `SetupAsync` handles CLI resolution, config binding, health check, and tool registration. Returns an `AgentContext` record or early-exit code.
- `RunAsync` consumes the context: builds pipeline, runs agent, returns exit code.
- `--config-key` selects a `Models:{key}` section from `appsettings.json`. Model options (endpoint, apiKey, model) are bound via `AgentModelOptions.Resolve`.
- `EndpointHealthCheck.ValidateAsync` calls `GET /v1/models` before building the pipeline — fail fast if the endpoint is down or the model isn't loaded.
- System.CommandLine handles `--help`, `--version`, and parse error reporting automatically.

### System Prompts

- System prompts are built by a `static` method in a dedicated class (`SystemPrompt.Build`).
- The method takes runtime parameters (paths, config) and returns a string.
- System prompts are testable — unit tests verify they contain expected tool names, format specs, and paths.
- The prompt defines the agent's workflow step-by-step: which tools to call, in what order, and what output format to produce.
- Include the exact output format as a markdown template in the prompt.

### Tool Design

- Tools are `public static` methods on a static class in a `Tools/` directory.
- Each method has `[Description]` attributes on the method and every parameter.
- Descriptions are the LLM's documentation — write them for an LLM reader, not a human.
- Tools return `string` — the LLM needs text, not typed objects.
- Tools return error messages as strings (e.g., `"Error: file not found"`), not exceptions. The LLM can read and react to error strings; it cannot catch exceptions.
- Tools that touch the file system must validate paths against a root directory. Use `Path.GetFullPath` + `StartsWith` to prevent directory traversal.
- Tools are registered via `AIFunctionFactory.Create(ToolClass.MethodName)`.
- Keep tools deterministic where possible — same input, same output. No randomness, no timestamps in tool logic.

### Path Safety

Every tool that accepts a file path must:

1. Resolve relative paths against the configured root directory.
2. Canonicalize via `Path.GetFullPath`.
3. Verify the result starts with the root directory (case-insensitive on Windows).
4. Return an error string if the path escapes the root.

Use a shared `ResolveSafePath` helper. Never inline path validation.

---

## System.CommandLine

### Do

- Use `RootCommand` with collection initializers for the command tree.
- Use `Argument<T>` for positional required inputs (e.g., `Argument<DirectoryInfo>` for the target directory).
- Use `Option<T?>` for named optional values. Nullable types signal "not provided".
- Use the async `SetAction` overload: `(ParseResult, CancellationToken) => Task<int>`. Propagate the `CancellationToken` to all async work.
- Let `rootCommand.Parse(args).Invoke()` handle help, version, and parse errors — don't reimplement.
- Return `0` for success, `1` for failure from the action.

### Don't

- Don't mix manual `args` parsing with System.CommandLine. Let the library own the CLI contract.
- Don't use `SetHandler` — that's v2 API. Use `SetAction` with `ParseResult` (v3 API).
- Don't add subcommands unless the agent genuinely has multiple modes. Single-purpose agents use `RootCommand` directly.

---

## Logging

### Serilog for Structured Console Output

- Call `AgentLogging.Configure(configuration)` from Agent.SDK — bootstraps `Log.Logger` with the standard output template and reads overrides from `IConfiguration`.
- Use the `[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}` output template (matches continuum-engine convention).
- Use `AnsiConsoleTheme.Code` for colored terminal output.
- **Log-level overrides live in `appsettings.json`**, not hardcoded in bootstrap. Silence noisy sources via the `Serilog:MinimumLevel:Override` section.
- Always `await Log.CloseAndFlushAsync()` in a `finally` block to ensure buffered logs are flushed.
- Use structured log properties (`logger.LogInformation("Target: {TargetPath}", path)`) — not string interpolation.

### Configuration-driven overrides (`appsettings.json`)

- Build `IConfiguration` from `appsettings.json` in `Program.cs` using `ConfigurationBuilder`.
- Set `Directory.GetCurrentDirectory()` as the base path so agents find config relative to where they run.
- Mark `appsettings.json` as `<Content CopyToOutputDirectory="PreserveNewest" />` in the csproj.
- The `Serilog` section in `appsettings.json` drives `ReadFrom.Configuration` — no recompile needed to tune log levels.
- The `Models` section in `appsettings.json` holds named model configurations (`Models:default`, `Models:embedding`, etc.).

### Categorical ILogger<T>

- `AgentInCommand` receives `ILogger<AgentInCommand>` via primary constructor — log entries carry the source context for filtering.
- Create the `ILoggerFactory` in `Program.cs` from `AgentLogging.CreateLoggerFactory()` and resolve typed loggers from it.
- The same factory (or a second one created inside the action) is passed to `UseOpenTelemetry` and `UseFunctionInvocation` on the `ChatClientBuilder` pipeline.
- Wrap in `using` — the factory is disposed when the program exits.

---

## Telemetry

### ActivitySource + Meter (Borrowed from Continuum Engine)

Telemetry uses BCL types directly (`System.Diagnostics.ActivitySource`, `System.Diagnostics.Metrics.Meter`). No OpenTelemetry SDK export packages — agents are short-lived CLI tools where export overhead isn't justified.

### Trace: Agent.SDK `AgentTrace` + agent-specific wrapper

- `AgentTrace(string sourceName)` is an instance-based `ActivitySource` factory in Agent.SDK. Each agent creates one with its own source name.
- Agent wraps it in a static accessor: e.g., `CsiTrace.Instance` is `new AgentTrace("CrimeSceneInvestigator")`.
- `StartSpan(name, kind, tags)` returns `Activity?` — null when no listener is attached (zero overhead).
- Wrap agent runs in `using var span = CsiTrace.Instance.StartSpan("agent-run", ActivityKind.Client)`.
- Use `ActivityExtensions` for null-safe fluent tagging: `span?.WithTag("key", value)`, `span?.SetSuccess()`, `span?.RecordError(ex)`.

### Metrics: agent-specific `Meter`

- Each agent defines its own static `Meter` (e.g., `CsiMetrics` with `"CrimeSceneInvestigator"`).
- Pre-defined counters: `FilesDiscovered`, `FilesRead`, `ToolInvocations` (prefixed per agent, e.g., `csi.*`).
- Pre-defined histogram: `RunDuration` (seconds).
- Record metrics at the point of action — don't batch.

### ActivityExtensions (Agent.SDK)

- Null-safe fluent extensions on `Activity?`: `WithTag`, `RecordError`, `SetSuccess`.
- Lives in Agent.SDK — shared by all agents.
- Borrowed from `Continuum.Telemetry.ActivityExtensions`.
- All methods return `Activity?` for chaining.

---

## Rejected Patterns

| Pattern | Why Rejected |
|---|---|
| Generic Host / `IHost` | Agents are short-lived console apps. `CreateBuilder` adds DI, config, and logging overhead for no benefit. |
| Dependency Injection | Static tools with a configured root directory. No interfaces, no registrations, no service locators. |
| Aspire | No container orchestration needed. Agents connect to a single endpoint. |
| Semantic Kernel | M.E.AI abstractions are sufficient. SK adds plugin infrastructure the agents don't need. |
| Typed tool results | The LLM consumes text. Returning objects adds a serialization layer with no consumer. |
| `HttpClient` for LLM calls | The OpenAI SDK + M.E.AI pipeline handles HTTP, retries, and streaming. Don't bypass it. |
| Interactive/multi-turn agents | Each agent runs a single `GetResponseAsync` with tool calling. No REPL, no conversation loop. |
| Exceptions from tools | Return error strings. The function-invocation middleware can't surface exception details to the LLM meaningfully. |
| Manual `args` parsing | System.CommandLine handles positional args, named options, help, version, and validation. Don't reimplement. |
| OpenTelemetry SDK export | Agents are short-lived CLI tools. The OTel SDK export pipeline adds startup cost for minimal benefit. Use BCL `ActivitySource` + `Meter` for zero-overhead instrumentation. |
| `Console.WriteLine` for operational messages | Use `Log.Information` / `Log.Error`. Reserve `Console.WriteLine` for agent output the user requested (e.g., the generated context map). |

---

## Build Infrastructure

- **`Directory.Build.props`** — Centralized `net10.0`, nullable, implicit usings, `<Version>`. Do not duplicate in csproj files.
- **`Directory.Packages.props`** — Central Package Management. All NuGet versions live here. Individual csproj files use `<PackageReference Include="..." />` without `Version`.
- **`Test.Build.props`** — Auto-imported for `*.Tests` projects. Provides xUnit, NSubstitute, coverlet, Test SDK. Do not add test packages manually.
- **`global.json`** — Pins .NET SDK version with `rollForward: latestMinor`.
- **`.editorconfig`** — Enforces indent style, braces (`true:error`), file-scoped namespaces, `var` preference, using placement.
- **`nuget.config`** — Package source mapping. Isolated from global feeds.
- **Versioning** — Pure semver only (`0.1.0`). No pre-release suffixes.

---

## Testing

### Conventions

- **xUnit + NSubstitute.** `Assert.*` assertions. No FluentAssertions unless already present.
- **One test class per production class.** `FileTools` → `FileToolsTests`, `SystemPrompt` → `SystemPromptTests`.
- **Naming:** `MethodName_Condition_ExpectedBehavior` (e.g., `ListMarkdownFiles_EmptyDirectory_ReportsNone`).
- **Test project:** `[AgentName].Tests` — auto-wired by `Test.Build.props`.
- **Setup/teardown:** Constructor + `IDisposable` for per-test state. No shared mutable state between tests.

### What to Test

- **Tools:** Exercise every tool method with valid inputs, edge cases, and error paths (path traversal, missing files, empty directories).
- **System prompts:** Verify the built prompt contains expected tool names, output format markers, and runtime parameters.
- **Path safety:** Dedicated tests that attempt directory traversal (`../../../etc/passwd`) and verify rejection.
- **No LLM in tests.** Tools are pure functions. System prompts are string builders. Test them without an LLM endpoint.

### File System Tests

- Create a temp directory per test with a random name.
- Set `FileTools.RootDirectory` in the constructor.
- Delete the temp directory in `Dispose`.
- Write test fixtures as in-memory files, not checked-in fixtures.

---

## Naming

- Agent project names: `PascalCase` (e.g., `CrimeSceneInvestigator`)
- Shared library: `Agent.SDK` — the only non-agent project
- Tool classes: `[Domain]Tools` (e.g., `FileTools`)
- System prompt class: `SystemPrompt` with a static `Build` method
- Agent domain class: `AgentInCommand` — record with `ILogger<AgentInCommand>`
- CLI setup class: `AgentCommandSetup` — static factory for `RootCommand`
- CLI argument names: `--kebab-case` (e.g., `--api-key`, `--endpoint`)
- Environment variable names: `SHORTNAME_SETTING` (e.g., `CSI_ENDPOINT`, `CSI_API_KEY`)
- Test projects: `[AgentName].Tests`

---

## Adding a New Agent

1. Create `src/NewAgent/NewAgent.csproj` — `<ProjectReference>` to Agent.SDK, plus M.E.AI packages without versions (CPM owns them).
2. Add any new package versions to `src/Directory.Packages.props`.
3. Create `src/NewAgent/appsettings.json` — Serilog overrides, `<Content CopyToOutputDirectory="PreserveNewest" />`.
4. Create `src/NewAgent/Program.cs` — thin bootstrap: build `IConfiguration`, `AgentLogging.Configure`, create `AgentInCommand` with `ILogger<T>`, invoke CLI.
5. Create `src/NewAgent/AgentCommandSetup.cs` — `RootCommand` factory accepting the action delegate.
6. Create `src/NewAgent/AgentInCommand.cs` — record with `ILogger<AgentInCommand>`, domain logic in `RunAsync`.
7. Create `src/NewAgent/Tools/` — static tool class with `[Description]` methods.
8. Create `src/NewAgent/SystemPrompt.cs` — static `Build` method returning the system prompt string.
9. Create `src/NewAgent/Telemetry/` — agent-specific `AgentTrace` instance + `Meter` class.
10. Create `src/NewAgent.Tests/NewAgent.Tests.csproj` — only needs `<ProjectReference>` to the agent. Test infrastructure is auto-imported.
11. Update `docs/STRUCTURE.md` with the new agent entry.
12. Update `README.md` with quick-start instructions.
