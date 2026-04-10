using System.Text.Json;
using Agent.SDK.Configuration;

namespace Agent.SDK.Tests;

public sealed class ModelRegistryTests : IDisposable
{
    private readonly string _root;

    public ModelRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"registry-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ── Load ──

    [Fact]
    public void Load_WithValidFiles_ReturnsPopulatedRegistry()
    {
        WriteModelRegistry("""
        {
          "$schema": "model-registry-v1",
          "models": {
            "test-model-4b": {
              "params_b": 4.0,
              "architecture": "dense",
              "active_params_b": 4.0,
              "context_k": 128,
              "vram_q4_gb": 3,
              "vram_q8_gb": 5,
              "vram_bf16_gb": 8,
              "tool_calling": "native",
              "thinking": true,
              "inference": { "temperature": 0.6, "top_p": 0.95 },
              "scanners": {
                "markdown": "good",
                "structure": "good",
                "rules": "marginal",
                "quality": "marginal",
                "journal": "good",
                "done": "good"
              },
              "notes": "Test model",
              "profile": "test-model-4b.md"
            }
          }
        }
        """);
        WriteGpuRegistry("""
        {
          "$schema": "gpu-registry-v1",
          "gpus": {
            "test-gpu": {
              "vram_gb": 16,
              "bandwidth_gb_s": 640,
              "compute_capability": "8.9",
              "cuda_cores": 9728,
              "fits": {
                "test-model-4b": { "q8": true, "q4": true }
              },
              "profile": "test-gpu.md"
            }
          }
        }
        """);

        var registry = ModelRegistry.Load(_root);

        Assert.Single(registry.Models);
        Assert.Single(registry.Gpus);
    }

    [Fact]
    public void Load_ParsesModelEntryCorrectly()
    {
        WriteModelRegistry("""
        {
          "models": {
            "gemma-31b": {
              "params_b": 30.7,
              "architecture": "dense",
              "active_params_b": 30.7,
              "context_k": 256,
              "vram_q4_gb": 20,
              "vram_q8_gb": 33,
              "vram_bf16_gb": 62,
              "tool_calling": "gemma4_native",
              "thinking": true,
              "inference": { "temperature": 1.0, "top_p": 0.95, "top_k": 64 },
              "scanners": {
                "markdown": "excellent",
                "structure": "excellent",
                "rules": "excellent",
                "quality": "excellent",
                "journal": "excellent",
                "done": "excellent"
              },
              "notes": "Strongest quality.",
              "profile": "google-gemma-4-31b-it.md"
            }
          }
        }
        """);

        var registry = ModelRegistry.Load(_root);
        var model = registry.Models["gemma-31b"];

        Assert.Equal(30.7, model.ParamsB);
        Assert.Equal("dense", model.Architecture);
        Assert.Equal(30.7, model.ActiveParamsB);
        Assert.Equal(256, model.ContextK);
        Assert.Equal(20, model.VramQ4Gb);
        Assert.Equal(33, model.VramQ8Gb);
        Assert.Equal(62, model.VramBf16Gb);
        Assert.Equal("gemma4_native", model.ToolCalling);
        Assert.True(model.Thinking);
        Assert.NotNull(model.Inference);
        Assert.Equal(1.0, model.Inference.Temperature);
        Assert.Equal(0.95, model.Inference.TopP);
        Assert.Equal(64, model.Inference.TopK);
        Assert.NotNull(model.Scanners);
        Assert.Equal(ScannerRating.Excellent, model.Scanners.Markdown);
        Assert.Equal(ScannerRating.Excellent, model.Scanners.Rules);
        Assert.Equal("Strongest quality.", model.Notes);
        Assert.Equal("google-gemma-4-31b-it.md", model.Profile);
    }

    [Fact]
    public void Load_ParsesScannerRatings()
    {
        WriteModelRegistry("""
        {
          "models": {
            "test": {
              "params_b": 4.0,
              "architecture": "dense",
              "active_params_b": 4.0,
              "context_k": 128,
              "vram_q4_gb": 3,
              "vram_q8_gb": 5,
              "vram_bf16_gb": 8,
              "tool_calling": "native",
              "thinking": false,
              "scanners": {
                "markdown": "good",
                "structure": "excellent",
                "rules": "marginal",
                "quality": "marginal",
                "journal": "good",
                "done": "excellent"
              }
            }
          }
        }
        """);

        var scanners = ModelRegistry.Load(_root).Models["test"].Scanners!;

        Assert.Equal(ScannerRating.Good, scanners.Markdown);
        Assert.Equal(ScannerRating.Excellent, scanners.Structure);
        Assert.Equal(ScannerRating.Marginal, scanners.Rules);
        Assert.Equal(ScannerRating.Marginal, scanners.Quality);
        Assert.Equal(ScannerRating.Good, scanners.Journal);
        Assert.Equal(ScannerRating.Excellent, scanners.Done);
    }

    [Fact]
    public void Load_MissingFiles_ReturnsEmptyRegistry()
    {
        var registry = ModelRegistry.Load(_root);

        Assert.Empty(registry.Models);
        Assert.Empty(registry.Gpus);
    }

    [Fact]
    public void Load_MalformedJson_ReturnsEmptyRegistry()
    {
        WriteModelRegistry("{ not valid json at all }}}");
        WriteGpuRegistry("{ also broken }}}");

        var registry = ModelRegistry.Load(_root);

        Assert.Empty(registry.Models);
        Assert.Empty(registry.Gpus);
    }

