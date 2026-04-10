# Model Benchmark Report

> Generated: 2026-04-10 20:07:21 UTC
> Models tested: 6
> Models loaded: devstral-small-2-24b-instruct-2512, jina-embeddings-v5-text-nano-classification, jina-embeddings-v5-text-nano-clustering, jina-embeddings-v5-text-nano-text-matching, jina-embeddings-v5-text-nano-retrieval, unsloth/nvidia-nemotron-3-nano-4b, nvidia/nemotron-3-nano-4b, text-embedding-nomic-embed-text-v1.5, gemma-4-26b-a4b-it, gemma-4-31b-it, gemma-4-e4b-it, glm-4.7-flash, nemotron-3-nano-30b-a3b, qwen3-coder-30b-a3b-instruct

## Rankings

| Rank | Model | Config | Composite | Accuracy | Tok/s (median) | TTFT (ms) | Pass Rate |
|------|-------|--------|-----------|----------|----------------|-----------|-----------|
| 1 | gemma-4-e4b-it | gemma-e4b | 1.000 | 1.000 | 181.8 | 91 | 9/9 (100%) |
| 2 | gemma-4-26b-a4b-it | gemma-26b | 0.998 | 0.997 | 174.3 | 120 | 9/9 (100%) |
| 3 | gemma-4-31b-it | gemma | 0.995 | 0.992 | 69.5 | 210 | 9/9 (100%) |
| 4 | qwen3-coder-30b-a3b-instruct | qwen | 0.971 | 0.952 | 295.1 | 60 | 9/9 (100%) |
| 5 | unsloth/nvidia-nemotron-3-nano-4b | default | 0.876 | 0.938 | 37.4 | 1737 | 8/9 (89%) |
| 6 | glm-4.7-flash | glm | 0.346 | 0.521 | 0.0 | 25139 | 3/9 (33%) |

## Hardware Summary

| GPU | VRAM | Bandwidth | CUDA Cores |
|-----|------|-----------|------------|
| rtx-4090-mobile | 16GB | 640 GB/s | 9728 |
| rtx-5090-msi-suprim | 32GB | 1792 GB/s | 21760 |

## Per-Model Scorecards

### gemma-4-e4b-it (config: gemma-e4b)

- **Parameters:** 8B total, 4.5B active
- **Architecture:** dense
- **Context:** 128K, VRAM Q4: 5GB
- **Tool Calling:** gemma4_native, Thinking: True

**Speed:**
- Median tok/s: 181.8
- P5 tok/s: 134.9
- Median TTFT: 91ms
- Median total: 0.5s

**Accuracy:**
- Mean: 1.000
- Pass rate: 9/9 (100%)

| Prompt | Category | Tok/s | Duration | Accuracy | Pass |
|--------|----------|-------|----------|----------|------|
| strict_format_json | instruct | 135.3 | 0.2s | 1.00 | ✓ |
| constrained_list | instruct | 211.5 | 0.3s | 1.00 | ✓ |
| stop_when_told | instruct | 165.1 | 0.2s | 1.00 | ✓ |
| extract_model_specs | extract | 181.3 | 0.6s | 1.00 | ✓ |
| extract_key_value | extract | 173.6 | 0.3s | 1.00 | ✓ |
| generate_context_map | markdown | 246.4 | 1.2s | 1.00 | ✓ |
| generate_table | markdown | 179.1 | 0.5s | 1.00 | ✓ |
| model_selection_reasoning | reason | 204.7 | 0.6s | 1.00 | ✓ |
| comparative_analysis | reason | 204.7 | 0.5s | 1.00 | ✓ |

### gemma-4-26b-a4b-it (config: gemma-26b)

- **Parameters:** 25.2B total, 3.8B active
- **Architecture:** moe
- **Context:** 256K, VRAM Q4: 17GB
- **Tool Calling:** gemma4_native, Thinking: True

**Speed:**
- Median tok/s: 174.3
- P5 tok/s: 127.6
- Median TTFT: 120ms
- Median total: 0.6s

**Accuracy:**
- Mean: 0.997
- Pass rate: 9/9 (100%)

| Prompt | Category | Tok/s | Duration | Accuracy | Pass |
|--------|----------|-------|----------|----------|------|
| strict_format_json | instruct | 129.7 | 0.3s | 0.97 | ✓ |
| constrained_list | instruct | 219.7 | 0.4s | 1.00 | ✓ |
| stop_when_told | instruct | 171.6 | 0.3s | 1.00 | ✓ |
| extract_model_specs | extract | 162.2 | 0.8s | 1.00 | ✓ |
| extract_key_value | extract | 146.6 | 0.4s | 1.00 | ✓ |
| generate_context_map | markdown | 210.6 | 1.1s | 1.00 | ✓ |
| generate_table | markdown | 159.6 | 0.6s | 1.00 | ✓ |
| model_selection_reasoning | reason | 176.2 | 0.7s | 1.00 | ✓ |
| comparative_analysis | reason | 183.4 | 0.9s | 1.00 | ✓ |

### gemma-4-31b-it (config: gemma)

- **Parameters:** 30.7B total, 30.7B active
- **Architecture:** dense
- **Context:** 256K, VRAM Q4: 20GB
- **Tool Calling:** gemma4_native, Thinking: True

**Speed:**
- Median tok/s: 69.5
- P5 tok/s: 56.0
- Median TTFT: 210ms
- Median total: 1.5s

**Accuracy:**
- Mean: 0.992
- Pass rate: 9/9 (100%)

