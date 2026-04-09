using Agent.SDK.Console;
using Agent.SDK.Logging;
using CrimeSceneInvestigator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;

var headless = args.Contains("--headless");

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .Build();

AgentLogging.Configure(configuration, suppressConsole: !headless);
AgentConsole.Configure(headless);
using var loggerFactory = AgentLogging.CreateLoggerFactory();

try
{
    var agent = new AgentInCommand(loggerFactory.CreateLogger<AgentInCommand>(), configuration);

    return await AgentCommandSetup
        .CreateRootCommand(agent.RunAsync)
        .Parse(args)
        .InvokeAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Agent execution failed with an unhandled exception");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
