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
        var companies = await _api.SearchCompaniesAsync(name);
        foreach (var c in companies)
        {
            if (IsClose(c.Name, name))
            {
                _companyCache[name] = c.Id;
                return c.Id;
            }
        }
        var newId = await _api.AddCompanyAsync(name) ?? string.Empty;
        _companyCache[name] = newId;
        return newId;
    }

    public async Task<string> GetOrCreateProjectAsync(string companyId, string projectName)
    {
        var key = companyId + "|" + projectName;
        if (_projectCache.TryGetValue(key, out var pid)) return pid;
        var projects = await _api.ListProjectsAsync(companyId);
        foreach (var p in projects)
        {
            if (IsClose(p.Name, projectName))
            {
                _projectCache[key] = p.Id;
                return p.Id;
            }
        }
        var newId = await _api.AddProjectAsync(companyId, projectName) ?? string.Empty;
        _projectCache[key] = newId;
        return newId;
    }

    public async Task<string> GetOrCreateGroupAsync(string projectId, string phaseName)
    {
        var key = projectId + "|" + phaseName;
        if (_phaseCache.TryGetValue(key, out var id)) return id;
        var phases = await _api.ListProjectPhasesAsync(projectId);
        foreach (var ph in phases)
        {
            if (IsClose(ph.Name, phaseName))
            {
                _phaseCache[key] = ph.Id;
                return ph.Id;
            }
        }
        var newId = await _api.AddProjectPhaseAsync(projectId, phaseName) ?? string.Empty;
        _phaseCache[key] = newId;
        return newId;
    }

    public async Task<string> GetOrCreateTaskAsync(string projectPhaseId, string taskName)
    {
        var key = projectPhaseId + "|" + taskName;
        if (_taskCache.TryGetValue(key, out var id)) return id;

        // We don't have a direct tasks.list by phase id in KISS; attempt to list by project if possible (skipped here).
        // In KISS: tasks matched by exact equality; create if not found.

        var newId = await _api.AddTaskAsync(string.Empty, projectPhaseId, taskName) ?? string.Empty;
        _taskCache[key] = newId;
        return newId;
    }
}
