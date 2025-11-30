using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.IO;

// Configure Serilog early for bootstrap logging
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/teamleader-.log", rollingInterval: RollingInterval.Day)
    .CreateBootstrapLogger();

try
{
    var rawArgs = args.ToList();

    // Detect flags anywhere in the args
    var dryRun = rawArgs.Contains("--dry-run");
    var authTest = rawArgs.Contains("--auth-test");
    var exchangeCodeFlag = rawArgs.Contains("--exchange-code");
    var listCompanies = rawArgs.Contains("--list-companies");
    var listProjects = rawArgs.Contains("--list-projects");
    var listProjectGroups = rawArgs.Contains("--list-projectgroups");
    var listTasks = rawArgs.Contains("--list-tasks");
    var listTimeTracking = rawArgs.Contains("--list-timetracking");

    // Build a list of positional args (exclude known flags and their parameters)
    var positional = new List<string>();
    for (int i = 0; i < rawArgs.Count; i++)
    {
        var a = rawArgs[i];
        if (a == "--dry-run" || a == "--auth-test" || a == "--list-companies" || a == "--list-projects" || a == "--list-projectgroups" || a == "--list-tasks" || a == "--list-timetracking") continue;
        if (a == "--exchange-code")
        {
            i++; // skip the flag and its value
            continue;
        }
        positional.Add(a);
    }

    // set to examples/ManicTime_Tags_2025-11-23.csv for easier testing
    var inputPath = "C:\\Users\\JaRa\\OneDrive\\OneSyncFiles\\ObsidianVault\\Tailormade\\Projects\\teamleader-focus-csv-importer\\src\\examples\\ManicTime_Tags_2025-11-29.csv";

    // Handle dry-run (doesn't need config/token)
    if (dryRun)
    {
        return CommandHandler.RunDryRun(inputPath);
    }

    // Build host with standard .NET configuration
    var host = Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((context, config) =>
        {
            // CreateDefaultBuilder already loads appsettings.json, appsettings.{env}.json, env vars, command line
            // We just ensure we're using the right base path
            config.SetBasePath(Directory.GetCurrentDirectory());
        })
        .UseSerilog((context, services, loggerConfig) =>
        {
            loggerConfig
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File("logs/teamleader-.log", rollingInterval: RollingInterval.Day);
        })
        .ConfigureServices((context, services) =>
        {
            // Bind configuration to strongly-typed options
            services.Configure<AppSettings>(context.Configuration);
            services.AddSingleton(context.Configuration.Get<AppSettings>() ?? new AppSettings());
        })
        .Build();

    var config = host.Services.GetRequiredService<AppSettings>();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();

    if (string.IsNullOrWhiteSpace(config.BaseUrl))
    {
        logger.LogError("Please set 'BaseUrl' in appsettings.json");
        return 1;
    }

    var cfgDir = Directory.GetCurrentDirectory();

    // Handle special commands
    if (authTest)
    {
        return await CommandHandler.RunAuthTestAsync(config, cfgDir, logger);
    }

    if (exchangeCodeFlag)
    {
        var idx = rawArgs.IndexOf("--exchange-code");
        if (idx < 0 || rawArgs.Count <= idx + 1)
        {
            logger.LogError("Usage: --exchange-code \"<redirect_url>\"");
            return 1;
        }
        return await CommandHandler.RunExchangeCodeAsync(config, cfgDir, rawArgs[idx + 1], logger);
    }

    if (listCompanies)
    {
        return await CommandHandler.RunListCompaniesAsync(config, cfgDir, logger);
    }

    if (listProjects)
    {
        return await CommandHandler.RunListProjectsAsync(config, cfgDir, logger);
    }

    if (listProjectGroups)
    {
        return await CommandHandler.RunListProjectGroupsAsync(config, cfgDir, logger);
    }
    if (listTasks)
    {
        return await CommandHandler.RunListTasksAsync(config, cfgDir, logger);
    }

    if (listTimeTracking)
    {
        return await CommandHandler.RunListTimeTrackingAsync(config, cfgDir, logger);
    }

    // Default: run import
    return await CommandHandler.RunImportAsync(config, cfgDir, inputPath, logger);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// Placeholder class for top-level statements logger
public partial class Program { }
