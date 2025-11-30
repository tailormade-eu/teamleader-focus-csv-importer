using System.Collections.Generic;

public class ProjectResolutionResult
{
    public string? ProjectId { get; set; }
    public string? ProjectGroupId { get; set; }
    public bool UsedProjectGroup { get; set; }
    public string? ProjectGroupName { get; set; }
}

public class Resolver
{
    private readonly TeamleaderApi _api;
    private readonly ImportSettings _importSettings;
    private readonly Dictionary<string, string> _companyCache = new();
    private readonly Dictionary<string, string> _projectCache = new(); // key: companyId|projectName
    private readonly Dictionary<string, string> _projectGroupCache = new(); // key: projectId|projectGroupName
    private readonly Dictionary<string, string> _taskCache = new(); // key: projectGroupId|taskName

    public Resolver(TeamleaderApi api, ImportSettings importSettings)
    {
        _api = api;
        _importSettings = importSettings;
    }

    bool IsClose(string a, string b)
    {
        a = (a ?? string.Empty).ToLower().Trim();
        b = (b ?? string.Empty).ToLower().Trim();
        if (a.Contains(b) || b.Contains(a)) return true;
        if (a.Length >= 3 && b.Length >= 3 && a[..3] == b[..3]) return true;
        return false;
    }

    public async Task<string?> GetOrCreateCompanyAsync(string name)
    {
        if (_companyCache.TryGetValue(name, out var id)) return id;
        List<TeamleaderApi.ApiItem> companies;
        try
        {
            companies = await _api.SearchCompaniesAsync(name);
        }
        catch (HttpRequestException)
        {
            companies = new List<TeamleaderApi.ApiItem>();
        }
        foreach (var c in companies.OrderByDescending(c => c.Name.Length))
        {
            if (IsClose(c.Name, name))
            {
                _companyCache[name] = c.Id;
                return c.Id;
            }
        }
        if (!_importSettings.CreateCompanies)
        {
            return null;
        }
        var newId = await _api.AddCompanyAsync(name);
        _companyCache[name] = newId ?? string.Empty;
        return newId;
    }

    public async Task<ProjectResolutionResult> GetOrCreateProjectAsync(string companyId, string projectName)
    {
        var key = companyId + "|" + projectName;
        if (_projectCache.TryGetValue(key, out var pid))
            return new ProjectResolutionResult { ProjectId = pid };
        List<TeamleaderApi.ApiItem> projects;
        try
        {
            projects = await _api.ListProjectsAsync(companyId);
        }
        catch (HttpRequestException)
        {
            projects = new List<TeamleaderApi.ApiItem>();
        }
        foreach (var p in projects.OrderByDescending(c => c.Name.Length))
        {
            if (IsClose(p.Name, projectName))
            {
                _projectCache[key] = p.Id;
                return new ProjectResolutionResult { ProjectId = p.Id };
            }
        }
        // Fallback: search project groups
        var groups = new List<TeamleaderApi.ApiItem>();
        try
        {
            // If we ever have a projectId, pass it here. For now, only companyId is available.
            foreach (var p in projects)
            {
                var projectGroups = await _api.ListProjectGroupsAsync(p.Id);
                if (projectGroups != null && projectGroups.Any())
                {
                    groups.AddRange(projectGroups);
                }
            }
        }
        catch (HttpRequestException)
        {
            groups = new List<TeamleaderApi.ApiItem>();
        }
        foreach (var g in groups.OrderByDescending(c => c.Name.Length))
        {
            if (IsClose(g.Name, projectName))
            {
                // Found in project groups — cache the project group (phase) rather than writing into the project cache
                // Key format for _phaseCache is projectId|phaseName
                var parentProjectId = g.ProjectId ?? string.Empty;
                var projectGroupKey = parentProjectId + "|" + (g.Name ?? string.Empty);
                _projectGroupCache[projectGroupKey] = g.Id;
                return new ProjectResolutionResult {
                    ProjectId = g.ProjectId,
                    ProjectGroupId = g.Id,
                    UsedProjectGroup = true,
                    ProjectGroupName = g.Name
                };
            }
        }
        if (!_importSettings.CreateProjects)
        {
            return new ProjectResolutionResult { ProjectId = null };
        }
        var newId = await _api.AddProjectAsync(companyId, projectName);
        _projectCache[key] = newId ?? string.Empty;
        return new ProjectResolutionResult { ProjectId = newId };
    }

    public async Task<string?> GetOrCreateGroupAsync(string projectId, string phaseName)
    {
        var key = projectId + "|" + phaseName;
        if (_projectGroupCache.TryGetValue(key, out var id)) return id;
        List<TeamleaderApi.ApiItem> projectGroups;
        try
        {
            // Use v2 project groups listing instead of legacy projectPhases
            projectGroups = await _api.ListProjectGroupsAsync(projectId);
        }
        catch (HttpRequestException)
        {
            projectGroups = new List<TeamleaderApi.ApiItem>();
        }
        foreach (var pg in projectGroups.OrderByDescending(c => c.Name.Length))
        {
            if (IsClose(pg.Name, phaseName))
            {
                _projectGroupCache[key] = pg.Id;
                return pg.Id;
            }
        }
        if (!_importSettings.CreateGroups)
        {
            return null;
        }
        var newId = await _api.AddProjectGroupV2Async(projectId, phaseName);
        _projectGroupCache[key] = newId ?? string.Empty;
        return newId;
    }

    public async Task<string?> GetOrCreateTaskAsync(string projectGroupId, string taskName, string? projectId = null, IEnumerable<string>? ticketIds = null)
    {
        var key = projectGroupId + "|" + taskName;
        if (_taskCache.TryGetValue(key, out var id)) return id;

        // Search for existing task in project group using v2 API
        var tasks = await _api.ListTasksV2Async(projectGroupId, projectId, true);

        // If ticket ids were provided, prefer matching by ticket id contained in task name or description
        if (ticketIds is not null)
        {
            var ticketList = ticketIds.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            if (ticketList.Count > 0)
            {
                foreach (var t in tasks)
                {
                    var hay = (t.Name ?? string.Empty).ToLowerInvariant();
                    foreach (var tid in ticketList)
                    {
                        var norm = tid.ToLowerInvariant().Trim();
                        if (hay.Contains(norm))
                        {
                            _taskCache[key] = t.Id;
                            return t.Id;
                        }
                    }
                }
            }
        }

        foreach (var t in tasks.OrderByDescending(c => c.Name.Length))
        {
            if (IsClose(t.Name, taskName))
            {
                _taskCache[key] = t.Id;
                return t.Id;
            }
        }
        if (!_importSettings.CreateTasks)
        {
            return null;
        }
        var newId = await _api.AddTaskV2Async(projectGroupId, taskName, projectId, ticketIds);
        _taskCache[key] = newId ?? string.Empty;
        return newId;
    }
}
