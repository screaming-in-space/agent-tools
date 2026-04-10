# ModelBoss

Benchmark local LLM models with deterministic speed, accuracy, and quality scoring. No LLM-as-judge — all scoring uses substring matching, structure validation, and bigram similarity.

## Quick start

```bash
cd agents/dotnet
dotnet run --project src/ModelBoss -- --headless
```

Requires:
- .NET 10 SDK
- An OpenAI-compatible endpoint running locally ([LM Studio](https://lmstudio.ai), [Ollama](https://ollama.com))
- At least one model loaded at the endpoint

## What it does

1. Reads model configs from `appsettings.json` (`Models:{key}` sections)
2. Validates the endpoint is healthy (`GET /v1/models`)
3. Runs built-in benchmark prompts against each model (warmup + measured iterations)
4. Scores accuracy deterministically against expected output criteria
5. Builds per-model scorecards with speed, accuracy, and composite metrics
6. Writes a ranked `BENCHMARK.md` report

## CLI options

| Option | Description | Default |
|--------|-------------|---------|
| `--models` | Comma-separated config keys to benchmark | all configured models |
| `--iterations` | Measured iterations per prompt | `3` |
| `--category` | `instruction_following`, `extraction`, `markdown_generation`, `reasoning`, `all` | `all` |
| `--output` | Output directory for reports | `<repo-root>/benchmarks/` |
| `--repo-root` | Repository root for model/GPU registries | auto-detect from cwd |
| `--headless` | Plain log output (no Spectre.Console UI) | `false` |

## Examples

Benchmark only the default model:

```bash
dotnet run --project src/ModelBoss -- --models default --headless
```

Run just the reasoning suite with 5 iterations:

```bash
dotnet run --project src/ModelBoss -- --category reasoning --iterations 5 --headless
```

Benchmark two specific models and write results to a custom directory:

```bash
dotnet run --project src/ModelBoss -- --models default,gemma-26b --output ./results --headless
```

## Configuration

Models are configured in `appsettings.json` under `Models:{key}`:

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

Add as many model sections as you want — each key becomes selectable via `--models`.

The `embedding` key is excluded from benchmarks automatically (used by CrimeSceneInvestigator for retrieval).

## Output

Writes `BENCHMARK.md` to the output directory with:

- **Rankings table** — all models ranked by composite score
- **Hardware summary** — GPU specs from `context/gpu/_registry.json`
- **Per-model scorecards** — speed metrics (median tok/s, P5 tok/s, TTFT), accuracy metrics (mean score, pass rate), per-prompt breakdown
- **Recommendations** — best overall, best speed, best accuracy, best value
- **Methodology** — iteration counts, scoring formula, normalization

### Composite score formula

```
composite = (accuracy × 0.6) + (normalized_speed × 0.3) + (pass_rate × 0.1)
```

Speed normalization: 50 tok/s = 1.0 (linear, capped at 1.0).

## Benchmark suites

| Category | Prompts | Tests |
|----------|---------|-------|
| `instruction_following` | 3 | Format compliance, constrained output, stop-when-told |
| `extraction` | 2 | Structured data extraction from unstructured text |
| `markdown_generation` | 2 | Context maps, tables, section structure |
| `reasoning` | 2 | Model selection reasoning, comparative analysis |

## Accuracy scoring

Each prompt defines expected output criteria. Scoring is deterministic:

| Check | Weight | What it measures |
|-------|--------|------------------|
| `required_substrings` | 2.0 | Key content present (case-insensitive) |
| `forbidden_substrings` | 1.5 | Chatbot filler absent ("Sure", "Here's", etc.) |
| `required_structure` | 1.5 | Markdown elements present (`#`, `|`, `---`, `- `) |
| `reference_similarity` | 1.0 | Bigram similarity to gold-standard output |
| `length` | 0.5 | Response within min/max character bounds |

## Architecture

```
ModelBoss/
├── Benchmarks/
│   ├── AccuracyScorer.cs      Deterministic output scoring
│   ├── BenchmarkRunner.cs     Warmup + measured iterations with streaming
│   ├── BenchmarkSuites.cs     Built-in prompt suites
│   ├── ScorecardBuilder.cs    Raw results → percentile-based scorecard
│   └── ...                    Records: BenchmarkPrompt, BenchmarkResult, AccuracyResult, ModelScorecard
├── Tools/
│   ├── BenchmarkTools.cs      Agent-callable tool wrappers
│   ├── ModelTools.cs          Registry + endpoint queries
│   ├── GpuTools.cs            GPU discovery + model-fit matrix
│   └── ReportTools.cs         File output
├── BossAgent.cs               Orchestrator (record with ILoggerFactory + IConfiguration)
├── BossCommandSetup.cs        System.CommandLine CLI definitions
├── BossPrompt.cs              System prompt for LLM-driven workflow
├── ReportFormatter.cs         Markdown report builder
└── Program.cs                 Thin bootstrap: config → logging → CLI → flush
```

## Testing

```bash
cd agents/dotnet
dotnet test --filter "Project=ModelBoss.Tests"
```

36 tests covering `AccuracyScorer`, `ScorecardBuilder`, and `BenchmarkSuites`.
