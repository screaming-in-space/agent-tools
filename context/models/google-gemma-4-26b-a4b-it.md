# Google Gemma 4 26B-A4B-it

Model capability profile for planner evaluation.

---

## Identity

| Field | Value |
|-------|-------|
| Full Name | gemma-4-26B-A4B-it |
| Parameters | 25.2B total, 3.8B active (MoE) |
| Architecture | Mixture-of-Experts, 30 layers, 8 active / 128 total + 1 shared expert, 262K vocabulary, 1024-token sliding window |
| Context Window | 256K tokens |
| Modalities | Text, Image (variable resolution), Video (max 60s @ 1fps) |
| Audio | Not supported (E2B/E4B only) |
| Data Cutoff | January 2025 |
| License | Apache 2.0 |

---

## Special Tokens

Same token set as all Gemma 4 models:

| Token | Purpose |
|-------|---------|
| `<bos>` (ID 2) | Beginning of stream |
| `<eos>` (ID 1) | End of stream |
| `<\|turn>` | Start of turn (before role name) |
| `<turn\|>` | End of turn / EOS for chat |
| `<\|think\|>` | Enable thinking mode (in system prompt) |
| `<\|channel>thought` | Start of thinking output |
| `<channel\|>` | End of thinking output |
| `<\|tool>` / `<tool\|>` | Tool definition block |
| `<\|tool_call>` / `<tool_call\|>` | Tool invocation block |
| `<\|tool_response>` / `<tool_response\|>` | Tool result block |
| `<\|"\|>` | String delimiter in tool args |
| `<\|image\|>` | Image placeholder |

### Chat Template

```
<bos><|turn>system\n{system_prompt}<turn|>
<|turn>user\n{user_message}<turn|>
<|turn>model\n{response}<turn|>
```

### Tool Call Format

```
<|turn>model
<|tool_call>
{"name": "function_name", "arguments": {"key": <|"|>value<|"|>}}
<tool_call|><turn|>
<|turn>tool
<|tool_response>
{"result": "..."}
<tool_response|><turn|>
```

---

## GGUF Variants (Unsloth)

Running [unsloth/gemma-4-26B-A4B-it-GGUF](https://huggingface.co/unsloth/gemma-4-26B-A4B-it-GGUF).

MoE advantage: only 3.8B active params per token → faster inference than 31B dense despite larger total size.

| Format | Size | VRAM | Notes |
|--------|------|------|-------|
| UD-Q4_K_XL | 17.1 GB | ~18 GB | Fits laptop RTX 4090 (16GB) with reduced context. Best speed/quality tradeoff. |
| UD-Q5_K_XL | 21.2 GB | ~23 GB | Desktop RTX 5090 (32GB). Good balance. |
| Q8_0 | 26.9 GB | ~28 GB | Desktop RTX 5090. Near full quality. |
| BF16 | 50.5 GB | ~52 GB | Needs 2x GPU or heavy CPU offload. |

---

## Inference Settings

| Setting | Value |
|---------|-------|
| Temperature | 1.0 |
| Top-P | 0.95 |
| Top-K | 64 |
| Repetition Penalty | 1.0 (disabled) |
| Context | Start at 32K, max 256K |
| Thinking | OFF for tool-calling scanners |

---

## Capabilities

### Strong

- **Reasoning** — MMLU Pro: 82.6%, AIME 2026: 88.3%, GPQA Diamond: 82.3%. Near 31B quality.
- **Coding** — LiveCodeBench: 77.1%, Codeforces: 1718. Strong code generation.
- **Tool/function calling** — Native `<|tool_call>` format. Designed for agentic workflows.
- **Speed** — MoE with 3.8B active params means ~7x faster per-token than 31B dense. Best throughput of any Gemma 4 large model.
- **Long context** — 256K window. Good for large codebases.
- **Multilingual** — MMMLU: 86.3%. 35+ languages.
- **Vision** — MMMU Pro: 73.8%. Strong for charts, screenshots, UI.

### Moderate

- **Complex synthesis** — Slightly below 31B on reasoning benchmarks but still far above 4B models. Good for heavy scanners.
- **Long context retrieval** — MRCR v2 128K: 44.1%. Decent but below 31B (66.4%).

### Weak

- **Audio** — Not supported (E2B/E4B only).
- **Absolute quality ceiling** — 31B beats it on every benchmark by 2-5%. If quality is paramount and speed doesn't matter, 31B is better.

---

## Benchmarks

| Benchmark | Score |
|-----------|-------|
| MMLU Pro | 82.6% |
| AIME 2026 | 88.3% |
| GPQA Diamond | 82.3% |
| LiveCodeBench v6 | 77.1% |
| Codeforces ELO | 1718 |
| MMMLU | 86.3% |
| MMMU Pro | 73.8% |
| MRCR v2 128K | 44.1% |

---

## Scanner Suitability

| Scanner | Complexity | Suitability | Notes |
|---------|------------|-------------|-------|
| markdown | light | Excellent | Overkill but fast thanks to MoE. |
| structure | light | Excellent | Reliable project analysis and formatting. |
| rules | heavy | Good | Strong synthesis capability. Near 31B quality at much faster speed. |
| quality | heavy | Good | Can interpret Roslyn metrics and produce graded reports. Better than 4B, slightly below 31B. |
| journal | medium | Excellent | Git data synthesis is reliable at this parameter count. |
| done | medium | Excellent | Trivial for this model. |

### Guidance for Planner

- **Best speed/quality tradeoff** for heavy scanners when both 26B-A4B and 31B are available.
- Prefer 26B-A4B over 31B for most scanners — 88% of 31B's quality at ~7x the speed.
- Reserve 31B only for `quality` scanner where grading accuracy matters most.
- On laptop RTX 4090: UD-Q4_K_XL (17.1GB) is tight but usable with 32K context.
- On desktop RTX 5090: Q8_0 (26.9GB) fits comfortably.
- Thinking OFF for tool-calling scanners.
- MaxOutputTokens: 8192 recommended.

---

## Deployment (Unsloth GGUF via llama-server)

Desktop RTX 5090:
```bash
./llama.cpp/llama-server \
    --model unsloth/gemma-4-26B-A4B-it-GGUF/gemma-4-26B-A4B-it-UD-Q4_K_XL.gguf \
    --alias "gemma-4-26b-a4b-it" \
    --ctx-size 32768 \
    --temp 1.0 \
    --top-p 0.95 \
    --top-k 64 \
    --port 1234 \
    --chat-template-kwargs '{"enable_thinking":false}'
```

---

## Known Behaviors (CSI-Specific)

- **MoE routing noise** — Occasional inconsistency in output quality between runs. Temperature 1.0 + top-k 64 mitigates this.
- **Code fence wrapping** — Same as 31B. Prompts should forbid it.
- **Thinking token bleed** — If thinking is enabled, may emit `<|channel>thought ... <channel|>` before tool calls. Disable thinking for CSI.
- **Fast but hungry on memory** — MoE means the full 25.2B params must be in memory even though only 3.8B are active per token.

---

## Sources

- [Unsloth: Gemma 4 How-To Guide](https://unsloth.ai/docs/models/gemma-4)
- [Unsloth GGUF: gemma-4-26B-A4B-it-GGUF](https://huggingface.co/unsloth/gemma-4-26B-A4B-it-GGUF)
- [Google: Gemma 4 Prompt Formatting](https://ai.google.dev/gemma/docs/core/prompt-formatting-gemma4)
- [Google: Gemma 4 Thinking Mode](https://ai.google.dev/gemma/docs/capabilities/thinking)
