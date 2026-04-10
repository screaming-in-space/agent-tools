# NVIDIA Nemotron 3 Nano 4B

Model capability profile for planner evaluation.

---

## Identity

| Field | Value |
|-------|-------|
| Full Name | NVIDIA-Nemotron-3-Nano-4B |
| Parameters | 3.97B |
| Architecture | Mamba-2 + Transformer Hybrid (21 Mamba, 4 Attention, 17 MLP layers) |
| Positional Encoding | NoPE (no explicit positional embeddings — YaRN not needed) |
| Context Window | 262K default, up to 1M (adjust `max_position_embeddings`) |
| Data Cutoff | September 2024 |
| Training | December 2025 – January 2026, >10T tokens |
| Release | March 2026 |
| License | NVIDIA Nemotron Open Model License (commercial OK) |
| Parent Model | Compressed from Nemotron-Nano-9B-v2 via Nemotron Elastic framework |

---

## GGUF Variants (Unsloth)

We run the [unsloth/NVIDIA-Nemotron-3-Nano-4B-GGUF](https://huggingface.co/unsloth/NVIDIA-Nemotron-3-Nano-4B-GGUF) quantizations.

| Format | Size | VRAM | Notes |
|--------|------|------|-------|
| BF16 | 7.9 GB | ~8 GB | Full precision, safetensors |
| FP8 | 4.0 GB | ~4 GB | 100% median accuracy, 1.8x throughput, DGX Spark/Jetson Thor |
| Q8_0 GGUF | 4.2 GB | ~5 GB | Recommended for quality-sensitive tasks |
| Q4_K_M GGUF | 2.5 GB | ~3 GB | Best for constrained VRAM, Ollama/llama.cpp |

---

## Inference Settings

NVIDIA-recommended settings per use case:

| Use Case | Temperature | Top-P | Max Tokens | Notes |
|----------|-------------|-------|------------|-------|
| General chat/instruction | 1.0 | 1.0 | 32,768+ | Default, broad generation |
| Tool calling (our use case) | 0.6 | 0.95 | 32,768 | Focused, deterministic |
| Deep reasoning | 1.0 | 0.95 | 262,144 | Increase as VRAM allows |

### Reasoning Mode

Reasoning is ON by default. The model produces `<think>...</think>` tokens (ID 12/13) before the response.

- **Reasoning ON**: Higher accuracy for complex tasks. Temperature 1.0, Top-P 0.95.
- **Reasoning OFF**: Direct answers, faster. Disable via `enable_thinking=False`.

For CSI tool-calling scanners, reasoning-OFF with temp 0.6 is recommended — tool calls don't benefit from chain-of-thought, and it reduces token waste.

### Chat Template

```
<|im_start|>system\n{system_prompt}<|im_end|>
<|im_start|>user\n{user_message}<|im_end|>
<|im_start|>assistant\n<think>{reasoning}</think>{response}<|im_end|>
```

Tool call parser: `qwen3_coder` (vLLM) or native via OpenAI-compatible API.

---

## Capabilities

### Strong

- **Instruction following** — SOTA in size class. IFEval-Instruction: 88.0 (off), 92.0 (on).
- **Tool/function calling** — Native Qwen3-Coder format. BFCL v3: 61.1. Trained on 26.2B tokens of multi-turn conversational tool-calling data.
- **Structured output** — JSON, XML generation trained via multi-environment RL.
- **Math reasoning** — MATH500: 95.4, AIME25: 78.5 (reasoning-on).
- **Hallucination avoidance** — HaluEval: 62.2. Competitive for size class.
- **Long context** — RULER 128K: 91.1. NoPE architecture extends cleanly to 1M.
- **Edge deployment** — Lowest VRAM footprint in class. 18 tok/s on Jetson Orin Nano 8GB (Q4_K_M).

### Moderate

- **Code generation** — Trained on GitHub (permissive licenses). Multi-language. Not specialized like Devstral or Qwen-Coder.
- **Multilingual** — English primary. DE, ES, FR, IT, KO, PT, RU, JA, ZH post-trained.
- **Agentic workflows** — Trained on calendar scheduling, multi-turn agent interactions. Simple agent loops work well.

### Weak

- **Complex multi-step synthesis** — 4B parameters limit reasoning depth. After 10+ tool calls, may lose coherence or loop.
- **Large output generation** — Tends toward shorter outputs. May not produce 500+ word structured documents reliably.
- **Code analysis reasoning** — Can call Roslyn/analysis tools but may struggle to interpret and synthesize complex quality metrics into actionable reports.
- **Self-correction** — When a tool returns an error, retries with slight variations rather than reasoning about the correct fix.

---

## Benchmarks

| Benchmark | Score |
|-----------|-------|
| IFEval-Prompt | 82.8 (off) / 87.9 (on) |
| IFEval-Instruction | 88.0 (off) / 92.0 (on) |
| MATH500 | 95.4 |
| AIME25 | 78.5 |
| GPQA | 53.2 |
| BFCL v3 (tool calling) | 61.1 |
| RULER (128K) | 91.1 |
| HaluEval | 62.2 |
| EQ-Bench3 | 63.2 |

---

## Scanner Suitability

Planner should use this table when assigning the model to CSI scanners.

| Scanner | Complexity | Suitability | Notes |
|---------|------------|-------------|-------|
| markdown | light | Good | Lists files + reads content + writes map. Tool calling works well. Keep prompt concise. |
| structure | light | Good | Tool outputs are structured tables. Model copies and formats reliably. |
| rules | heavy | Marginal | Must synthesize comments from 30+ files into design principles. May produce shallow output or echo prompt. |
| quality | heavy | Marginal | Roslyn tools do analysis, but model must interpret metrics and produce graded report. May use placeholders. |
| journal | medium | Good | Git tools return structured data. Model synthesizes into narratives. Keep entries short (100-200 words). |
| done | medium | Good | Reads prior scanner output and checks existence. Straightforward aggregation. |

### Guidance for Planner

- Assign to **light** and **medium** scanners confidently.
- Assign to **heavy** scanners only if no better model is loaded.
- If a larger model (14B+) is available, prefer it for `rules` and `quality`.
- Use `"skip"` for heavy scanners if this is the only model and output quality is critical.
- MaxOutputTokens: 4096 for light, 8192 for heavy scanners.
- Temperature: 0.6 for tool-calling scanners (NVIDIA recommended). 0.3 is too deterministic for synthesis.
- Reasoning OFF is recommended for tool-calling scanners — reduces token waste.

---

## Deployment (Unsloth GGUF via llama-server)

For CSI's OpenAI-compatible endpoint, run via llama-server:

```bash
./llama.cpp/llama-server \
    --model unsloth/NVIDIA-Nemotron-3-Nano-4B-GGUF/NVIDIA-Nemotron-3-Nano-4B-Q8_0.gguf \
    --alias "unsloth/nvidia-nemotron-3-nano-4b" \
    --ctx-size 32768 \
    --temp 0.6 \
    --top-p 0.95 \
    --port 1234
```

Or via Ollama:
```bash
ollama run nemotron-3-nano-4b
```

The model exposes an OpenAI-compatible `/v1/chat/completions` endpoint. CSI connects via `AgentModelOptions.Endpoint = "http://localhost:1234/v1"`.

---

## Known Behaviors (CSI-Specific)

Observed during CSI scanner runs:

- **Tool call looping** — When a tool returns an error, retries with slight path variations instead of reasoning about the fix. Mitigated by: correct tool output→input contracts, excluding stale output from scans.
- **Code fence wrapping** — May wrap WriteOutput content in ` ```markdown ``` `. Prompts should explicitly forbid this.
- **Prompt echoing** — May echo raw prompt rules into output. Prompts should say "Do NOT include the Rules section."
- **Coherence loss** — Produces `<｜begin▁of▁sentence｜>` tokens on generation failure. Indicates MaxIterations exhausted or context overflow.
- **Short output preference** — Prefers bullet points and tables over prose. Design prompts for structured output, not paragraphs.
- **Stale context poisoning** — If previous run's output files are in the scan directory, model reads them and uses their (potentially wrong) paths/content. Mitigated by: ExcludeDirectory on the context/ folder.

---

## Sources

- [Unsloth: Nemotron 3 Nano How-To Guide](https://unsloth.ai/docs/models/nemotron-3-nano)
- [Unsloth GGUF: NVIDIA-Nemotron-3-Nano-4B-GGUF](https://huggingface.co/unsloth/NVIDIA-Nemotron-3-Nano-4B-GGUF)
- [NVIDIA Blog: Nemotron 3 Nano 4B](https://huggingface.co/blog/nvidia/nemotron-3-nano-4b)
- [HuggingFace Model Card](https://huggingface.co/nvidia/NVIDIA-Nemotron-3-Nano-4B-BF16)
- [Technical Report: arXiv 2512.20856](https://arxiv.org/abs/2512.20856)
