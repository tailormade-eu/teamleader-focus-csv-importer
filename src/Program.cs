using System.Text.Json;
using System.Linq;

var rawArgs = args.ToList();
// detect flags anywhere in the args
var dryRun = rawArgs.Contains("--dry-run");
var authTest = rawArgs.Contains("--auth-test");
var exchangeCodeFlag = rawArgs.Contains("--exchange-code");

// Build a list of positional args (exclude known flags and their parameters)
var positional = new List<string>();
for (int i = 0; i < rawArgs.Count; i++)
{
    var a = rawArgs[i];
    if (a == "--dry-run" || a == "--auth-test") continue;
    if (a == "--exchange-code")
    {
        // skip the flag and the next value (the redirect url) if present
        i++;
        continue;
    }
    positional.Add(a);
}

var configPath = positional.Count > 0 ? positional[0] : "appsettings.json";
var inputPath = positional.Count > 1 ? positional[1] : "input.csv";

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

// Load configuration by merging base config and optional environment-specific config.
var baseConfigPath = Path.GetFullPath(configPath);
if (!File.Exists(baseConfigPath))
{
    Console.WriteLine($"Config file not found: {configPath}");
    return 1;
}

var baseJson = await File.ReadAllTextAsync(baseConfigPath);
var mergedDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(baseJson) ?? new Dictionary<string, JsonElement>();

// Determine environment (prefer DOTNET_ENVIRONMENT, then ASPNETCORE_ENVIRONMENT, default to Development)
var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
var envConfigPath = Path.Combine(Path.GetDirectoryName(baseConfigPath) ?? Directory.GetCurrentDirectory(), $"appsettings.{env}.json");
if (File.Exists(envConfigPath))
{
    var envJson = await File.ReadAllTextAsync(envConfigPath);
    var envDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(envJson) ?? new Dictionary<string, JsonElement>();
    // overlay env values (env overrides base)
    foreach (var kv in envDict)
    {
        mergedDict[kv.Key] = kv.Value;
    }
}

// Also allow overriding from environment variables (simple mapping: TEAMLEADER_TOKEN -> ApiToken)
var envToken = Environment.GetEnvironmentVariable("TEAMLEADER_TOKEN");
if (!string.IsNullOrWhiteSpace(envToken))
{
    mergedDict["ApiToken"] = JsonDocument.Parse(JsonSerializer.Serialize(envToken)).RootElement;
}

// Re-serialize merged dictionary to JSON and bind to Config
var mergedJson = JsonSerializer.Serialize(mergedDict);
var cfg = JsonSerializer.Deserialize<Config>(mergedJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

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
if (exchangeCodeFlag)
{
    // find the flag in the original raw args to extract the provided url
    var idx = rawArgs.FindIndex(a => a == "--exchange-code");
    if (idx < 0 || rawArgs.Count <= idx + 1)
    {
        Console.WriteLine("Usage: --exchange-code \"<redirect_url>\"");
        return 1;
    }
    var redirectUrl = rawArgs[idx + 1];
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

// Interactive auth-test flow: prints authorize URL, prompts for redirect URL, exchanges code
if (authTest)
{
    if (cfg.Authentication is null || string.IsNullOrWhiteSpace(cfg.Authentication.ClientId) || string.IsNullOrWhiteSpace(cfg.Authentication.ClientSecret))
    {
        Console.WriteLine("ClientId/ClientSecret not found in config. Add them under Authentication or set environment variables.");
        return 1;
    }

    var clientId = cfg.Authentication.ClientId;
    var state = "st-" + Guid.NewGuid().ToString("N").Substring(0, 8);
    var defaultRedirect = "http://localhost:5000/callback";
    Console.WriteLine("Interactive OAuth Authorization Code flow (manual steps):");
    Console.WriteLine($"1) Open the following URL in your browser and authorize the app:");
    var authUrl = $"https://focus.teamleader.eu/oauth2/authorize?client_id={Uri.EscapeDataString(clientId)}&response_type=code&redirect_uri={Uri.EscapeDataString(defaultRedirect)}&state={Uri.EscapeDataString(state)}";
    Console.WriteLine(authUrl);
    Console.WriteLine();
    Console.WriteLine("2) After consenting you will be redirected to your redirect URI. Copy the full redirect URL (including ?code=...) and paste it here.");
    Console.Write("Redirect URL> ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input))
    {
        Console.WriteLine("No redirect URL provided. Aborting.");
        return 1;
    }

    try
    {
        var uri = new Uri(input.Trim());
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
        Console.WriteLine("Exchanging authorization code for access token...");
        var tokenObj = await OAuthClient.ExchangeAuthorizationCodeAsync(cfg.BaseUrl, cfg.Authentication.ClientId, cfg.Authentication.ClientSecret, code, redirectUri);
        if (tokenObj is null)
        {
            Console.WriteLine("Token exchange failed.");
            return 1;
        }

        var cfgDir = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? Directory.GetCurrentDirectory();
        var tokenPath = Path.Combine(cfgDir, "auth_token.json");
        OAuthClient.SaveAuthTokenToFile(tokenObj, tokenPath);
        Console.WriteLine($"Token saved to: {tokenPath}");
        Console.WriteLine($"Expires in: {tokenObj.ExpiresIn} seconds (obtained at {tokenObj.ObtainedAt:o})");
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
