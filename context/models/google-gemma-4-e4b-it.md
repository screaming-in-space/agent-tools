# Google Gemma 4 E4B-it

Model capability profile for planner evaluation.

---

## Identity

| Field | Value |
|-------|-------|
| Full Name | gemma-4-E4B-it |
| Parameters | 8B total, 4.5B effective (Per-Layer Embeddings) |
| Architecture | Dense, 42 layers, 262K vocabulary, 512-token sliding window, hybrid local+global attention |
| Context Window | 128K tokens |
| Modalities | Text, Image (variable resolution), Audio (max 30s), Video (max 60s @ 1fps) |
| Data Cutoff | January 2025 |
| License | Apache 2.0 |

---

## Special Tokens

All Gemma 4 models share this token set:

| Token | Purpose |
|-------|---------|
| `<bos>` (ID 2) | Beginning of stream |
| `<eos>` (ID 1) | End of stream |
| `<|turn>` | Start of turn (before role name) |
| `<turn\|>` | End of turn / EOS for chat |
| `<\|think\|>` | Enable thinking mode (in system prompt) |
| `<\|channel>thought` | Start of thinking output |
| `<channel\|>` | End of thinking output |
| `<\|tool>` / `<tool\|>` | Tool definition block |
| `<\|tool_call>` / `<tool_call\|>` | Tool invocation block |
| `<\|tool_response>` / `<tool_response\|>` | Tool result block |
| `<\|"\|>` | String delimiter in tool args (wraps all string values) |
| `<\|image\|>` | Image placeholder |
| `<\|audio\|>` | Audio placeholder |

### Chat Template

```
<bos><|turn>system\n{system_prompt}<turn|>
<|turn>user\n{user_message}<turn|>
<|turn>model\n{response}<turn|>
```

### Thinking Output

```
<|channel>thought\n{internal reasoning}<channel|>{final answer}<turn|>
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

Running [unsloth/gemma-4-E4B-it-GGUF](https://huggingface.co/unsloth/gemma-4-E4B-it-GGUF). Fits on laptop RTX 4090 (16GB).

| Format | Size | Notes |
|--------|------|-------|
| Q8_0 | 8.2 GB | Recommended — near full quality, fits 16GB with room for context |
| Q6_K | 7.1 GB | Good balance |
| UD-Q4_K_XL | 5.1 GB | Fastest, lowest VRAM |
| BF16 | 15.1 GB | Full precision — tight fit on 16GB |

---

## Inference Settings

| Setting | Value |
|---------|-------|
| Temperature | 1.0 |
| Top-P | 0.95 |
| Top-K | 64 |
| Repetition Penalty | 1.0 (disabled) |
| Context | Start at 32K, max 128K |
| Thinking | OFF for tool-calling scanners |

---

## Capabilities

### Strong

- **Multimodal** — Text + Image + Audio + Video. Only Gemma 4 variant with audio support at this size.
- **Instruction following** — Solid for size class.
- **Tool calling** — Native `<|tool_call>` format. Works for structured workflows.
- **Multilingual** — MMMLU: 76.6%. 35+ languages.
- **Audio** — ASR and speech translation, max 30 seconds. CoVoST: 35.54.

### Moderate

- **Reasoning** — MMLU Pro: 69.4%. Better than Nemotron nano-4b but below 26B/31B.
- **Coding** — LiveCodeBench: 52.0%, Codeforces: 940. Adequate for light code tasks.
- **Vision** — MMMU Pro: 52.6%. Good for classification, captioning. Less reliable for OCR.

### Weak

- **Complex synthesis** — 4.5B effective params limit reasoning depth. Same class as nano-4b for heavy scanners.
- **Long context** — MRCR v2 128K: 25.4%. Degrades significantly at long context.
- **Math reasoning** — AIME 2026: 42.5%. Not suitable for math-heavy analysis.

---

## Benchmarks

| Benchmark | Score |
|-----------|-------|
| MMLU Pro | 69.4% |
| AIME 2026 | 42.5% |
| LiveCodeBench v6 | 52.0% |
| Codeforces ELO | 940 |
| GPQA Diamond | 58.6% |
| MMMLU | 76.6% |
| MMMU Pro | 52.6% |
| MATH-Vision | 59.5% |
| MRCR v2 128K | 25.4% |

---

## Scanner Suitability

| Scanner | Complexity | Suitability | Notes |
|---------|------------|-------------|-------|
| markdown | light | Good | Similar capability to nano-4b. Tool calling works. |
| structure | light | Good | Can parse project tables and format output. |
| rules | heavy | Marginal | Same effective parameter class as nano-4b. May struggle with synthesis. |
| quality | heavy | Marginal | Better than nano-4b marginally. Still may produce placeholders. |
| journal | medium | Good | Git data synthesis is manageable at this size. |
| done | medium | Good | Aggregation is straightforward. |

### Guidance for Planner

- Similar capability tier to nano-4b for text-only tasks.
- Prefer for scanners that benefit from multimodal (image/audio) input — none of the current CSI scanners do.
- For heavy scanners, prefer 26B-A4B or 31B if available.
- Use Q8_0 on 16GB VRAM for best quality.

---

## Sources

- [Unsloth: Gemma 4 How-To Guide](https://unsloth.ai/docs/models/gemma-4)
- [Unsloth GGUF: gemma-4-E4B-it-GGUF](https://huggingface.co/unsloth/gemma-4-E4B-it-GGUF)
- [Google: Gemma 4 Prompt Formatting](https://ai.google.dev/gemma/docs/core/prompt-formatting-gemma4)
