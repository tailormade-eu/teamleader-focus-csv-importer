using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

public static class CommandHandler
{
    /// <summary>
    /// Handles the --list-projectgroups command.
    /// </summary>
    public static async Task<int> RunListProjectGroupsAsync(AppSettings cfg, string configDir, ILogger logger)
    {
        var token = await TokenManager.AcquireTokenAsync(cfg, configDir, logger);
        if (string.IsNullOrWhiteSpace(token))
        {
            return 1;
        }

        using var http = new HttpClient { BaseAddress = new Uri(cfg.BaseUrl) };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var api = new TeamleaderApi(http);

        logger.LogInformation("Fetching project groups from Teamleader...");

        try
        {
            var groups = await api.ListProjectGroupsAsync();
            if (groups.Count == 0)
            {
                logger.LogInformation("No project groups found.");
                return 0;
            }

            logger.LogInformation("Found {Count} project groups:", groups.Count);
            Console.WriteLine();
            Console.WriteLine("ID                                     Name");
            Console.WriteLine(new string('-', 80));
            foreach (var group in groups)
            {
                Console.WriteLine($"{group.Id,-38} {group.Name}");
            }
            return 0;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to fetch project groups from API");
            return 1;
        }
    }

    // ...existing code...
    /// <summary>
    /// Handles the --dry-run command.
    /// </summary>
    public static int RunDryRun(string inputPath)
    {
        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"Input file not found: {inputPath}");
            return 1;
        }

        var entries = CsvParser.Read(inputPath);
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

    /// <summary>
    /// Handles the --auth-test command (interactive OAuth flow).
    /// </summary>
    public static async Task<int> RunAuthTestAsync(AppSettings cfg, string configDir, ILogger logger)
    {
        if (cfg.Authentication is null
            || string.IsNullOrWhiteSpace(cfg.Authentication.ClientId)
            || string.IsNullOrWhiteSpace(cfg.Authentication.ClientSecret))
        {
            logger.LogError("ClientId/ClientSecret not found in config. Add them under Authentication.");
            return 1;
        }

        var clientId = cfg.Authentication.ClientId;
        var state = "st-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var redirectUri = cfg.Authentication.RedirectUri ?? "https://tailormade.eu/callback";
        var authorizeUrl = cfg.Authentication.AuthorizeUrl ?? "https://focus.teamleader.eu/oauth2/authorize";

        // Request scopes for the resources we need access to
        var scopes = "companies events products projects todos";

        logger.LogInformation("Interactive OAuth Authorization Code flow (manual steps):");
        Console.WriteLine("1) Open the following URL in your browser and authorize the app:");
        var authUrl = $"{authorizeUrl}?client_id={Uri.EscapeDataString(clientId)}&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(state)}&scope={Uri.EscapeDataString(scopes)}";
        Console.WriteLine(authUrl);
        Console.WriteLine();
        Console.WriteLine("2) After consenting you will be redirected to your redirect URI. Copy the full redirect URL (including ?code=...) and paste it here.");
        Console.Write("Redirect URL> ");

        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            logger.LogError("No redirect URL provided. Aborting.");
            return 1;
        }

        return await ExchangeCodeAndSaveAsync(cfg, configDir, input.Trim(), logger);
    }

    /// <summary>
    /// Handles the --exchange-code command.
    /// </summary>
    public static async Task<int> RunExchangeCodeAsync(AppSettings cfg, string configDir, string redirectUrl, ILogger logger)
    {
        if (cfg.Authentication is null
            || string.IsNullOrWhiteSpace(cfg.Authentication.ClientId)
            || string.IsNullOrWhiteSpace(cfg.Authentication.ClientSecret))
        {
            logger.LogError("ClientId/ClientSecret not found in config. Add them under Authentication.");
            return 1;
        }

        return await ExchangeCodeAndSaveAsync(cfg, configDir, redirectUrl, logger);
    }

    /// <summary>
    /// Common code exchange logic used by both auth-test and exchange-code commands.
    /// </summary>
    private static async Task<int> ExchangeCodeAndSaveAsync(AppSettings cfg, string configDir, string redirectUrl, ILogger logger)
    {
        try
        {
            var uri = new Uri(redirectUrl);
            var queryParams = TokenManager.ParseQueryString(uri);

            if (!queryParams.TryGetValue("code", out var code))
            {
                logger.LogError("No 'code' parameter found in redirect URL.");
                return 1;
            }

            var redirectUri = uri.GetLeftPart(UriPartial.Path);
            logger.LogInformation("Exchanging authorization code for access token...");

            var tokenUrl = cfg.Authentication!.TokenUrl ?? "https://focus.teamleader.eu/oauth2/access_token";
            var tokenObj = await OAuthClient.ExchangeAuthorizationCodeAsync(
                tokenUrl,
                cfg.Authentication.ClientId,
                cfg.Authentication.ClientSecret,
                code,
                redirectUri);

            if (tokenObj is null)
            {
                logger.LogError("Token exchange failed.");
                return 1;
            }

            var tokenPath = Path.Combine(configDir, "auth_token.json");
            OAuthClient.SaveAuthTokenToFile(tokenObj, tokenPath);
            logger.LogInformation("Token saved to: {TokenPath}", tokenPath);
            logger.LogInformation("Expires in: {ExpiresIn} seconds (obtained at {ObtainedAt:o})", tokenObj.ExpiresIn, tokenObj.ObtainedAt);
            logger.LogInformation("Authentication complete. You can now run the importer without --auth-test.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse or exchange code");
            return 1;
        }
    }

    /// <summary>
    /// Handles the --list-tasks command.
    /// </summary>
    public static async Task<int> RunListTasksAsync(AppSettings cfg, string configDir, ILogger logger)
    {
        var token = await TokenManager.AcquireTokenAsync(cfg, configDir, logger);
        if (string.IsNullOrWhiteSpace(token))
        {
            return 1;
        }

        using var http = new HttpClient { BaseAddress = new Uri(cfg.BaseUrl) };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var api = new TeamleaderApi(http);

        logger.LogInformation("Fetching tasks from Teamleader nextgen projects API...");

        try
        {
            var tasks = await api.ListTasksV2Async();
            if (tasks.Count == 0)
            {
                logger.LogInformation("No tasks found.");
                return 0;
            }

            logger.LogInformation("Found {Count} tasks:", tasks.Count);
            Console.WriteLine();
            Console.WriteLine("ID                                     Title");
            Console.WriteLine(new string('-', 80));
            foreach (var task in tasks)
            {
                Console.WriteLine($"{task.Id,-38} {task.Name}");
            }
            return 0;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to fetch tasks from API");
            return 1;
        }
    }

    /// <summary>
    /// Handles the --list-timetracking command.
    /// </summary>
    public static async Task<int> RunListTimeTrackingAsync(AppSettings cfg, string configDir, ILogger logger)
    {
        var token = await TokenManager.AcquireTokenAsync(cfg, configDir, logger);
        if (string.IsNullOrWhiteSpace(token))
        {
            return 1;
        }

        using var http = new HttpClient { BaseAddress = new Uri(cfg.BaseUrl) };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var api = new TeamleaderApi(http);

        logger.LogInformation("Fetching time tracking entries from Teamleader...");

        try
        {
            var entries = await api.ListTimeTrackingAsync();

            if (entries.Count == 0)
            {
                logger.LogInformation("No time tracking entries found.");
                return 0;
            }

            logger.LogInformation("Found {Count} time tracking entries:", entries.Count);
            Console.WriteLine();
            Console.WriteLine($"{"ID",-38} {"Started",-20} {"Duration",-10} Description");
            Console.WriteLine(new string('-', 100));

            foreach (var entry in entries)
            {
                var durationStr = entry.DurationSeconds.HasValue
                    ? TimeSpan.FromSeconds(entry.DurationSeconds.Value).ToString(@"hh\:mm\:ss")
                    : "--:--:--";
                var startStr = entry.StartedOn?.ToString("yyyy-MM-dd HH:mm") ?? "N/A";
                var desc = entry.Description ?? "(no description)";
                if (desc.Length > 40) desc = desc.Substring(0, 37) + "...";

                Console.WriteLine($"{entry.Id,-38} {startStr,-20} {durationStr,-10} {desc}");
            }

            return 0;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to fetch time tracking from API");
            return 1;
        }
    }

    /// <summary>
    /// Handles the --list-projects command.
    /// </summary>
    public static async Task<int> RunListProjectsAsync(AppSettings cfg, string configDir, ILogger logger)
    {
        var token = await TokenManager.AcquireTokenAsync(cfg, configDir, logger);
        if (string.IsNullOrWhiteSpace(token))
        {
            return 1;
        }

        using var http = new HttpClient { BaseAddress = new Uri(cfg.BaseUrl) };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var api = new TeamleaderApi(http);

        logger.LogInformation("Fetching projects from Teamleader...");

        try
        {
            var projects = await api.ListAllProjectsAsync();

            if (projects.Count == 0)
            {
                logger.LogInformation("No projects found.");
                return 0;
            }

            logger.LogInformation("Found {Count} projects:", projects.Count);
            Console.WriteLine();
            Console.WriteLine("ID                                     Name");
            Console.WriteLine(new string('-', 80));

            foreach (var project in projects)
            {
                Console.WriteLine($"{project.Id,-38} {project.Name}");
            }

            return 0;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to fetch projects from API");
            return 1;
        }
    }

    /// <summary>
    /// Handles the --list-companies command.
    /// </summary>
    public static async Task<int> RunListCompaniesAsync(AppSettings cfg, string configDir, ILogger logger)
    {
        var token = await TokenManager.AcquireTokenAsync(cfg, configDir, logger);
        if (string.IsNullOrWhiteSpace(token))
        {
            return 1;
        }

        using var http = new HttpClient { BaseAddress = new Uri(cfg.BaseUrl) };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var api = new TeamleaderApi(http);

        logger.LogInformation("Fetching companies from Teamleader...");

        try
        {
            var companies = await api.ListCompaniesAsync();

            if (companies.Count == 0)
            {
                logger.LogInformation("No companies found.");
                return 0;
            }

            logger.LogInformation("Found {Count} companies:", companies.Count);
            Console.WriteLine();
            Console.WriteLine("ID                                     Name");
            Console.WriteLine(new string('-', 80));

            foreach (var company in companies)
            {
                Console.WriteLine($"{company.Id,-38} {company.Name}");
            }

            return 0;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to fetch companies from API");
            return 1;
        }
    }

    /// <summary>
    /// Handles the main import command.
    /// </summary>
    public static async Task<int> RunImportAsync(AppSettings cfg, string configDir, string inputPath, ILogger logger)
    {
        var token = await TokenManager.AcquireTokenAsync(cfg, configDir, logger);
        if (string.IsNullOrWhiteSpace(token))
        {
            return 1;
        }

        if (!File.Exists(inputPath))
        {
            logger.LogError("Input file not found: {InputPath}", inputPath);
            return 1;
        }

        using var http = new HttpClient { BaseAddress = new Uri(cfg.BaseUrl) };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var api = new TeamleaderApi(http);
        var importSettings = cfg.Import ?? new ImportSettings();
        var resolver = new Resolver(api, importSettings);

        var entries = CsvParser.Read(inputPath);
        int processed = 0;

        foreach (var e in entries)
        {
            logger.LogInformation("Processing: {Company} / {Project} / {Group} / {Task} / {Start}", e.Company, e.Project, e.Group, e.Task,e.Start);
            var companyId = await resolver.GetOrCreateCompanyAsync(e.Company);
            if (string.IsNullOrEmpty(companyId))
            {
                logger.LogError("Skipping entry: Company '{Company}' not found and creation is disabled.", e.Company);
                continue;
            }
            var projectResult = await resolver.GetOrCreateProjectAsync(companyId, e.Project);
            if (string.IsNullOrEmpty(projectResult.ProjectId))
            {
                logger.LogError("Skipping entry: Project '{Project}' not found for company '{Company}' and creation is disabled.", e.Project, e.Company);
                continue;
            }
            string groupValue = e.Group;
            string taskValue = e.Task;
            string notesValue = e.Notes;
            string? groupId = null;
            if (projectResult.UsedProjectGroup)
            {
                // Swap: group becomes task, original task goes to notes
                groupValue = projectResult.ProjectGroupName ?? e.Group;
                notesValue = string.Join(
                    "\r\n",
                    new[] { e.Task == e.Group ? null : e.Task, e.Notes }
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                );
                taskValue = e.Group;
                groupId = projectResult.ProjectGroupId;
            }
            else
            {
                groupId = await resolver.GetOrCreateGroupAsync(projectResult.ProjectId, groupValue);
            }
            if (string.IsNullOrEmpty(groupId))
            {
                logger.LogError("Skipping entry: Group '{Group}' not found for project '{Project}' and creation is disabled.", groupValue, e.Project);
                continue;
            }
            var taskId = await resolver.GetOrCreateTaskAsync(groupId, taskValue, projectResult.ProjectId, e.TicketIds);
            if (string.IsNullOrEmpty(taskId))
            {
                logger.LogError("Skipping entry: Task '{Task}' not found and creation is disabled.", taskValue);
                continue;
            }
            // get task by id
            var task = await api.GetTaskV2ByIdAsync(taskId);
            var result = await api.AddTimeEntryAsync(task!, e.Start, e.End, notesValue);
            if (result)
            {
                logger.LogInformation("Time entry created.");
                processed++;
            }
            else
            {
                logger.LogWarning("Failed to create time entry for {Company}/{Project}/{Group}/{Task}.", e.Company, e.Project, groupValue, taskValue);
            }
        }

        logger.LogInformation("Done. Processed {ProcessedCount} entries.", processed);
        return 0;
    }
}