    [Fact]
    public void Load_EmptyModelsObject_ReturnsEmptyModels()
    {
        WriteModelRegistry("""{ "models": {} }""");

        var registry = ModelRegistry.Load(_root);

        Assert.Empty(registry.Models);
    }

    [Fact]
    public void Load_MultipleModels_ReturnsAll()
    {
        WriteModelRegistry("""
        {
          "models": {
            "model-a": { "params_b": 4.0, "architecture": "dense", "active_params_b": 4.0, "context_k": 128, "vram_q4_gb": 3, "vram_q8_gb": 5, "vram_bf16_gb": 8, "tool_calling": "native", "thinking": false },
            "model-b": { "params_b": 30.0, "architecture": "moe", "active_params_b": 3.8, "context_k": 256, "vram_q4_gb": 17, "vram_q8_gb": 27, "vram_bf16_gb": 51, "tool_calling": "native", "thinking": true }
          }
        }
        """);

        var registry = ModelRegistry.Load(_root);

        Assert.Equal(2, registry.Models.Count);
        Assert.Equal(4.0, registry.Models["model-a"].ParamsB);
        Assert.Equal(30.0, registry.Models["model-b"].ParamsB);
        Assert.Equal("moe", registry.Models["model-b"].Architecture);
    }

    // ── GpuEntry.CanFit ──

    [Fact]
    public void CanFit_TrueValue_ReturnsTrue()
    {
        WriteGpuRegistry("""
        {
          "gpus": {
            "gpu": {
              "vram_gb": 32,
              "bandwidth_gb_s": 1792,
              "compute_capability": "10.0",
              "cuda_cores": 21760,
              "fits": {
                "model-a": { "q4": true, "q8": true }
              }
            }
          }
        }
        """);

        var gpu = ModelRegistry.Load(_root).Gpus["gpu"];

        Assert.True(gpu.CanFit("model-a", "q4"));
        Assert.True(gpu.CanFit("model-a", "q8"));
    }

    [Fact]
    public void CanFit_FalseValue_ReturnsFalse()
    {
        WriteGpuRegistry("""
        {
          "gpus": {
            "gpu": {
              "vram_gb": 16,
              "bandwidth_gb_s": 640,
              "compute_capability": "8.9",
              "cuda_cores": 9728,
              "fits": {
                "big-model": { "q4": false }
              }
            }
          }
        }
        """);

        var gpu = ModelRegistry.Load(_root).Gpus["gpu"];

        Assert.False(gpu.CanFit("big-model", "q4"));
    }

    [Fact]
    public void CanFit_TightValue_ReturnsTrue()
    {
        WriteGpuRegistry("""
        {
          "gpus": {
            "gpu": {
              "vram_gb": 16,
              "bandwidth_gb_s": 640,
              "compute_capability": "8.9",
              "cuda_cores": 9728,
              "fits": {
                "medium-model": { "q4": "tight" }
              }
            }
          }
        }
        """);

        var gpu = ModelRegistry.Load(_root).Gpus["gpu"];

        Assert.True(gpu.CanFit("medium-model", "q4"));
    }

    [Fact]
    public void CanFit_UnknownModel_ReturnsFalse()
    {
        WriteGpuRegistry("""
        {
          "gpus": {
            "gpu": {
              "vram_gb": 16,
              "bandwidth_gb_s": 640,
              "compute_capability": "8.9",
              "cuda_cores": 9728,
              "fits": {}
            }
          }
        }
        """);

        var gpu = ModelRegistry.Load(_root).Gpus["gpu"];

        Assert.False(gpu.CanFit("unknown-model", "q4"));
    }

    [Fact]
    public void CanFit_UnknownQuantization_ReturnsFalse()
    {
        WriteGpuRegistry("""
        {
          "gpus": {
            "gpu": {
              "vram_gb": 16,
              "bandwidth_gb_s": 640,
              "compute_capability": "8.9",
              "cuda_cores": 9728,
              "fits": {
                "model": { "q4": true }
              }
            }
          }
        }
        """);

        var gpu = ModelRegistry.Load(_root).Gpus["gpu"];

        Assert.False(gpu.CanFit("model", "bf16"));
    }

    // ── Integration: load real registry files ──

    [Fact]
    public void Load_RealRegistryFiles_ParsesSuccessfully()
    {
        // Walk up to find the repo root
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            return; // Skip if not in a git repo
        }

        var registry = ModelRegistry.Load(dir.FullName);

        Assert.True(registry.Models.Count >= 4, $"Expected at least 4 models, got {registry.Models.Count}");
        Assert.True(registry.Gpus.Count >= 2, $"Expected at least 2 GPUs, got {registry.Gpus.Count}");

        // Spot check a known model
        Assert.True(registry.Models.ContainsKey("google-gemma-4-31b-it"));
        var gemma31 = registry.Models["google-gemma-4-31b-it"];
        Assert.Equal(30.7, gemma31.ParamsB);
        Assert.Equal(ScannerRating.Excellent, gemma31.Scanners!.Quality);

        // Spot check a known GPU
        Assert.True(registry.Gpus.ContainsKey("rtx-5090-msi-suprim"));
        var rtx5090 = registry.Gpus["rtx-5090-msi-suprim"];
        Assert.Equal(32, rtx5090.VramGb);
        Assert.True(rtx5090.CanFit("google-gemma-4-31b-it", "q4"));
    }

    // ── Helpers ──

    private void WriteModelRegistry(string json)
    {
        var dir = Path.Combine(_root, "context", "models");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "_registry.json"), json);
    }

    private void WriteGpuRegistry(string json)
    {
        var dir = Path.Combine(_root, "context", "gpu");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "_registry.json"), json);
    }
}
