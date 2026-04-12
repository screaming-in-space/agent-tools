# Model Benchmark Report

> Generated: 2026-04-12 22:13:37 UTC
> Models tested: 1
> Models loaded: unsloth/nvidia-nemotron-3-nano-4b, devstral-small-2-24b-instruct-2512, jina-embeddings-v5-text-nano-classification, jina-embeddings-v5-text-nano-clustering, jina-embeddings-v5-text-nano-text-matching, jina-embeddings-v5-text-nano-retrieval, nvidia/nemotron-3-nano-4b, text-embedding-nomic-embed-text-v1.5

## Rankings

| Rank | Model | Config | Composite | Accuracy | Tok/s | Gen tok/s | TTFT (ms) | Think (ms) | Pass Rate |
|------|-------|--------|-----------|----------|-------|-----------|-----------|------------|-----------|
| 1 | unsloth/nvidia-nemotron-3-nano-4b | default | 0.791 | 0.888 | 29.1 | 112.0 | 895 | 769 | 5/6 (83%) |

## Hardware Summary

| GPU | VRAM | Bandwidth | CUDA Cores |
|-----|------|-----------|------------|
| rtx-4090-mobile | 16GB | 640 GB/s | 9728 |
| rtx-5090-msi-suprim | 32GB | 1792 GB/s | 21760 |

## Per-Model Scorecards

### unsloth/nvidia-nemotron-3-nano-4b (config: default)

- **Parameters:** 4B total, 4B active
- **Architecture:** hybrid-mamba
- **Context:** 262K, VRAM Q4: 3GB
- **Tool Calling:** qwen3_coder, Thinking: True

**Speed:**
- Median tok/s: 29.1
- P5 tok/s: 5.8
- Median TTFT: 895ms
- Median total: 1.1s
- **Generation tok/s: 112.0** (excluding thinking overhead)
- Thinking tokens: 898 total across all runs
- Median thinking time: 769ms

**Accuracy:**
- Mean: 0.888
- Pass rate: 5/6 (83%)

| Prompt | Category | Tok/s | Gen tok/s | Think (ms) | Duration | Accuracy | Pass |
|--------|----------|-------|-----------|------------|----------|----------|------|
| strict_format_json | instruction_following | 30.6 | 102.4 | 712 | 1.0s | 0.97 | ✓ |
| constrained_list | instruction_following | 50.5 | 127.3 | 358 | 0.6s | 1.00 | ✓ |
| stop_when_told | instruction_following | 0.0 | 0.0 | 265 | 0.4s | 0.38 | ✗ |
| minimal_prompt_json | instruction_following | 29.6 | 98.8 | 827 | 1.2s | 0.98 | ✓ |
| multi_constraint | instruction_following | 23.3 | 135.9 | 2876 | 3.5s | 1.00 | ✓ |
| negative_instruction | instruction_following | 28.6 | 121.6 | 855 | 1.1s | 1.00 | ✓ |

## Recommendations

- **Best overall:** unsloth/nvidia-nemotron-3-nano-4b — composite 0.791
- **Best speed:** unsloth/nvidia-nemotron-3-nano-4b — 29.1 tok/s, 895ms TTFT
- **Best accuracy:** unsloth/nvidia-nemotron-3-nano-4b — 0.888 mean, 83% pass rate
- **Best value (speed × accuracy):** unsloth/nvidia-nemotron-3-nano-4b

## Methodology

- Warmup iterations: 1
- Measured iterations: per-run config (default 3)
- Benchmark suites: instruction_following, extraction, markdown_generation, reasoning, multi_turn, context_window
- Accuracy scoring: deterministic (substring matching, structure validation, bigram similarity)
- Speed metrics: streaming token counting with Stopwatch-based timing
- Thinking tokens: tracked separately via TextReasoningContent; generation tok/s excludes thinking overhead
- Composite formula: `(accuracy × 0.6) + (normalized_speed × 0.3) + (pass_rate × 0.1)`
- Speed normalization: 50 tok/s = 1.0 (linear)
