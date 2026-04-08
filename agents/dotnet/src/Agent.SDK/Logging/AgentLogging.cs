using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace Agent.SDK.Logging;

/// <summary>
/// Serilog bootstrap helpers for console agents.
/// Configures structured logging with the standard agent output template.
/// </summary>
public static class AgentLogging
{
    private const string OutputTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Configures <see cref="Log.Logger"/> with structured console output.
    /// Call once at startup, before any logging.
    /// When <paramref name="configuration"/> is provided, Serilog reads overrides
    /// (e.g. minimum level per namespace) from the <c>Serilog</c> section.
    /// </summary>
    public static void Configure(
        IConfiguration? configuration = null,
        LogEventLevel minimumLevel = LogEventLevel.Information)
    {
        var builder = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: OutputTemplate, theme: AnsiConsoleTheme.Code);

        if (configuration is not null)
        {
            builder.ReadFrom.Configuration(configuration);
        }

        Log.Logger = builder.CreateLogger();
    }

    /// <summary>
    /// Creates an <see cref="ILoggerFactory"/> backed by the current Serilog <see cref="Log.Logger"/>.
    /// The caller owns the returned factory and should dispose it.
    /// </summary>
    public static ILoggerFactory CreateLoggerFactory()
    {
        return LoggerFactory.Create(builder => builder.AddSerilog());
    }
}
