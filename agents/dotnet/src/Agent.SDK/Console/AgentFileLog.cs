using System.Text;

namespace Agent.SDK.Console;

/// <summary>
/// Manages a buffered <see cref="StreamWriter"/> to a single log file with
/// <see cref="SemaphoreSlim"/> synchronization and optional rotation.
/// Construct one instance per log file; the static facades
/// (<see cref="AgentDebugLog"/>, <see cref="AgentErrorLog"/>) own their own instance.
/// </summary>
public sealed class AgentFileLog(
    string fileName,
    string headerLabel,
    bool autoFlush,
    long maxBytesBeforeRotation = 0) : IAsyncDisposable
{
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Opens (or re-opens) the log file in <paramref name="outputDirectory"/>.</summary>
    public async Task InitializeAsync(string outputDirectory)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _writer?.Dispose();

            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var logPath = Path.Combine(outputDirectory, fileName);
            RotateIfNeeded(logPath);

            _writer = new StreamWriter(logPath, append: false, Encoding.UTF8) { AutoFlush = autoFlush };
            await _writer.WriteLineAsync($"=== {headerLabel} — {DateTime.Now:yyyy-MM-dd HH:mm:ss.ffffff zzz} ===").ConfigureAwait(false);
            await _writer.WriteLineAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Writes a line of text followed by a newline.</summary>
    public async Task WriteLineAsync(string text)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_writer is not null)
            {
                await _writer.WriteLineAsync(text).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Writes text without appending a newline.</summary>
    public async Task WriteAsync(string text)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_writer is not null)
            {
                await _writer.WriteAsync(text).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Flushes the underlying stream to disk.</summary>
    public async Task FlushAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_writer is not null)
            {
                await _writer.FlushAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_writer is not null)
            {
                await _writer.DisposeAsync().ConfigureAwait(false);
                _writer = null;
            }
        }
        finally
        {
            _lock.Release();
        }

        GC.SuppressFinalize(this);
    }

    private void RotateIfNeeded(string logPath)
    {
        if (maxBytesBeforeRotation <= 0) { return; }

        try
        {
            if (File.Exists(logPath) && new FileInfo(logPath).Length > maxBytesBeforeRotation)
            {
                var prevPath = Path.ChangeExtension(logPath, ".prev.log");
                File.Copy(logPath, prevPath, overwrite: true);
            }
        }
        catch { /* best effort */ }
    }

    /// <summary>Truncates <paramref name="s"/> to <paramref name="max"/> characters with an ellipsis.</summary>
    public static string? Truncate(string? s, int max)
        => s is null ? null : s.Length <= max ? s : s[..max] + "...";
}
