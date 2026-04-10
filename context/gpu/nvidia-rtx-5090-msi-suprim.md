# NVIDIA GeForce RTX 5090 — MSI SUPRIM SOC

GPU capability profile for model planner evaluation.

---

## Identity

| Field | Value |
|-------|-------|
| Name | GeForce RTX 5090 (MSI SUPRIM SOC) |
| Chip | GB202 (Blackwell) |
| Process | TSMC 4N (5nm) |
| Compute Capability | 10.0 |
| CUDA Cores | 21,760 |
| Tensor Cores | 680 (5th gen) |
| RT Cores | 170 (4th gen) |

---

## Memory

| Field | Value |
|-------|-------|
| VRAM | 32 GB GDDR7 |
| Memory Bus | 512-bit |
| Memory Speed | 28 Gbps (effective) |
| Memory Bandwidth | 1,792 GB/s |

---

## Power & Clocks

| Field | Value |
|-------|-------|
| TDP | 575W (reference), up to ~600W under load |
| Boost Clock | 2580 MHz (MSI Center Gaming Mode) |
| Base Clock | 2017 MHz |

---

## CUDA Runtime

| Field | Value |
|-------|-------|
| Architecture | Blackwell |
| Compute Capability | sm_100 |
| CUDA Toolkit | 13.x+ (avoid 13.2 runtime for GGUF — causes poor outputs) |
| FP16 Performance | ~1800 TFLOPS |
| FP4 Performance | ~3600 TOPS (Blackwell native) |

---

## LLM Inference Capacity

| Quantization | Max Model Size (approx) | Notes |
|-------------|------------------------|-------|
| Q4_K_M | ~60B params | Comfortable with 32K context |
| Q8_0 | ~30B params | Good for 31B dense models |
| BF16 | ~14B params | Full precision with room for context |

### Models That Fit

| Model | Quant | VRAM | Context | Fit |
|-------|-------|------|---------|-----|
| Nemotron-3-Nano-4B | Q8_0 | ~5 GB | 49K+ | Trivial |
| Gemma-4-E4B | Q8_0 | ~8 GB | 128K | Easy |
| Gemma-4-26B-A4B | UD-Q4_K_XL | ~17 GB | 32K+ | Comfortable |
| Gemma-4-26B-A4B | Q8_0 | ~27 GB | 32K | Good fit |
| Gemma-4-31B | UD-Q4_K_XL | ~19 GB | 32K+ | Comfortable |
| Gemma-4-31B | Q6_K | ~25 GB | 32K | Good fit |
| Gemma-4-31B | Q8_0 | ~33 GB | 16K | Tight — near VRAM ceiling |

---

## Planner Guidance

- This is the **desktop GPU** — 32GB VRAM with massive bandwidth (1,792 GB/s).
- Best for large models (26B-31B) that don't fit on the laptop 4090.
- Gemma-4-31B at UD-Q4_K_XL is the sweet spot: 19GB leaves 13GB for KV cache (32K+ context).
- Gemma-4-26B-A4B at Q8_0 (27GB) fits with room for 32K context — near-lossless quality.
- For CSI, this GPU should handle **all heavy scanners** (`rules`, `quality`) with the best available model.
- Blackwell's FP4 support means future quantized models may fit even larger models.
- **Warning:** Do not use CUDA 13.2 runtime with GGUF — causes poor outputs. Use 13.0 or 13.1.

---

## Network Deployment

This GPU runs on a desktop machine accessible over the network. CSI connects via:

```json
{
  "Endpoint": "http://<desktop-ip>:1234/v1",
  "Model": "gemma-4-31b-it"
}
```

The planner should detect this as a remote endpoint and factor in network latency (~1-5ms LAN) when comparing with local laptop inference.

---

## Sources

- [MSI: RTX 5090 SUPRIM SOC Specs](https://www.msi.com/Graphics-Card/GeForce-RTX-5090-32G-SUPRIM-SOC/Specification)
- [ThePCEnthusiast: MSI RTX 5090 Review](https://thepcenthusiast.com/msi-geforce-rtx-5090-review-suprim-soc-edition-graphics-card/)
- [HWCooling: MSI RTX 5090 Suprim SOC Review](https://www.hwcooling.net/en/msi-geforce-rtx-5090-suprim-soc-review-600w-with-ease/)
