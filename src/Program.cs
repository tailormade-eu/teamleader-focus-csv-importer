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
    // Try client credentials from configuration
    if (cfg.Authentication is not null && !string.IsNullOrWhiteSpace(cfg.Authentication.ClientId) && !string.IsNullOrWhiteSpace(cfg.Authentication.ClientSecret))
    {
        Console.WriteLine("Obtaining OAuth token via client credentials...");
        token = await OAuthClient.GetTokenAsync(cfg.BaseUrl, cfg.Authentication.ClientId, cfg.Authentication.ClientSecret);
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.WriteLine("Failed to acquire OAuth token using client credentials.");
            return 1;
        }
    }
    else
    {
        Console.WriteLine("Please set TEAMLEADER_TOKEN environment variable, 'ApiToken' in appsettings.json, or provide client credentials in the Authentication section.");
        return 1;
    }
}

// Support manual authorization-code exchange: --exchange-code "<redirect_url>"
if (argsList.Contains("--exchange-code"))
{
    var idx = argsList.IndexOf("--exchange-code");
    if (idx < 0 || argsList.Count <= idx + 1)
    {
        Console.WriteLine("Usage: --exchange-code \"<redirect_url>\"");
        return 1;
    }
    var redirectUrl = argsList[idx + 1];
    // parse code and redirect_uri from URL
    try
    {
        var uri = new Uri(redirectUrl);
        var qs = uri.Query.TrimStart('?');
        var dict = qs.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split(new[] { '=' }, 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(k => Uri.UnescapeDataString(k[0]), v => Uri.UnescapeDataString(v[1]));

        if (!dict.TryGetValue("code", out var code))
        {
            Console.WriteLine("No 'code' parameter found in redirect URL.");
            return 1;
        }

        var redirectUri = uri.GetLeftPart(UriPartial.Path);

        if (cfg.Authentication is null || string.IsNullOrWhiteSpace(cfg.Authentication.ClientId) || string.IsNullOrWhiteSpace(cfg.Authentication.ClientSecret))
        {
            Console.WriteLine("ClientId/ClientSecret not found in config. Add them under Authentication or set environment variables.");
            return 1;
        }

        Console.WriteLine("Exchanging authorization code for access token...");
        var tokenObj = await OAuthClient.ExchangeAuthorizationCodeAsync(cfg.BaseUrl, cfg.Authentication.ClientId, cfg.Authentication.ClientSecret, code, redirectUri);
        if (tokenObj is null)
        {
            Console.WriteLine("Token exchange failed.");
            return 1;
        }

        // store token next to config file
        var cfgDir = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();
        var tokenPath = Path.Combine(cfgDir, "auth_token.json");
        OAuthClient.SaveAuthTokenToFile(tokenObj, tokenPath);
        Console.WriteLine($"Token saved to: {tokenPath}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to parse or exchange code: {ex.Message}");
        return 1;
    }
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

record Config(string BaseUrl, string? ApiToken, Authentication? Authentication);

record Authentication(string ClientId, string ClientSecret);
