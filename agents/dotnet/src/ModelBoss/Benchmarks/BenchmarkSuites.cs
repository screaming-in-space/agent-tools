namespace ModelBoss.Benchmarks;

/// <summary>
/// Built-in benchmark prompt suites that test specific model capabilities.
/// Each suite targets a dimension you care about when selecting a local model:
/// can it follow instructions, extract structured data, produce markdown, call tools correctly.
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
    ];

    /// <summary>
    /// Tests whether the model follows explicit constraints: format, length, inclusions, exclusions.
    /// This is the #1 failure mode for small models — they drift off-instruction.
    /// </summary>
    public static IReadOnlyList<BenchmarkPrompt> InstructionFollowing() =>
    [
        new BenchmarkPrompt
        {
            Name = "strict_format_json",
            Category = "instruction_following",
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
            Category = "instruction_following",
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
            Category = "instruction_following",
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
    ];

    /// <summary>
    /// Tests structured data extraction from unstructured text.
    /// Models that can't do this reliably will botch tool-call argument parsing.
    /// </summary>
    public static IReadOnlyList<BenchmarkPrompt> Extraction() =>
    [
        new BenchmarkPrompt
        {
            Name = "extract_model_specs",
            Category = "extraction",
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
            Category = "extraction",
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
    ];

    /// <summary>
    /// Tests the model's ability to produce well-structured markdown.
    /// This is what our CSI scanners need — if the model can't produce clean markdown, it's useless.
    /// </summary>
    public static IReadOnlyList<BenchmarkPrompt> MarkdownGeneration() =>
    [
        new BenchmarkPrompt
        {
            Name = "generate_context_map",
            Category = "markdown_generation",
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
            Category = "markdown_generation",
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
        new BenchmarkPrompt
        {
            Name = "model_selection_reasoning",
            Category = "reasoning",
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
            Category = "reasoning",
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
    ];
}
