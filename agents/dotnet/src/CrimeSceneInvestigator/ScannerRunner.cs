using System.Diagnostics;
using System.Text;
using Agent.SDK.Configuration;
using Agent.SDK.Console;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CrimeSceneInvestigator;

/// <summary>
/// Runs a single scanner with timeout, retry, and fallback output handling.
/// </summary>
internal sealed record ScannerRunner(ILogger Logger)
{
    /// <summary>
    /// Runs a single scanner with timeout and one retry. If the model produces text
    /// output but doesn't call WriteOutput, substantive markdown is saved as a fallback.
    /// </summary>
    public async Task RunAsync(
        AgentContext context,
        string scannerName,
        string systemPrompt,
        string userMessage,
        IList<AITool> tools,
        CancellationToken ct,
        string? expectedOutputPath = null,
        AgentModelOptions? modelOverride = null,
        TimeSpan? timeout = null)
    {
        var output = AgentConsole.Output;

        if (modelOverride?.IsSkipped == true)
        {
            var reason = "No loaded model meets capability requirements for this scanner";
            await output.ScannerSkippedAsync(scannerName, reason);
            Logger.LogWarning("Skipping scanner {ScannerName}: {Reason}", scannerName, reason);
            await AgentErrorLog.LogAsync(scannerName, $"Skipped: {reason}");
            return;
        }

        var activeModel = modelOverride ?? context.ModelOptions;
        var sw = Stopwatch.StartNew();

        await output.ScannerStartedAsync(scannerName, activeModel.Model);
        Logger.LogInformation("Starting scanner: {ScannerName} with model {Model}", scannerName, activeModel.Model);

        var agent = context.GetAgentClient(modelOverride);

        var chatOptions = new ChatOptions
        {
            Tools = tools.WithProgress(output),
        };

        if (activeModel.Temperature.HasValue)
        {
            chatOptions.Temperature = activeModel.Temperature;
        }

        if (activeModel.TopP.HasValue)
        {
            chatOptions.TopP = activeModel.TopP;
        }

        if (activeModel.MaxOutputTokens.HasValue)
        {
            chatOptions.MaxOutputTokens = activeModel.MaxOutputTokens;
        }

        var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(3);
        const int maxAttempts = 2;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, attempt > 1
                    ? $"{userMessage}\n\nPrevious attempt failed to produce output. Focus on calling WriteOutput with valid markdown content."
                    : userMessage),
            };

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(effectiveTimeout);
            var scannerCt = timeoutCts.Token;

            try
            {
                var responseBuf = new StringBuilder();
                await foreach (var update in agent.GetStreamingResponseAsync(messages, chatOptions, scannerCt))
                {
                    if (update.Text is { Length: > 0 } text)
                    {
                        responseBuf.Append(text);
                    }
                }

                var responseText = responseBuf.ToString();
                if (responseText.Length > 0)
                {
                    var preview = responseText.Length > 200 ? responseText[..200] + "..." : responseText;
                    Logger.LogInformation("Scanner {ScannerName} response: {Preview}", scannerName, preview);

                    // Fallback: if the model produced markdown as text but didn't call WriteOutput.
                    if (expectedOutputPath is not null && !File.Exists(expectedOutputPath))
                    {
                        var trimmed = responseText.TrimStart();
                        if (AgentInCommand.IsSubstantiveMarkdown(trimmed))
                        {
                            var dir = Path.GetDirectoryName(expectedOutputPath);
                            if (dir is not null && !Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }

                            File.WriteAllText(expectedOutputPath, trimmed);
                            Logger.LogInformation("Fallback write: saved {ScannerName} output to {Path}",
                                scannerName, expectedOutputPath);
                        }
                        else
                        {
                            var rejected = trimmed.Length > 100 ? trimmed[..100] + "..." : trimmed;
                            Logger.LogWarning("Scanner {ScannerName} fallback rejected — not markdown: {Preview}",
                                scannerName, rejected);
                            await AgentErrorLog.LogAsync(scannerName, $"Fallback rejected — model produced chatbot filler: {rejected}");
                        }
                    }
                }
                else
                {
                    Logger.LogWarning("Scanner {ScannerName} produced no text response", scannerName);
                }

                // Retry if output file still missing and we have attempts left
                if (expectedOutputPath is not null && !File.Exists(expectedOutputPath) && attempt < maxAttempts)
                {
                    Logger.LogWarning("Scanner {ScannerName} produced no output — retrying (attempt {Attempt}/{Max})",
                        scannerName, attempt, maxAttempts);
                    continue;
                }

                if (expectedOutputPath is not null && !File.Exists(expectedOutputPath))
                {
                    Logger.LogError("Scanner {ScannerName} produced no output file at {Path}",
                        scannerName, expectedOutputPath);
                    await AgentErrorLog.LogAsync(scannerName, $"No output file produced at {expectedOutputPath}");
                }

                break;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Logger.LogWarning("Scanner {ScannerName} timed out after {Timeout}", scannerName, effectiveTimeout);
                await AgentErrorLog.LogAsync(scannerName, $"Timed out after {effectiveTimeout}");
                if (attempt < maxAttempts)
                {
                    Logger.LogInformation("Retrying scanner {ScannerName} (attempt {Attempt}/{Max})",
                        scannerName, attempt + 1, maxAttempts);
                    continue;
                }

                sw.Stop();
                await output.ScannerCompletedAsync(scannerName, sw.Elapsed, success: false);
                return;
            }
            catch (Exception ex)
            {
                sw.Stop();
                await output.ScannerCompletedAsync(scannerName, sw.Elapsed, success: false);
                Logger.LogError(ex, "Scanner {ScannerName} failed: {Message}", scannerName, ex.Message);
                await AgentErrorLog.LogAsync(scannerName, $"Failed: {ex.Message}", ex);
                return;
            }
        }

        sw.Stop();
        await output.ScannerCompletedAsync(scannerName, sw.Elapsed, success: true);
        Logger.LogInformation("Completed scanner: {ScannerName} in {Elapsed:F1}s", scannerName, sw.Elapsed.TotalSeconds);
    }
}
