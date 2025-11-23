using System.Text.Json;

var configPath = args.Length > 0 ? args[0] : "appsettings.json";
var inputPath = args.Length > 1 ? args[1] : "input.csv";

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
