# NVIDIA GeForce RTX 4090 Laptop GPU

GPU capability profile for model planner evaluation.

---

## Identity

| Field | Value |
|-------|-------|
| Name | GeForce RTX 4090 Laptop GPU |
| Chip | AD103 (Ada Lovelace) |
| Process | TSMC 4N (5nm) |
| Compute Capability | 8.9 |
| CUDA Cores | 9,728 |
| Tensor Cores | 304 (4th gen) |
| RT Cores | 76 (3rd gen) |

---

## Memory

| Field | Value |
|-------|-------|
| VRAM | 16 GB GDDR6 |
| Memory Bus | 256-bit |
| Memory Speed | 20 Gbps (effective) |
| Memory Bandwidth | 640 GB/s |

---

## Power & Clocks

| Field | Value |
|-------|-------|
| TGP Range | 80W – 150W (configurable by OEM) |
| Boost Clock (80W) | 1455 MHz |
| Boost Clock (150W) | 2040 MHz |

---

## CUDA Runtime

| Field | Value |
|-------|-------|
| Architecture | Ada Lovelace |
| Compute Capability | sm_89 |
| CUDA Toolkit | 12.x+ |
| FP16 Performance | ~330 TFLOPS (varies by TGP) |
| INT8 Performance | ~660 TOPS (varies by TGP) |

---

## LLM Inference Capacity

| Quantization | Max Model Size (approx) | Notes |
|-------------|------------------------|-------|
| Q4_K_M | ~28B params | Tight fit at 16GB, reduced context |
| Q8_0 | ~14B params | Comfortable with 32K context |
| BF16 | ~7B params | Full precision, limited context |

### Models That Fit

| Model | Quant | VRAM | Context | Fit |
|-------|-------|------|---------|-----|
| Nemotron-3-Nano-4B | Q8_0 | ~5 GB | 32K+ | Comfortable |
| Nemotron-3-Nano-4B | Q4_K_M | ~3 GB | 49K+ | Easy |
| Gemma-4-E4B | Q8_0 | ~8 GB | 32K | Good |
| Gemma-4-26B-A4B | UD-Q4_K_XL | ~17 GB | 16K | Tight — may need GPU layer offload |
| Gemma-4-31B | UD-Q4_K_XL | ~19 GB | — | Does not fit |

---

## Planner Guidance

- This is the **laptop GPU** — 16GB VRAM is the hard ceiling.
- Best for small models (4B-8B) at Q8_0 or medium models (14B) at Q4.
- 26B MoE models technically fit at Q4 but leave almost no room for KV cache — prefer running these on the desktop 5090.
- For CSI, this GPU is ideal for: nano-4b (all scanners), E4B (light/medium scanners).
- Consider offloading heavy scanners to the desktop 5090 endpoint when available.

---

## Sources

- [NotebookCheck: RTX 4090 Laptop GPU](https://www.notebookcheck.net/NVIDIA-GeForce-RTX-4090-Laptop-GPU-Benchmarks-and-Specs.675091.0.html)
- [TechSpot: RTX 4090 Laptop Review](https://www.techspot.com/review/2624-nvidia-geforce-rtx-4090-laptop-gpu/)
- [NVIDIA: RTX 40 Series Laptops](https://www.nvidia.com/en-us/geforce/laptops/40-series/)
