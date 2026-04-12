namespace ModelBoss.Benchmarks;

/// <summary>
/// Built-in benchmark prompt suites that test specific model capabilities.
/// Each suite targets a dimension you care about when selecting a local model:
/// can it follow instructions, extract structured data, produce markdown, call tools correctly.
///
/// Prompts are tagged with <see cref="BenchmarkDifficulty"/>:
/// Level 1 — basic capability checks (can the model do this at all?)
/// Level 2 — stricter compliance (fewer guardrails, harder constraints, ambiguous input)
/// Level 3 — stress tests (long context, adversarial constraints, multi-step chains)
/// </summary>
public static class BenchmarkSuites
{
    /// <summary>
    /// Returns all built-in benchmark prompts across all categories.
    /// </summary>
    public static IReadOnlyList<BenchmarkPrompt> All() =>
    [
        .. InstructionFollowing(),
        .. Extraction(),
        .. MarkdownGeneration(),
        .. Reasoning(),
        .. MultiTurn(),
        .. ContextWindow(),
    ];

    /// <summary>
    /// Returns prompts for a given category name, or all prompts if the category
    /// is null, empty, or "all". Unknown categories also return all prompts.
    /// </summary>
    public static IReadOnlyList<BenchmarkPrompt> GetByCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category) || string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
        {
            return All();
        }

        return category.ToLowerInvariant() switch
        {
            "instruction_following" => InstructionFollowing(),
            "extraction" => Extraction(),
            "markdown_generation" => MarkdownGeneration(),
            "reasoning" => Reasoning(),
            "multi_turn" => MultiTurn(),
            "context_window" => ContextWindow(),
            _ => All(),
        };
    }

    /// <summary>
    /// Returns prompts filtered by maximum difficulty level.
    /// Level 1 returns only basic prompts; Level 2 includes Level 1 + Level 2, etc.
    /// </summary>
    public static IReadOnlyList<BenchmarkPrompt> UpToLevel(BenchmarkDifficulty maxLevel) =>
        All().Where(p => p.Difficulty <= maxLevel).ToList();

    /// <summary>
    /// Tests whether the model follows explicit constraints: format, length, inclusions, exclusions.
    /// This is the #1 failure mode for small models — they drift off-instruction.
    /// </summary>
    public static IReadOnlyList<BenchmarkPrompt> InstructionFollowing() =>
    [
        // ── Level 1: Basic compliance ──────────────────────────────────
        new BenchmarkPrompt
        {
            Name = "strict_format_json",
            Description = "Tests whether the model returns pure JSON with no preamble or markdown fencing.",
            Category = "instruction_following",
            Difficulty = BenchmarkDifficulty.Level1,
            SystemMessage = "You are a data extraction assistant. Respond ONLY with valid JSON. No explanation, no markdown, no preamble.",
            UserMessage = """
                Extract the following into JSON with keys "name", "version", "language":
                The project is called AgentTools, it's version 0.1.0, written in C#.
                """,
            Expected = new ExpectedOutput
            {
                RequiredSubstrings = ["AgentTools", "0.1.0", "C#"],
                ForbiddenSubstrings = ["Sure", "Here's", "I'll", "Let me"],
                MinLength = 20,
                MaxLength = 300,
                ReferenceOutput = """{"name": "AgentTools", "version": "0.1.0", "language": "C#"}""",
                PassThreshold = 0.7,
            },
        },
        new BenchmarkPrompt
        {
            Name = "constrained_list",
            Description = "Tests whether the model produces exactly 3 bullet points with no intro or conclusion.",
            Category = "instruction_following",
            Difficulty = BenchmarkDifficulty.Level1,
            SystemMessage = "Respond with exactly 3 bullet points. No introduction. No conclusion. Just 3 bullets starting with '- '.",
            UserMessage = "List three benefits of local LLM inference.",
            Expected = new ExpectedOutput
            {
                RequiredStructure = ["- "],
                ForbiddenSubstrings = ["Sure", "Here are", "In conclusion", "Overall"],
                MinLength = 30,
                MaxLength = 500,
                PassThreshold = 0.6,
            },
        },
        new BenchmarkPrompt
        {
            Name = "stop_when_told",
            Description = "Tests whether the model stops after one sentence and writes DONE with nothing after.",
            Category = "instruction_following",
            Difficulty = BenchmarkDifficulty.Level1,
            SystemMessage = """
                Answer the question in ONE sentence. After the sentence, write "DONE" on a new line.
                Do not write anything after DONE.
                """,
            UserMessage = "What is dependency injection?",
            Expected = new ExpectedOutput
            {
                RequiredSubstrings = ["DONE"],
                ForbiddenSubstrings = ["Here's", "Sure", "Let me explain"],
                MinLength = 20,
                MaxLength = 400,
                PassThreshold = 0.6,
            },
        },

        // ── Level 2: Stricter compliance, minimal prompting ────────────
        new BenchmarkPrompt
        {
            Name = "minimal_prompt_json",
            Description = "Tests JSON extraction with a terse system prompt — no hand-holding, model must infer format.",
            Category = "instruction_following",
            Difficulty = BenchmarkDifficulty.Level2,
            SystemMessage = "JSON only.",
            UserMessage = """{"task": "extract", "input": "Redis cache at port 6380, 256MB maxmemory, eviction policy allkeys-lru", "schema": {"port": "int", "maxmemory": "string", "eviction": "string"}}""",
            Expected = new ExpectedOutput
            {
                RequiredSubstrings = ["6380", "256", "allkeys-lru"],
                ForbiddenSubstrings = ["Sure", "Here", "Let me", "```"],
                ForbiddenPreamble = ["Sure", "Of course", "Here's", "I'd", "Let me", "Certainly"],
                MinLength = 30,
                MaxLength = 200,
                ReferenceOutput = """{"port": 6380, "maxmemory": "256MB", "eviction": "allkeys-lru"}""",
                PassThreshold = 0.75,
            },
        },
        new BenchmarkPrompt
        {
            Name = "multi_constraint",
            Description = "Tests compliance with 5 simultaneous constraints: paragraph count, prefixes, word limit, forbidden words.",
            Category = "instruction_following",
            Difficulty = BenchmarkDifficulty.Level2,
            SystemMessage = """
                Rules (all must be followed):
                1. Respond in exactly 2 paragraphs
                2. First paragraph must start with "Analysis:"
                3. Second paragraph must start with "Verdict:"
                4. Total response must be under 150 words
                5. Do not use the word "however"
                """,
            UserMessage = "Should a small dev team use microservices or a monolith?",
            Expected = new ExpectedOutput
            {
                RequiredSubstrings = ["Analysis:", "Verdict:"],
                ForbiddenSubstrings = ["however", "Sure", "Here's", "Let me"],
                ForbiddenPreamble = ["Sure", "Of course", "Great question", "That's"],
                MinLength = 100,
                MaxLength = 1200,
                PassThreshold = 0.7,
            },
        },
        new BenchmarkPrompt
        {
            Name = "negative_instruction",
            Description = "Tests adherence to negative constraints: no markdown, no sentences starting with 'The', max 3 sentences.",
            Category = "instruction_following",
            Difficulty = BenchmarkDifficulty.Level2,
            SystemMessage = """
                You must NOT:
                - Use any markdown formatting (no #, *, `, |, -)
                - Start any sentence with "The"
                - Use more than 3 sentences
                Just answer in plain text.
                """,
            UserMessage = "Explain what a load balancer does.",
            Expected = new ExpectedOutput
            {
                ForbiddenSubstrings = ["#", "*", "`", "|"],
                ForbiddenPreamble = ["Sure", "Of course", "Great", "The"],
                MinLength = 40,
                MaxLength = 500,
                PassThreshold = 0.7,
            },
        },
    ];

    /// <summary>
    /// Tests structured data extraction from unstructured text.
    /// Models that can't do this reliably will botch tool-call argument parsing.
    /// </summary>
    public static IReadOnlyList<BenchmarkPrompt> Extraction() =>
    [
        // ── Level 1: Clean extraction ──────────────────────────────────
        new BenchmarkPrompt
        {
            Name = "extract_model_specs",
            Description = "Tests structured data extraction from prose into a markdown table with specific columns.",
            Category = "extraction",
            Difficulty = BenchmarkDifficulty.Level1,
            SystemMessage = "Extract model specifications into a markdown table with columns: Model, Parameters, Architecture, VRAM (Q4). No other text.",
            UserMessage = """
                The nvidia-nemotron-3-nano-4b has 4.0B parameters, uses hybrid-mamba architecture, and needs 3GB VRAM at Q4.
                The google-gemma-4-31b-it has 30.7B parameters, uses dense architecture, and needs 20GB VRAM at Q4.
                The google-gemma-4-26b-a4b-it has 25.2B parameters, uses MoE architecture, and needs 17GB VRAM at Q4.
                """,
            Expected = new ExpectedOutput
            {
                RequiredSubstrings = ["nemotron", "gemma", "4.0", "30.7", "25.2"],
                RequiredStructure = ["|", "---"],
                ForbiddenSubstrings = ["Sure", "Here's", "I'll"],
                MinLength = 100,
                MaxLength = 800,
                PassThreshold = 0.7,
            },
        },
        new BenchmarkPrompt
        {
            Name = "extract_key_value",
            Description = "Tests extraction of key-value pairs from natural language into structured lines.",
            Category = "extraction",
            Difficulty = BenchmarkDifficulty.Level1,
            SystemMessage = "Extract all key-value pairs as `key: value` lines. One per line. No other text.",
            UserMessage = """
                The server runs at http://localhost:1234/v1. The API key is "no-key".
                The default model is unsloth/nvidia-nemotron-3-nano-4b with temperature 0.3 and max tokens 4096.
                """,
            Expected = new ExpectedOutput
            {
                RequiredSubstrings = ["localhost:1234", "no-key", "nemotron", "0.3", "4096"],
                ForbiddenSubstrings = ["Sure", "Here are", "Let me"],
                MinLength = 50,
                MaxLength = 500,
                PassThreshold = 0.7,
            },
        },

        // ── Level 2: Noisy/ambiguous extraction ────────────────────────
        new BenchmarkPrompt
        {
            Name = "extract_from_noise",
            Description = "Tests extraction of error messages from noisy log output, filtering timestamps and log levels.",
            Category = "extraction",
            Difficulty = BenchmarkDifficulty.Level2,
            SystemMessage = "Extract ONLY the error messages from the log. Return one per line, no timestamps, no log levels, no other text.",
            UserMessage = """
                2024-12-01T10:30:15Z INFO  Starting benchmark run for gemma-4-e4b-it
                2024-12-01T10:30:16Z DEBUG Warmup iteration 1/1 for strict_format_json
                2024-12-01T10:30:18Z INFO  → 42.3 tok/s, TTFT=120ms
                2024-12-01T10:30:19Z ERROR Connection refused: endpoint http://localhost:1234/v1 not responding
                2024-12-01T10:30:20Z WARN  Retrying in 2s...
                2024-12-01T10:30:22Z ERROR Timeout after 60s on prompt extract_model_specs (0 tokens generated)
                2024-12-01T10:30:23Z INFO  Benchmark complete: 1 passed, 2 failed
                2024-12-01T10:30:24Z ERROR Model gemma-4-31b-it requires 20GB VRAM but only 16GB available
                """,
            Expected = new ExpectedOutput
            {
                RequiredSubstrings = ["Connection refused", "Timeout after 60s", "requires 20GB"],
                ForbiddenSubstrings = ["2024-12", "INFO", "DEBUG", "WARN", "Sure", "Here"],
                ForbiddenPreamble = ["Sure", "Here", "The", "I"],
                MinLength = 50,
                MaxLength = 500,
                PassThreshold = 0.75,
            },
        },
        new BenchmarkPrompt
        {
            Name = "extract_nested_json",
            Description = "Tests conversion of indented text hierarchy to valid nested JSON without code fences.",
            Category = "extraction",
            Difficulty = BenchmarkDifficulty.Level2,
            SystemMessage = "Respond with valid JSON only. No markdown code fences. No explanation.",
            UserMessage = """
                Convert this to JSON preserving the hierarchy:
                GPU: RTX 5090
                  VRAM: 32GB
                  Bandwidth: 1792 GB/s
                  Models loaded:
                    - gemma-4-e4b-it (active, 4096 context)
                    - qwen3-coder (idle, 8192 context)
                """,
            Expected = new ExpectedOutput
            {
                RequiredSubstrings = ["RTX 5090", "32", "1792", "gemma", "qwen3", "4096", "8192"],
                ForbiddenSubstrings = ["```", "Sure", "Here"],
                ForbiddenPreamble = ["Sure", "Here", "```", "Of course"],
                MinLength = 100,
                MaxLength = 600,
                PassThreshold = 0.75,
            },
        },
    ];

    /// <summary>
    /// Tests the model's ability to produce well-structured markdown.
    /// This is what our CSI scanners need — if the model can't produce clean markdown, it's useless.
    /// </summary>
    public static IReadOnlyList<BenchmarkPrompt> MarkdownGeneration() =>
    [
        // ── Level 1: Basic markdown ────────────────────────────────────
        new BenchmarkPrompt
        {
            Name = "generate_context_map",
            Description = "Tests generation of a structured markdown context map with required sections and file index table.",
            Category = "markdown_generation",
            Difficulty = BenchmarkDifficulty.Level1,
            SystemMessage = """
                You produce context maps in markdown. Every response must contain these sections in order:
                # Context Map
                ## Overview
                ## File Index (as a table)
                ## Themes
                No other sections. No preamble.
                """,
            UserMessage = """
                Create a context map for a directory containing these files:
                - README.md: Project introduction and setup instructions
                - CONTRIBUTING.md: How to contribute, PR process
                - CHANGELOG.md: Version history and breaking changes
                """,
            Expected = new ExpectedOutput
            {
                RequiredSubstrings = ["README.md", "CONTRIBUTING.md", "CHANGELOG.md"],
                RequiredStructure = ["# Context Map", "## Overview", "## File Index", "## Themes", "|"],
                ForbiddenSubstrings = ["Sure", "Here's", "I'll create"],
                MinLength = 200,
                MaxLength = 2000,
                PassThreshold = 0.7,
            },
        },
        new BenchmarkPrompt
        {
            Name = "generate_table",
            Description = "Tests generation of a clean markdown comparison table with no surrounding text.",
            Category = "markdown_generation",
            Difficulty = BenchmarkDifficulty.Level1,
            SystemMessage = "Respond with a markdown table only. No text before or after the table.",
            UserMessage = """
                Create a comparison table for these GPUs:
                RTX 4090 Mobile: 16GB VRAM, 640 GB/s bandwidth, 9728 CUDA cores
                RTX 5090 MSI Suprim: 32GB VRAM, 1792 GB/s bandwidth, 21760 CUDA cores
                """,
            Expected = new ExpectedOutput
            {
                RequiredSubstrings = ["4090", "5090", "16", "32"],
                RequiredStructure = ["|", "---"],
                ForbiddenSubstrings = ["Sure", "Here", "Let me"],
                MinLength = 80,
                MaxLength = 600,
                PassThreshold = 0.7,
            },
        },
    ];

    /// <summary>
    /// Tests logical reasoning and multi-step analysis.
    /// Models that fail here will make wrong decisions in the planner/scorer roles.
    /// </summary>
    public static IReadOnlyList<BenchmarkPrompt> Reasoning() =>
    [
        // ── Level 1: Basic reasoning ───────────────────────────────────
        new BenchmarkPrompt
        {
            Name = "model_selection_reasoning",
            Description = "Tests model selection reasoning given GPU VRAM constraints and model requirements.",
            Category = "reasoning",
            Difficulty = BenchmarkDifficulty.Level1,
            SystemMessage = """
                You are a model selection advisor. Given GPU specs and model requirements,
                recommend which model to use. Explain your reasoning in 2-3 sentences.
                Then state your recommendation on a final line: "RECOMMENDATION: <model name>"
                """,
            UserMessage = """
                GPU: RTX 4090 Mobile with 16GB VRAM
                Available models:
                - nemotron-3-nano-4b: 3GB VRAM at Q4, fast, marginal on complex tasks
                - gemma-4-26b-a4b-it: 17GB VRAM at Q4 (tight fit), excellent quality
                - gemma-4-31b-it: 20GB VRAM at Q4, best quality but won't fit

                Task: Run a code quality analysis scanner that needs good reasoning.
                """,
            Expected = new ExpectedOutput
            {
                RequiredSubstrings = ["RECOMMENDATION:"],
                ForbiddenSubstrings = ["gemma-4-31b"],
                MinLength = 100,
                MaxLength = 800,
                PassThreshold = 0.6,
            },
        },
        new BenchmarkPrompt
        {
            Name = "comparative_analysis",
            Description = "Tests trade-off analysis between a dense high-quality model and a fast MoE model for agent workflows.",
            Category = "reasoning",
            Difficulty = BenchmarkDifficulty.Level1,
            SystemMessage = "Compare the two items. State which is better for the given use case and why. Be concise.",
            UserMessage = """
                Use case: Running a local LLM agent that makes 15+ tool calls per task.

                Option A: Dense 31B model at Q4 — highest quality, 2 tokens/sec on available GPU
                Option B: MoE 26B model (3.8B active) at Q4 — 88% of A's quality, 14 tokens/sec

                Which option and why?
                """,
            Expected = new ExpectedOutput
            {
                RequiredSubstrings = ["Option B", "speed", "tool"],
                MinLength = 80,
                MaxLength = 600,
                PassThreshold = 0.6,
            },
        },

        // ── Level 2: Multi-step and quantitative reasoning ─────────────
        new BenchmarkPrompt
        {
            Name = "quantitative_reasoning",
            Description = "Tests step-by-step calculation of total agent task time from tok/s, TTFT, and tool call counts.",
            Category = "reasoning",
            Difficulty = BenchmarkDifficulty.Level2,
            SystemMessage = """
                You are a performance analyst. Show your calculations step by step.
                End with a line: "ANSWER: <number> <unit>"
                """,
            UserMessage = """
                A model generates 45 tokens/second with 200ms TTFT.
                Each tool call requires: 1 request (avg 80 tokens output) + 1 response (avg 120 tokens output).
                An agent task makes 12 tool calls sequentially.

                What is the total estimated time in seconds for the full task?
                Include TTFT only for the first request.
                """,
            Expected = new ExpectedOutput
            {
                RequiredSubstrings = ["ANSWER:"],
                ForbiddenPreamble = ["Sure", "Of course", "Great"],
                MinLength = 100,
                MaxLength = 1000,
                PassThreshold = 0.65,
            },
        },
        new BenchmarkPrompt
        {
            Name = "multi_step_deduction",
            Description = "Tests logical deduction from a set of relational constraints about speed and accuracy.",
            Category = "reasoning",
            Difficulty = BenchmarkDifficulty.Level2,
            SystemMessage = """
                Solve the logic puzzle. Show your reasoning. End with:
                "SOLUTION: <answer>"
                """,
            UserMessage = """
                Three models (A, B, C) were benchmarked. These facts are known:
                1. Model A is faster than Model C
                2. Model B has higher accuracy than Model A
                3. Model C has higher accuracy than Model B
                4. The model with the lowest accuracy is the fastest
                5. No two models tie on any metric

                Which model is the fastest? Which has the highest accuracy?
                """,
            Expected = new ExpectedOutput
            {
                RequiredSubstrings = ["SOLUTION:"],
                ForbiddenPreamble = ["Sure", "Of course", "Great", "Let me"],
                MinLength = 100,
                MaxLength = 1000,
                PassThreshold = 0.65,
            },
        },
    ];

    /// <summary>
    /// MT-Bench style multi-turn conversation benchmarks.
    /// Tests coherence across turns, follow-up refinement, context retention,
    /// and the model's ability to modify its prior output on request.
    /// These are the most realistic tests for agent workflows where tools
    /// send results back for the model to refine.
    /// </summary>
    public static IReadOnlyList<BenchmarkPrompt> MultiTurn() =>
    [
        new BenchmarkPrompt
        {
            Name = "mt_refine_output",
            Description = "Tests whether the model can modify a table by adding a row while keeping existing rows unchanged.",
            Category = "multi_turn",
            Difficulty = BenchmarkDifficulty.Level2,
            SystemMessage = "You are a technical documentation assistant. Follow instructions precisely. No preamble.",
            UserMessage = "", // Unused in multi-turn mode
            Expected = new ExpectedOutput { PassThreshold = 0.7 }, // Unused in multi-turn mode
            Timeout = TimeSpan.FromSeconds(90),
            Turns =
            [
                new ConversationTurn
                {
                    UserMessage = """
                        Write a markdown table comparing these two approaches:
                        | Approach | Pros | Cons |
                        Row 1: Monolith - simple deployment, tight coupling
                        Row 2: Microservices - independent scaling, operational complexity
                        """,
                    Expected = new ExpectedOutput
                    {
                        RequiredSubstrings = ["Monolith", "Microservices"],
                        RequiredStructure = ["|", "---"],
                        ForbiddenPreamble = ["Sure", "Here", "Of course"],
                        MinLength = 80,
                        MaxLength = 600,
                        PassThreshold = 0.7,
                    },
                },
                new ConversationTurn
                {
                    UserMessage = "Add a third row for 'Modular Monolith' with pros 'balanced complexity' and cons 'newer pattern, less tooling'. Keep the existing rows unchanged.",
                    Expected = new ExpectedOutput
                    {
                        RequiredSubstrings = ["Monolith", "Microservices", "Modular Monolith", "balanced complexity", "less tooling"],
                        RequiredStructure = ["|", "---"],
                        ForbiddenPreamble = ["Sure", "Here", "Of course"],
                        MinLength = 120,
                        MaxLength = 800,
                        PassThreshold = 0.7,
                    },
                },
            ],
        },
        new BenchmarkPrompt
        {
            Name = "mt_context_retention",
            Description = "Tests whether the model retains numerical facts across turns and reasons about trade-offs.",
            Category = "multi_turn",
            Difficulty = BenchmarkDifficulty.Level2,
            SystemMessage = "You are a performance analyst. Be precise with numbers. No preamble.",
            UserMessage = "",
            Expected = new ExpectedOutput { PassThreshold = 0.7 },
            Timeout = TimeSpan.FromSeconds(90),
            Turns =
            [
                new ConversationTurn
                {
                    UserMessage = """
                        Here are benchmark results for three models:
                        - Alpha: 42.3 tok/s, 0.91 accuracy, 180ms TTFT
                        - Beta: 28.7 tok/s, 0.95 accuracy, 310ms TTFT
                        - Gamma: 55.1 tok/s, 0.82 accuracy, 95ms TTFT

                        Which model has the best accuracy? State only the model name and score.
                        """,
                    Expected = new ExpectedOutput
                    {
                        RequiredSubstrings = ["Beta", "0.95"],
                        ForbiddenPreamble = ["Sure", "The", "Based"],
                        MinLength = 5,
                        MaxLength = 200,
                        PassThreshold = 0.7,
                    },
                },
                new ConversationTurn
                {
                    UserMessage = "Now, which model would you recommend for an agent making 20 sequential tool calls, where latency matters more than quality? Use the numbers I gave you. End with 'PICK: <name>'.",
                    Expected = new ExpectedOutput
                    {
                        RequiredSubstrings = ["PICK:", "Gamma"],
                        ForbiddenPreamble = ["Sure", "Of course", "Based on"],
                        MinLength = 30,
                        MaxLength = 500,
                        PassThreshold = 0.7,
                    },
                },
            ],
        },
        new BenchmarkPrompt
        {
            Name = "mt_instruction_shift",
            Description = "Tests adaptation when the output format changes between turns (numbered list to JSON array).",
            Category = "multi_turn",
            Difficulty = BenchmarkDifficulty.Level3,
            SystemMessage = "Follow instructions exactly. Output format changes between turns.",
            UserMessage = "",
            Expected = new ExpectedOutput { PassThreshold = 0.7 },
            Timeout = TimeSpan.FromSeconds(90),
            Turns =
            [
                new ConversationTurn
                {
                    UserMessage = """
                        List these items as a numbered list:
                        Redis, PostgreSQL, SQLite
                        """,
                    Expected = new ExpectedOutput
                    {
                        RequiredSubstrings = ["Redis", "PostgreSQL", "SQLite"],
                        RequiredStructure = ["1."],
                        ForbiddenPreamble = ["Sure", "Here"],
                        MinLength = 20,
                        MaxLength = 200,
                        PassThreshold = 0.7,
                    },
                },
                new ConversationTurn
                {
                    UserMessage = "Now convert that same list to a JSON array of strings. Nothing else.",
                    Expected = new ExpectedOutput
                    {
                        RequiredSubstrings = ["Redis", "PostgreSQL", "SQLite", "[", "]"],
                        ForbiddenSubstrings = ["1.", "2.", "3.", "```"],
                        ForbiddenPreamble = ["Sure", "Here", "Of course"],
                        MinLength = 20,
                        MaxLength = 200,
                        PassThreshold = 0.75,
                    },
                },
            ],
        },
    ];

    /// <summary>
    /// RULER-inspired context window benchmarks. Tests long-context retrieval and
    /// attention quality — not hardware speed, but whether the model can find and use
    /// information buried deep in the context. Uses programmatically generated padding.
    /// </summary>
    public static IReadOnlyList<BenchmarkPrompt> ContextWindow()
    {
        var prompts = new List<BenchmarkPrompt>();

        // ── Needle-in-a-Haystack (NIAH) ────────────────────────────────
        // Embed a specific fact in generated padding text, ask the model to retrieve it.
        prompts.Add(BuildNeedleInHaystack(
            name: "niah_4k",
            description: "Needle-in-haystack: finds a secret passphrase buried in ~4K tokens of technical padding.",
            difficulty: BenchmarkDifficulty.Level2,
            paddingParagraphs: 20,
            needlePosition: 0.5,
            needle: "The secret benchmark passphrase is 'crystal-pegasus-7'.",
            question: "What is the secret benchmark passphrase mentioned in the context?",
            expectedSubstrings: ["crystal-pegasus-7"],
            timeout: TimeSpan.FromSeconds(90)));

        prompts.Add(BuildNeedleInHaystack(
            name: "niah_8k",
            description: "Needle-in-haystack: finds a specific tok/s number buried in ~8K tokens at the 30% position.",
            difficulty: BenchmarkDifficulty.Level3,
            paddingParagraphs: 40,
            needlePosition: 0.3,
            needle: "Model X achieved exactly 47.2 tokens per second on the RTX 4090 benchmark run.",
            question: "What exact tokens-per-second rate did Model X achieve on the RTX 4090?",
            expectedSubstrings: ["47.2"],
            timeout: TimeSpan.FromSeconds(120)));

        // ── Multi-key retrieval ────────────────────────────────────────
        // Multiple facts scattered across the context — model must find all of them.
        prompts.Add(BuildMultiKeyRetrieval(
            difficulty: BenchmarkDifficulty.Level3,
            timeout: TimeSpan.FromSeconds(120)));

        // ── Variable tracking ──────────────────────────────────────────
        // Track a value that changes through sequential updates in the context.
        prompts.Add(BuildVariableTracking(
            difficulty: BenchmarkDifficulty.Level3,
            timeout: TimeSpan.FromSeconds(120)));

        return prompts;
    }

    // ── RULER prompt builders ──────────────────────────────────────────

    private static BenchmarkPrompt BuildNeedleInHaystack(
        string name,
        string description,
        BenchmarkDifficulty difficulty,
        int paddingParagraphs,
        double needlePosition,
        string needle,
        string question,
        IReadOnlyList<string> expectedSubstrings,
        TimeSpan timeout)
    {
        var haystack = GenerateHaystack(paddingParagraphs, needle, needlePosition);

        return new BenchmarkPrompt
        {
            Name = name,
            Description = description,
            Category = "context_window",
            Difficulty = difficulty,
            SystemMessage = "Answer the question based ONLY on the provided context. Be concise. No preamble.",
            UserMessage = $"Context:\n{haystack}\n\nQuestion: {question}",
            Expected = new ExpectedOutput
            {
                RequiredSubstrings = expectedSubstrings,
                ForbiddenPreamble = ["Sure", "Based on", "According to"],
                MinLength = 5,
                MaxLength = 300,
                PassThreshold = 0.8,
            },
            Timeout = timeout,
        };
    }

    private static BenchmarkPrompt BuildMultiKeyRetrieval(
        BenchmarkDifficulty difficulty,
        TimeSpan timeout)
    {
        var facts = new[]
        {
            ("Server alpha runs on port 8443 with TLS enabled.", "8443"),
            ("The database connection pool is limited to 25 connections.", "25"),
            ("Cache TTL is set to 3600 seconds for session data.", "3600"),
        };

        var paragraphs = new List<string>();
        var paddingBetween = 8;

        foreach (var (fact, _) in facts)
        {
            for (var i = 0; i < paddingBetween; i++)
            {
                paragraphs.Add(GeneratePaddingParagraph(paragraphs.Count));
            }

            paragraphs.Add(fact);
        }

        // Add trailing padding
        for (var i = 0; i < paddingBetween; i++)
        {
            paragraphs.Add(GeneratePaddingParagraph(paragraphs.Count));
        }

        var context = string.Join("\n\n", paragraphs);

        return new BenchmarkPrompt
        {
            Name = "niah_multi_key",
            Description = "Multi-key retrieval: finds 3 configuration values scattered across padding paragraphs.",
            Category = "context_window",
            Difficulty = difficulty,
            SystemMessage = "Answer the question using ONLY the provided context. List each value on its own line. No other text.",
            UserMessage = $"Context:\n{context}\n\nQuestion: What are the three configuration values mentioned? List each as 'key: value'.",
            Expected = new ExpectedOutput
            {
                RequiredSubstrings = [.. facts.Select(f => f.Item2)],
                ForbiddenPreamble = ["Sure", "Based on", "Here"],
                MinLength = 20,
                MaxLength = 500,
                PassThreshold = 0.75,
            },
            Timeout = timeout,
        };
    }

    private static BenchmarkPrompt BuildVariableTracking(
        BenchmarkDifficulty difficulty,
        TimeSpan timeout)
    {
        var updates = new[]
        {
            "Initial setting: max_retries = 3",
            "After load testing review: max_retries was increased to 5",
            "Post-incident adjustment: max_retries was reduced to 2",
            "Final production config: max_retries was set to 4",
        };

        var paragraphs = new List<string>();

        foreach (var update in updates)
        {
            for (var i = 0; i < 6; i++)
            {
                paragraphs.Add(GeneratePaddingParagraph(paragraphs.Count));
            }

            paragraphs.Add(update);
        }

        for (var i = 0; i < 6; i++)
        {
            paragraphs.Add(GeneratePaddingParagraph(paragraphs.Count));
        }

        var context = string.Join("\n\n", paragraphs);

        return new BenchmarkPrompt
        {
            Name = "ruler_variable_tracking",
            Description = "Variable tracking: identifies the final value of a setting that changes 4 times across the context.",
            Category = "context_window",
            Difficulty = difficulty,
            SystemMessage = "Answer based ONLY on the provided context. State only the final value. No explanation.",
            UserMessage = $"Context:\n{context}\n\nQuestion: What is the final value of max_retries?",
            Expected = new ExpectedOutput
            {
                RequiredSubstrings = ["4"],
                ForbiddenSubstrings = ["3", "5", "2"],
                ForbiddenPreamble = ["Sure", "Based on", "According"],
                MinLength = 1,
                MaxLength = 100,
                PassThreshold = 0.8,
            },
            Timeout = timeout,
        };
    }

    /// <summary>
    /// Generates a haystack of padding paragraphs with a needle embedded at the specified position.
    /// Each padding paragraph is deterministic but varied to avoid pattern recognition shortcuts.
    /// </summary>
    private static string GenerateHaystack(int paragraphCount, string needle, double needlePosition)
    {
        var insertAt = (int)(paragraphCount * needlePosition);
        var paragraphs = new List<string>(paragraphCount + 1);

        for (var i = 0; i < paragraphCount; i++)
        {
            if (i == insertAt)
            {
                paragraphs.Add(needle);
            }

            paragraphs.Add(GeneratePaddingParagraph(i));
        }

        return string.Join("\n\n", paragraphs);
    }

    /// <summary>
    /// Generates a deterministic but varied padding paragraph. Uses technical-sounding
    /// filler about software engineering topics so the context feels realistic rather
    /// than obviously random text.
    /// </summary>
    private static string GeneratePaddingParagraph(int index)
    {
        var topics = new[]
        {
            "When designing distributed systems, it is important to consider network partition tolerance alongside consistency and availability trade-offs. The CAP theorem provides a theoretical framework, but practical systems often operate in a spectrum between these guarantees.",
            "Continuous integration pipelines should run the full test suite on every commit to the main branch. Flaky tests that pass intermittently should be quarantined and fixed promptly, as they erode confidence in the CI signal over time.",
            "Memory allocation patterns in managed runtimes like .NET can significantly impact garbage collection pause times. Using object pooling for frequently allocated objects and avoiding large object heap fragmentation are common optimization strategies.",
            "API versioning strategies include URL path versioning, query parameter versioning, and header-based versioning. Each approach has trade-offs in terms of discoverability, cacheability, and client implementation complexity.",
            "Database indexing strategies must balance read performance against write overhead. Covering indexes can eliminate table lookups entirely, but over-indexing leads to slower inserts and increased storage requirements.",
            "Container orchestration platforms like Kubernetes provide declarative configuration for workload scheduling, automatic scaling, and self-healing capabilities. Pod disruption budgets ensure minimum availability during rolling updates.",
            "Structured logging with correlation IDs enables distributed tracing across microservice boundaries. Each service should propagate trace context headers and emit logs in a machine-parseable format like JSON.",
            "Load balancing algorithms range from simple round-robin to more sophisticated options like least-connections or weighted response time. Health checks ensure traffic is only routed to instances capable of serving requests.",
            "Feature flags allow decoupling deployment from release, enabling trunk-based development workflows. Flags should be short-lived and cleaned up promptly after the feature is fully rolled out or abandoned.",
            "TLS certificate management in production environments requires automated renewal processes to prevent expiration-related outages. Tools like cert-manager in Kubernetes can automate the entire lifecycle from issuance to rotation.",
        };

        return topics[index % topics.Length];
    }
}