| Prompt | Category | Tok/s | Duration | Accuracy | Pass |
|--------|----------|-------|----------|----------|------|
| strict_format_json | instruct | 55.8 | 0.6s | 0.97 | ✓ |
| constrained_list | instruct | 86.1 | 0.7s | 1.00 | ✓ |
| stop_when_told | instruct | 73.5 | 0.7s | 1.00 | ✓ |
| extract_model_specs | extract | 64.1 | 1.9s | 1.00 | ✓ |
| extract_key_value | extract | 60.9 | 0.9s | 1.00 | ✓ |
| generate_context_map | markdown | 85.9 | 2.8s | 1.00 | ✓ |
| generate_table | markdown | 63.7 | 1.5s | 1.00 | ✓ |
| model_selection_reasoning | reason | 69.1 | 1.7s | 1.00 | ✓ |
| comparative_analysis | reason | 75.2 | 2.7s | 0.96 | ✓ |

### qwen3-coder-30b-a3b-instruct (config: qwen)

**Speed:**
- Median tok/s: 295.1
- P5 tok/s: 193.8
- Median TTFT: 60ms
- Median total: 0.4s

**Accuracy:**
- Mean: 0.952
- Pass rate: 9/9 (100%)

| Prompt | Category | Tok/s | Duration | Accuracy | Pass |
|--------|----------|-------|----------|----------|------|
| strict_format_json | instruct | 191.8 | 0.2s | 0.97 | ✓ |
| constrained_list | instruct | 312.8 | 0.2s | 1.00 | ✓ |
| stop_when_told | instruct | 295.1 | 0.1s | 1.00 | ✓ |
| extract_model_specs | extract | 249.1 | 0.5s | 1.00 | ✓ |
| extract_key_value | extract | 219.3 | 0.3s | 1.00 | ✓ |
| generate_context_map | markdown | 385.5 | 0.9s | 1.00 | ✓ |
| generate_table | markdown | 301.8 | 0.9s | 0.97 | ✓ |
| model_selection_reasoning | reason | 275.3 | 0.8s | 0.62 | ✓ |
| comparative_analysis | reason | 284.9 | 0.4s | 1.00 | ✓ |

### unsloth/nvidia-nemotron-3-nano-4b (config: default)

- **Parameters:** 4B total, 4B active
- **Architecture:** hybrid-mamba
- **Context:** 262K, VRAM Q4: 3GB
- **Tool Calling:** qwen3_coder, Thinking: True

**Speed:**
- Median tok/s: 37.4
- P5 tok/s: 18.3
- Median TTFT: 1737ms
- Median total: 2.6s

**Accuracy:**
- Mean: 0.938
- Pass rate: 8/9 (89%)

| Prompt | Category | Tok/s | Duration | Accuracy | Pass |
|--------|----------|-------|----------|----------|------|
| strict_format_json | instruct | 34.4 | 0.9s | 0.97 | ✓ |
| constrained_list | instruct | 40.5 | 1.2s | 1.00 | ✓ |
| stop_when_told | instruct | 28.5 | 1.2s | 1.00 | ✓ |
| extract_model_specs | extract | 41.7 | 3.4s | 1.00 | ✓ |
| extract_key_value | extract | 19.9 | 3.0s | 1.00 | ✓ |
| generate_context_map | markdown | 54.9 | 2.6s | 1.00 | ✓ |
| generate_table | markdown | 49.7 | 2.2s | 1.00 | ✓ |
| model_selection_reasoning | reason | 23.0 | 4.5s | 1.00 | ✓ |
| comparative_analysis | reason | 24.6 | 2.8s | 0.47 | ✗ |

### glm-4.7-flash (config: glm)

**Speed:**
- Median tok/s: 0.0
- P5 tok/s: 0.0
- Median TTFT: 25139ms
- Median total: 25.2s

**Accuracy:**
- Mean: 0.521
- Pass rate: 3/9 (33%)

| Prompt | Category | Tok/s | Duration | Accuracy | Pass |
|--------|----------|-------|----------|----------|------|
| strict_format_json | instruct | 0.0 | 25.2s | 0.30 | ✗ |
| constrained_list | instruct | 0.0 | 25.1s | 0.43 | ✗ |
| stop_when_told | instruct | 0.3 | 3.7s | 0.90 | ✓ |
| extract_model_specs | extract | 0.0 | 25.3s | 0.27 | ✗ |
| extract_key_value | extract | 0.0 | 25.2s | 0.38 | ✗ |
| generate_context_map | markdown | 205.4 | 25.2s | 0.93 | ✓ |
| generate_table | markdown | 0.0 | 25.3s | 0.27 | ✗ |
| model_selection_reasoning | reason | 0.0 | 25.4s | 0.38 | ✗ |
| comparative_analysis | reason | 12.7 | 25.3s | 0.84 | ✓ |

## Recommendations

- **Best overall:** gemma-4-e4b-it — composite 1.000
- **Best speed:** qwen3-coder-30b-a3b-instruct — 295.1 tok/s, 60ms TTFT
- **Best accuracy:** gemma-4-e4b-it — 1.000 mean, 100% pass rate
- **Best value (speed × accuracy):** qwen3-coder-30b-a3b-instruct

## Methodology

- Warmup iterations: 1
- Measured iterations: per-run config (default 3)
- Benchmark suites: instruction_following, extraction, markdown_generation, reasoning
- Accuracy scoring: deterministic (substring matching, structure validation, bigram similarity)
- Speed metrics: streaming token counting with Stopwatch-based timing
- Composite formula: `(accuracy × 0.6) + (normalized_speed × 0.3) + (pass_rate × 0.1)`
- Speed normalization: 50 tok/s = 1.0 (linear)
