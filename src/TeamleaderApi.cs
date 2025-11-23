using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

public class TeamleaderApi
{
    private readonly HttpClient _http;

    public TeamleaderApi(HttpClient http)
    {
        _http = http;
    }

    public record ApiItem(string Id, string Name);

    private static List<ApiItem> ParseListFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ApiItem>();
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var el in data.EnumerateArray())
            {
                var id = el.TryGetProperty("id", out var idp) ? idp.GetString() ?? string.Empty : string.Empty;
                var nm = el.TryGetProperty("name", out var np) ? np.GetString() ?? string.Empty : string.Empty;
                list.Add(new ApiItem(id, nm));
            }
        }
        return list;
    }

    public async Task<List<ApiItem>> SearchCompaniesAsync(string name)
    {
        var url = $"/companies.list?search={Uri.EscapeDataString(name)}";
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return ParseListFromJson(json);
    }

    public async Task<string?> AddCompanyAsync(string name)
    {
        var payload = new { name };
        var resp = await _http.PostAsJsonAsync("/companies.add", payload);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        var list = ParseListFromJson(json);
        return list.FirstOrDefault()?.Id;
    }

    public async Task<List<ApiItem>> ListProjectsAsync(string companyId)
    {
        var url = $"/projects.list?company_id={Uri.EscapeDataString(companyId)}";
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return ParseListFromJson(json);
    }

    public async Task<string?> AddProjectAsync(string companyId, string name)
    {
        var payload = new { company_id = companyId, name };
        var resp = await _http.PostAsJsonAsync("/projects.add", payload);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        var list = ParseListFromJson(json);
        return list.FirstOrDefault()?.Id;
    }

    public async Task<List<ApiItem>> ListProjectPhasesAsync(string projectId)
    {
        var url = $"/projectPhases.list?project_id={Uri.EscapeDataString(projectId)}";
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return ParseListFromJson(json);
    }

    public async Task<string?> AddProjectPhaseAsync(string projectId, string name)
    {
        var payload = new { project_id = projectId, name };
        var resp = await _http.PostAsJsonAsync("/projectPhases.add", payload);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        var list = ParseListFromJson(json);
        return list.FirstOrDefault()?.Id;
    }

    public async Task<List<ApiItem>> ListTasksForProjectAsync(string projectId)
    {
        var url = $"/tasks.list?project_id={Uri.EscapeDataString(projectId)}";
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return ParseListFromJson(json);
    }

    public async Task<string?> AddTaskAsync(string projectId, string phaseId, string name)
    {
        var payload = new { project_id = projectId, project_phase_id = phaseId, name };
        var resp = await _http.PostAsJsonAsync("/tasks.add", payload);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        var list = ParseListFromJson(json);
        return list.FirstOrDefault()?.Id;
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

