using System.Net.Http.Json;
using System.Text.Json;

public class TeamleaderApi
{
    private readonly HttpClient _http;
    public TeamleaderApi(HttpClient http)
    {
        _http = http;
    }

    // Simple wrappers for endpoints used in KISS spec

    public async Task<IEnumerable<dynamic>> SearchCompaniesAsync(string name)
    {
        var url = $"/companies.list?search={Uri.EscapeDataString(name)}";
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var el in data.EnumerateArray()) yield return el;
        }
    }

    public async Task<dynamic?> AddCompanyAsync(string name)
    {
        var payload = new { name };
        var resp = await _http.PostAsJsonAsync("/companies.add", payload);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data)) return data.EnumerateArray().FirstOrDefault();
        return null;
    }

    public async Task<IEnumerable<dynamic>> ListProjectsAsync(string companyId)
    {
        var url = $"/projects.list?company_id={Uri.EscapeDataString(companyId)}";
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var el in data.EnumerateArray()) yield return el;
        }
    }

    public async Task<dynamic?> AddProjectAsync(string companyId, string name)
    {
        var payload = new { company_id = companyId, name };
        var resp = await _http.PostAsJsonAsync("/projects.add", payload);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data)) return data.EnumerateArray().FirstOrDefault();
        return null;
    }

    public async Task<IEnumerable<dynamic>> ListProjectPhasesAsync(string projectId)
    {
        var url = $"/projectPhases.list?project_id={Uri.EscapeDataString(projectId)}";
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var el in data.EnumerateArray()) yield return el;
        }
    }

    public async Task<dynamic?> AddProjectPhaseAsync(string projectId, string name)
    {
        var payload = new { project_id = projectId, name };
        var resp = await _http.PostAsJsonAsync("/projectPhases.add", payload);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data)) return data.EnumerateArray().FirstOrDefault();
        return null;
    }

    public async Task<IEnumerable<dynamic>> ListTasksForProjectAsync(string projectId)
    {
        var url = $"/tasks.list?project_id={Uri.EscapeDataString(projectId)}";
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var el in data.EnumerateArray()) yield return el;
        }
    }

    public async Task<dynamic?> AddTaskAsync(string projectId, string phaseId, string name)
    {
        var payload = new { project_id = projectId, project_phase_id = phaseId, name };
        var resp = await _http.PostAsJsonAsync("/tasks.add", payload);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data)) return data.EnumerateArray().FirstOrDefault();
        return null;
    }

    public async Task<bool> AddTimeEntryAsync(string taskId, DateTime? start, DateTime? end, string description)
    {
        var payload = new Dictionary<string, object?>
        {
            ["task_id"] = taskId,
            ["started_at"] = start?.ToString("yyyy-MM-dd HH:mm:ss"),
            ["ended_at"] = end?.ToString("yyyy-MM-dd HH:mm:ss"),
            ["description"] = description
        };

        var resp = await _http.PostAsJsonAsync("/timeTracking.add", payload);
        return resp.IsSuccessStatusCode;
    }
}
