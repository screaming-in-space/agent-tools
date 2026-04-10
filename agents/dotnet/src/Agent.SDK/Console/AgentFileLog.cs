using System.Text;

namespace Agent.SDK.Console;

/// <summary>
/// Base class for agent log files. Manages a buffered <see cref="StreamWriter"/>
/// with <see cref="SemaphoreSlim"/> synchronization and optional rotation.
/// Subclasses provide the filename, header, and specific write methods.
/// </summary>
public abstract class AgentFileLog : IAsyncDisposable
{
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _lock = new(1, 1);

    protected abstract string FileName { get; }
    protected abstract string HeaderLabel { get; }
    protected abstract bool AutoFlush { get; }
    protected virtual long MaxBytesBeforeRotation => 0;

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

            var logPath = Path.Combine(outputDirectory, FileName);
            RotateIfNeeded(logPath);

            _writer = new StreamWriter(logPath, append: false, Encoding.UTF8) { AutoFlush = AutoFlush };
            await _writer.WriteLineAsync($"=== {HeaderLabel} — {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===").ConfigureAwait(false);
            await _writer.WriteLineAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    protected async Task WriteLineAsync(string text)
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

    protected void WriteDirect(string text)
    {
        try { _writer?.Write(text); }
        catch { /* best effort */ }
    }

    protected void FlushDirect()
    {
        try { _writer?.Flush(); }
        catch { /* best effort */ }
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
        if (MaxBytesBeforeRotation <= 0) { return; }

        try
        {
            if (File.Exists(logPath) && new FileInfo(logPath).Length > MaxBytesBeforeRotation)
            {
                var prevPath = Path.ChangeExtension(logPath, ".prev.log");
                File.Copy(logPath, prevPath, overwrite: true);
            }
        }
        catch { /* best effort */ }
    }

    public static string? Truncate(string? s, int max)
        => s is null ? null : s.Length <= max ? s : s[..max] + "...";
}
