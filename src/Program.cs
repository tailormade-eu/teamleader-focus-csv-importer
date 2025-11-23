using System.Text.Json;
using System.Linq;

var argsList = args.ToList();
var dryRun = argsList.Contains("--dry-run");
if (dryRun) argsList.Remove("--dry-run");

var configPath = argsList.Count > 0 ? argsList[0] : "appsettings.json";
var inputPath = argsList.Count > 1 ? argsList[1] : "input.csv";

// If dry-run was requested, skip reading config/token and just parse the input file
if (dryRun)
{
    if (!File.Exists(inputPath))
    {
        Console.WriteLine($"Input file not found: {inputPath}");
        return 1;
    }

    var entriesDry = CsvParser.Read(inputPath);
    int i = 0;
    foreach (var e in entriesDry)
    {
        Console.WriteLine($"Entry #{++i}");
        Console.WriteLine($"  Company: {e.Company}");
        Console.WriteLine($"  Project: {e.Project}");
        Console.WriteLine($"  Group:   {e.Group}");
        Console.WriteLine($"  Task:    {e.Task}");
        Console.WriteLine($"  Start:   {e.Start:u}");
        Console.WriteLine($"  End:     {e.End:u}");
        Console.WriteLine($"  Billable:{(e.Billable.HasValue ? (e.Billable.Value ? " Yes" : " No") : " Unknown")}");
        Console.WriteLine($"  Notes:   {e.Notes}");
        Console.WriteLine();
    }

    Console.WriteLine($"Dry-run complete. Parsed {entriesDry.Count()} entries.");
    return 0;
}

if (!File.Exists(configPath))
{
    Console.WriteLine($"Config file not found: {configPath}");
    return 1;
}

var cfgJson = await File.ReadAllTextAsync(configPath);
var cfg = JsonSerializer.Deserialize<Config>(cfgJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

if (string.IsNullOrWhiteSpace(cfg.BaseUrl))
{
    Console.WriteLine("Please set 'BaseUrl' in appsettings.json");
    return 1;
}

// token can be provided via env var TEAMLEADER_TOKEN or appsettings
var token = Environment.GetEnvironmentVariable("TEAMLEADER_TOKEN") ?? cfg.ApiToken;
if (string.IsNullOrWhiteSpace(token))
{
    Console.WriteLine("Please set TEAMLEADER_TOKEN environment variable or 'ApiToken' in appsettings.json");
    return 1;
}

if (!File.Exists(inputPath))
{
    Console.WriteLine($"Input file not found: {inputPath}");
    return 1;
}

using var http = new HttpClient { BaseAddress = new Uri(cfg.BaseUrl) };
http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

var api = new TeamleaderApi(http);
var resolver = new Resolver(api);

var entries = CsvParser.Read(inputPath);
if (dryRun)
{
    int i = 0;
    foreach (var e in entries)
    {
        Console.WriteLine($"Entry #{++i}");
        Console.WriteLine($"  Company: {e.Company}");
        Console.WriteLine($"  Project: {e.Project}");
        Console.WriteLine($"  Group:   {e.Group}");
        Console.WriteLine($"  Task:    {e.Task}");
        Console.WriteLine($"  Start:   {e.Start:u}");
        Console.WriteLine($"  End:     {e.End:u}");
        Console.WriteLine($"  Billable:{(e.Billable.HasValue ? (e.Billable.Value ? " Yes" : " No") : " Unknown")}");
        Console.WriteLine($"  Notes:   {e.Notes}");
        Console.WriteLine();
    }
    Console.WriteLine($"Dry-run complete. Parsed {entries.Count()} entries.");
    return 0;
}

int processed = 0;
foreach (var e in entries)
{
    Console.WriteLine($"Processing: {e.Company} / {e.Project} / {e.Group} / {e.Task}");
    var companyId = await resolver.GetOrCreateCompanyAsync(e.Company);
    var projectId = await resolver.GetOrCreateProjectAsync(companyId, e.Project);
    var groupId = await resolver.GetOrCreateGroupAsync(projectId, e.Group);
    var taskId = await resolver.GetOrCreateTaskAsync(groupId, e.Task);

    var result = await api.AddTimeEntryAsync(taskId, e.Start, e.End, e.Notes);
    if (result)
    {
        Console.WriteLine("Time entry created.");
    }
    else
    {
        Console.WriteLine("Failed to create time entry.");
    }
    processed++;
}

Console.WriteLine($"Done. Processed {processed} entries.");
return 0;

record Config(string BaseUrl, string? ApiToken);
