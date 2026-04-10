# Google Gemma 4 31B-it

Model capability profile for planner evaluation.

---

## Identity

| Field | Value |
|-------|-------|
| Full Name | gemma-4-31B-it |
| Parameters | 30.7B (dense) |
| Architecture | Dense Transformer, 60 layers, 262K vocabulary, 1024-token sliding window |
| Context Window | 256K tokens |
| Modalities | Text, Image (variable resolution), Video (up to 60s @ 1fps) |
| Data Cutoff | January 2025 |
| License | Apache 2.0 |
---

## Special Tokens

All Gemma 4 models share this token set:

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
| `<\|"\|>` | String delimiter in tool args (wraps all string values) |
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

Running [unsloth/gemma-4-31B-it-GGUF](https://huggingface.co/unsloth/gemma-4-31B-it-GGUF). RTX 5090 (32GB VRAM) fits up to Q8_0.

| Format | Size | VRAM (approx) | Notes |
|--------|------|---------------|-------|
| UD-Q4_K_XL | 18.8 GB | ~20 GB | Dynamic 4-bit. Best speed/quality for 32GB cards. |
| Q5_K_M | 21.7 GB | ~23 GB | Good balance. |
| Q6_K | 25.2 GB | ~27 GB | Near-lossless. |
| Q8_0 | 32.6 GB | ~34 GB | Full quality. Tight fit on 32GB — may need reduced context. |
| BF16 | 61.4 GB | ~62 GB | Full precision. Needs 2x GPU or CPU offload. |

---

## Inference Settings

Google-recommended defaults:

| Setting | Value | Notes |
|---------|-------|-------|
| Temperature | 1.0 | Default for all use cases |
| Top-P | 0.95 | |
| Top-K | 64 | Gemma-specific — most other models don't use top-k |
| Repetition Penalty | 1.0 (disabled) | Enable only if looping observed |
| Context | Start at 32K, increase as VRAM allows | Max 256K |

### Thinking Mode

Enable by prepending `<|think|>` to the system prompt. The model outputs:
```
<|channel>thought
[internal reasoning]
<channel|>
[final answer]
```

For CSI tool-calling scanners, thinking OFF is recommended — tool calls don't benefit from chain-of-thought and it wastes tokens.

**Multi-turn rule:** Strip thinking blocks from conversation history. Only keep the final answer.

### Chat Template

```
<bos><|turn>system\n{system_prompt}<turn|>
<|turn>user\n{user_message}<turn|>
<|turn>model\n{response}<turn|>
```

---

## Capabilities

### Strong

- **Reasoning** — MMLU Pro: 85.2%, AIME 2026: 89.2%, GPQA Diamond: 84.3%. Significantly stronger than 4B models.
- **Coding** — LiveCodeBench v6: 80.0%, Codeforces ELO: 2150. Strong code generation and analysis.
- **Tool/function calling** — Native structured tool use support. Reliable multi-step tool orchestration.
- **Long context** — 256K window. MRCR v2 128K: 66.4%. Handles large codebases without truncation.
- **Multilingual** — MMMLU: 88.4%. 35+ languages with strong performance.
- **Vision** — MMMU Pro: 76.9%. Variable resolution (70-1120 visual tokens). Good for diagrams, screenshots, code images.
- **Structured output** — Reliable JSON, XML, markdown table generation at this parameter count.

### Moderate

- **Instruction following** — Strong but not SOTA for size class (Nemotron beats it on IFEval despite being 8x smaller).
- **Hallucination avoidance** — Better than small models but not specifically optimized for it.

### Weak

- **Edge deployment** — 31B dense model. Minimum ~20GB VRAM at Q4. Not suitable for laptops or embedded.
- **Speed** — Dense architecture means all 31B params active per token. Slower than MoE alternatives (26B-A4B).
- **Audio** — Not supported on 31B (E2B/E4B only).

---

## Benchmarks

| Benchmark | Score |
|-----------|-------|
| MMLU Pro | 85.2% |
| AIME 2026 (no tools) | 89.2% |
| GPQA Diamond | 84.3% |
| BigBench Extra Hard | 74.4% |
| LiveCodeBench v6 | 80.0% |
| Codeforces ELO | 2150 |
| MMMLU | 88.4% |
| MMMU Pro | 76.9% |
| MATH-Vision | 85.6% |
| MRCR v2 128K (avg) | 66.4% |

---

## Scanner Suitability

Planner should use this table when assigning the model to CSI scanners.

| Scanner | Complexity | Suitability | Notes |
|---------|------------|-------------|-------|
| markdown | light | Excellent | Overkill but will produce high-quality output. Fast enough on RTX 5090. |
| structure | light | Excellent | 31B can reason about dependency graphs and architecture patterns reliably. |
| rules | heavy | Excellent | Strong synthesis — can extract design principles from 30+ files and produce coherent rules. This is where 31B shines over 4B. |
| quality | heavy | Excellent | Can interpret Roslyn metrics, grade projects, and produce actionable recommendations. No placeholder issues. |
| journal | medium | Excellent | Git data synthesis into narratives is straightforward at this parameter count. |
| done | medium | Excellent | Aggregation and checklist production are trivial for 31B. |

### Guidance for Planner

- Assign to **heavy** scanners (`rules`, `quality`) where 4B models struggle.
- For light scanners, prefer a smaller/faster model if one is loaded — 31B is overkill for listing files.
- If this is the only model loaded, assign it to everything.
- Temperature 1.0 with top-k 64 is the recommended setting (Google default). Do not use 0.3.
- Thinking OFF for tool-calling scanners to reduce token waste.
- MaxOutputTokens: 8192 recommended — this model can produce full structured documents.
- On RTX 5090 with UD-Q4_K_XL, expect ~15-25 tok/s depending on context length.

---

## Deployment (Unsloth GGUF via llama-server)

For CSI's OpenAI-compatible endpoint on the desktop RTX 5090:

```bash
./llama.cpp/llama-server \
    --model unsloth/gemma-4-31B-it-GGUF/gemma-4-31B-it-UD-Q4_K_XL.gguf \
    --alias "gemma-4-31b-it" \
    --ctx-size 32768 \
    --temp 1.0 \
    --top-p 0.95 \
    --top-k 64 \
    --port 1234 \
    --chat-template-kwargs '{"enable_thinking":false}'
```

CSI connects via `AgentModelOptions.Endpoint = "http://<desktop-ip>:1234/v1"`.

---

## Known Behaviors (CSI-Specific)

- **Thinking tokens in tool calls** — If thinking is enabled, the model emits `<|channel>thought ... <channel|>` before tool calls. Disable thinking for tool-calling scanners to avoid parsing issues.
- **Top-K sensitivity** — Gemma 4 was trained with top-k=64. Omitting it may produce different behavior than expected.
- **Code fence habits** — May wrap structured output in markdown code fences. Prompts should forbid this.
- **Dense = slow** — At 31B all-active, expect slower inference than MoE models of similar total parameter count. Budget more time per scanner.

---

## Sources

- [Unsloth: Gemma 4 How-To Guide](https://unsloth.ai/docs/models/gemma-4)
- [Unsloth GGUF: gemma-4-31B-it-GGUF](https://huggingface.co/unsloth/gemma-4-31B-it-GGUF)
- [Google DeepMind: Gemma 4 Blog](https://blog.google/innovation-and-ai/technology/developers-tools/gemma-4/)
- [HuggingFace Blog: Gemma 4](https://huggingface.co/blog/gemma4)
