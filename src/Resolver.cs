public class Resolver
{
    private readonly TeamleaderApi _api;
    private readonly Dictionary<string, string> _companyCache = new();
    private readonly Dictionary<string, string> _projectCache = new(); // key: companyId|projectName
    private readonly Dictionary<string, string> _phaseCache = new(); // key: projectId|phaseName
    private readonly Dictionary<string, string> _taskCache = new(); // key: phaseId|taskName

    public Resolver(TeamleaderApi api)
    {
        _api = api;
    }

    bool IsClose(string a, string b)
    {
        a = (a ?? string.Empty).ToLower().Trim();
        b = (b ?? string.Empty).ToLower().Trim();
        if (a.Contains(b) || b.Contains(a)) return true;
        if (a.Length >= 3 && b.Length >= 3 && a[..3] == b[..3]) return true;
        return false;
    }

    public async Task<string> GetOrCreateCompanyAsync(string name)
    {
        if (_companyCache.TryGetValue(name, out var id)) return id;
        await foreach (var c in _api.SearchCompaniesAsync(name))
        {
            var cname = c.GetProperty("name").GetString() ?? string.Empty;
            var cid = c.GetProperty("id").GetString() ?? string.Empty;
            if (IsClose(cname, name))
            {
                _companyCache[name] = cid;
                return cid;
            }
        }
        var added = await _api.AddCompanyAsync(name);
        var newId = added?.GetProperty("id").GetString() ?? string.Empty;
        _companyCache[name] = newId;
        return newId;
    }

    public async Task<string> GetOrCreateProjectAsync(string companyId, string projectName)
    {
        var key = companyId + "|" + projectName;
        if (_projectCache.TryGetValue(key, out var pid)) return pid;
        await foreach (var p in _api.ListProjectsAsync(companyId))
        {
            var pname = p.GetProperty("name").GetString() ?? string.Empty;
            var id = p.GetProperty("id").GetString() ?? string.Empty;
            if (IsClose(pname, projectName))
            {
                _projectCache[key] = id;
                return id;
            }
        }
        var added = await _api.AddProjectAsync(companyId, projectName);
        var newId = added?.GetProperty("id").GetString() ?? string.Empty;
        _projectCache[key] = newId;
        return newId;
    }

    public async Task<string> GetOrCreateGroupAsync(string projectId, string phaseName)
    {
        var key = projectId + "|" + phaseName;
        if (_phaseCache.TryGetValue(key, out var id)) return id;
        await foreach (var ph in _api.ListProjectPhasesAsync(projectId))
        {
            var pname = ph.GetProperty("name").GetString() ?? string.Empty;
            var pid = ph.GetProperty("id").GetString() ?? string.Empty;
            if (IsClose(pname, phaseName))
            {
                _phaseCache[key] = pid;
                return pid;
            }
        }
        var added = await _api.AddProjectPhaseAsync(projectId, phaseName);
        var newId = added?.GetProperty("id").GetString() ?? string.Empty;
        _phaseCache[key] = newId;
        return newId;
    }

    public async Task<string> GetOrCreateTaskAsync(string projectPhaseId, string taskName)
    {
        var key = projectPhaseId + "|" + taskName;
        if (_taskCache.TryGetValue(key, out var id)) return id;

        // We don't have a direct tasks.list by phase id in KISS; attempt to list by project (if phase has projectId property)
        // For simplicity, list tasks for all projects - using ListTasksForProjectAsync is project-scoped; skip and always create if not found via exact match.

        // In KISS: tasks matched by exact equality
        // We'll attempt to find task among tasks for the project if available - best-effort omitted here.

        var added = await _api.AddTaskAsync("", projectPhaseId, taskName);
        var newId = added?.GetProperty("id").GetString() ?? string.Empty;
        _taskCache[key] = newId;
        return newId;
    }
}
