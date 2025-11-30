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

    public record ApiItem(string Id, string Name, string? Description, string? CompanyId, string? ProjectId, string? ProjectGroupId = null, string? Status = null, string? TaskId = null);

    // List tasks using projects-v2 API. Accepts optional filters for project_group_id or project_id.
    // List tasks using projects-v2 API. Accepts optional filters for project_group_id or project_id.
    // By default this method will fetch all pages and return the aggregated results.
    public async Task<List<ApiItem>> ListTasksV2Async(string? projectGroupId = null, string? projectId = null, bool onlyOpen = false, int pageSize = 100)
    {
        var results = new List<ApiItem>();
        var idsFilter = new List<string>();

        // If we have a projectId, prefer the projects-v2/projectLines.list endpoint
        // which accepts a server-side project_id filter and can limit line types to tasks.
        if (!string.IsNullOrEmpty(projectId))
        {
            var payload = new Dictionary<string, object?>
            {
                ["project_id"] = projectId,
                ["filter"] = new Dictionary<string, object?>
                {
                    ["types"] = new[] { "nextgenTask" }
                }
            };

            var resp = await _http.PostAsync("https://api.focus.teamleader.eu/projects-v2/projectLines.list", JsonContent.Create(payload));
            if (!resp.IsSuccessStatusCode)
            {
                var errorBody = await resp.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Response status code does not indicate success: {(int)resp.StatusCode} ({resp.ReasonPhrase}). Body: {errorBody}");
            }
            var json = await resp.Content.ReadAsStringAsync();
            var rawPageItems = ParseProjectLinesFromJson(json);

            // Apply optional client-side filtering by projectGroupId (projectLines.list returns items for the project)
            IEnumerable<ApiItem> pageItems = rawPageItems;
            if (!string.IsNullOrEmpty(projectGroupId))
            {
                pageItems = pageItems.Where(i => string.Equals(i.ProjectGroupId, projectGroupId, StringComparison.OrdinalIgnoreCase));
            }
            idsFilter = pageItems.Where(r => !string.IsNullOrEmpty(r.TaskId)).Select(r => r.TaskId!).ToList();
        }

        // Fallback: no projectId available -> use the generic tasks.list paging and client-side filter
        int fallbackPage = 1;
        while (true)
        {
            var payload = new Dictionary<string, object?>
            {
                ["page"] = new { size = pageSize, number = fallbackPage }
            };
            if (idsFilter.Count > 0)
            {
                payload["filter"] = new { ids = idsFilter };
            }

            // NOTE: the v2 tasks.list endpoint supports filtering by a list of task IDs only.
            // project_group_id / project_id are not supported as server-side filters here,
            // so we request pages unfiltered and apply project/group filtering client-side after parsing.

            var resp = await _http.PostAsync("https://api.focus.teamleader.eu/projects-v2/tasks.list", JsonContent.Create(payload));
            if (!resp.IsSuccessStatusCode)
            {
                var errorBody = await resp.Content.ReadAsStringAsync();
                throw new HttpRequestException($"Response status code does not indicate success: {(int)resp.StatusCode} ({resp.ReasonPhrase}). Body: {errorBody}");
            }
            var json = await resp.Content.ReadAsStringAsync();
            var rawPageItems = ParseListFromJson(json);
            if (rawPageItems.Count == 0) break;

            // Apply client-side filtering for project/group if requested (ids filter only supports task IDs server-side)
            IEnumerable<ApiItem> pageItems = rawPageItems;
            if (!string.IsNullOrEmpty(projectGroupId))
            {
                pageItems = pageItems.Where(i => string.Equals(i.ProjectGroupId, projectGroupId, StringComparison.OrdinalIgnoreCase));
            }
            else if (!string.IsNullOrEmpty(projectId))
            {
                pageItems = pageItems.Where(i => string.Equals(i.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));
            }

            // If onlyOpen is requested, filter by allowed open statuses
            if (onlyOpen)
            {
                var openStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "to_do",
                    "in_progress",
                    "on_hold"
                };
                pageItems = pageItems.Where(i => !string.IsNullOrEmpty(i.Status) && openStatuses.Contains(i.Status));
            }

            var filtered = pageItems.ToList();
            if (filtered.Count > 0) results.AddRange(filtered);

            if (rawPageItems.Count < pageSize) break; // last page
            fallbackPage++;
        }

        return results;
    }

    private static List<ApiItem> ParseListFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ApiItem>();
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var el in data.EnumerateArray())
            {
                var id = el.TryGetProperty("id", out var idp) ? idp.GetString() ?? string.Empty : string.Empty;
                // Try "name" first, then "title" (projects-v2 uses title)
                var nm = el.TryGetProperty("name", out var np) ? np.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrEmpty(nm) && el.TryGetProperty("title", out var tp))
                {
                    nm = tp.GetString() ?? string.Empty;
                }
                string? companyId = null;
                if (el.TryGetProperty("company_id", out var cp))
                {
                    companyId = cp.GetString();
                }
                // Fallback: try to get company from customers array
                if (companyId == null && el.TryGetProperty("customers", out var custArr) && custArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cust in custArr.EnumerateArray())
                    {
                        if (cust.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "company" && cust.TryGetProperty("id", out var idProp))
                        {
                            companyId = idProp.GetString();
                            break;
                        }
                    }
                }
                string? projectId = null;
                string? projectGroupId = null;
                string? status = null;
                string? taskId = null;
                string? description = null;
                if (el.TryGetProperty("project", out var projObj) && projObj.ValueKind == JsonValueKind.Object)
                {
                    if (projObj.TryGetProperty("type", out var projType) && projType.GetString() == "nextgenProject" && projObj.TryGetProperty("id", out var projIdProp))
                    {
                        projectId = projIdProp.GetString();
                    }
                }
                if (el.TryGetProperty("group", out var groupObj) && groupObj.ValueKind == JsonValueKind.Object)
                {
                    if (groupObj.TryGetProperty("type", out var grpType) && grpType.GetString() == "nextgenProjectGroup" && groupObj.TryGetProperty("id", out var grpIdProp))
                    {
                        projectGroupId = grpIdProp.GetString();
                    }
                }
                if (el.TryGetProperty("status", out var sp) && sp.ValueKind != JsonValueKind.Null)
                {
                    status = sp.GetString();
                }
                if (el.TryGetProperty("description", out var dp) && dp.ValueKind != JsonValueKind.Null)
                {
                    description = dp.GetString();
                }
                // Try to find a nested reference to a nextgenTask, e.g. {"type":"nextgenTask","id":"..."}
                taskId = FindReferencedIdByType(el, "nextgenTask");
                list.Add(new ApiItem(id, nm, description, companyId, projectId, projectGroupId, status, taskId));
            }
        }
        return list;
    }

    private static string? FindReferencedIdByType(JsonElement el, string wantedType)
    {
        // If this element is an object, check if it matches
        if (el.ValueKind == JsonValueKind.Object)
        {
            if (el.TryGetProperty("type", out var typeProp) && string.Equals(typeProp.GetString(), wantedType, StringComparison.OrdinalIgnoreCase)
                && el.TryGetProperty("id", out var idProp))
            {
                return idProp.GetString();
            }

            foreach (var prop in el.EnumerateObject())
            {
                var found = FindReferencedIdByType(prop.Value, wantedType);
                if (found is not null) return found;
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                var found = FindReferencedIdByType(item, wantedType);
                if (found is not null) return found;
            }
        }
        return null;
    }

    // Specialized parser for the projects-v2/projectLines.list response format.
    // Each data element typically contains a "line" (which may reference a nextgenTask)
    // and a "group" (nextgenProjectGroup). This parser extracts task id, group id,
    // and returns ApiItem entries with those fields populated.
    private static List<ApiItem> ParseProjectLinesFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ApiItem>();
        if (!doc.RootElement.TryGetProperty("data", out var data)) return list;

        foreach (var el in data.EnumerateArray())
        {
            string? taskId = null;
            string? groupId = null;

            if (el.ValueKind != JsonValueKind.Object) continue;

            if (el.TryGetProperty("line", out var lineObj) && lineObj.ValueKind == JsonValueKind.Object)
            {
                // line may be a typed reference { "type": "nextgenTask", "id": "..." }
                if (lineObj.TryGetProperty("type", out var lt) && lt.GetString() == "nextgenTask" && lineObj.TryGetProperty("id", out var lid))
                {
                    taskId = lid.GetString();
                }


            }

            // Prefer top-level 'group' sibling, but also accept a nested 'group' under 'line' for robustness
            if (el.TryGetProperty("group", out var groupObj) && groupObj.ValueKind == JsonValueKind.Object)
            {
                if (groupObj.TryGetProperty("type", out var gtype) && gtype.GetString() == "nextgenProjectGroup" && groupObj.TryGetProperty("id", out var gid))
                {
                    groupId = gid.GetString();
                }
                else if (groupObj.TryGetProperty("id", out var gid2))
                {
                    groupId = gid2.GetString();
                }
            }
            else
            {
                // fallback: sometimes group information may be nested under the 'line' object
                if (el.TryGetProperty("line", out var lineNested) && lineNested.ValueKind == JsonValueKind.Object && lineNested.TryGetProperty("group", out var nestedGroup) && nestedGroup.ValueKind == JsonValueKind.Object)
                {
                    if (nestedGroup.TryGetProperty("type", out var ngt) && ngt.GetString() == "nextgenProjectGroup" && nestedGroup.TryGetProperty("id", out var ngid))
                    {
                        groupId = ngid.GetString();
                    }
                    else if (nestedGroup.TryGetProperty("id", out var ngid2))
                    {
                        groupId = ngid2.GetString();
                    }
                }
            }


            // If we didn't find taskId yet, search anywhere in element for a nextgenTask reference
            if (taskId == null) taskId = FindReferencedIdByType(el, "nextgenTask");

            // Use taskId as Id when present, otherwise fall back to empty id
            var id = taskId ?? string.Empty;
            list.Add(new ApiItem(id, string.Empty, null, null, null, groupId, null, taskId));
        }

        return list;
    }

    public async Task<List<ApiItem>> SearchCompaniesAsync(string name)
    {
        var payload = new { filter = new { term = name } };
        var resp = await _http.PostAsJsonAsync("/companies.list", payload);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return ParseListFromJson(json);
    }

    public async Task<List<ApiItem>> ListCompaniesAsync(int pageSize = 20, int pageNumber = 1)
    {
        var payload = new { page = new { size = pageSize, number = pageNumber } };
        var resp = await _http.PostAsJsonAsync("/companies.list", payload);
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

    public async Task<List<ApiItem>> ListProjectsAsync(string companyId,bool onlyOpen = true)
    {
        // Use the documented customers filter to filter projects by company
        var payload = new
        {
            filter = new
            {
                customers = new[] { new { type = "company", id = companyId } }
            },
            page = new { size = 20, number = 1 }
        };
        var resp = await _http.PostAsJsonAsync("https://api.focus.teamleader.eu/projects-v2/projects.list", payload);
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Response status code does not indicate success: {(int)resp.StatusCode} ({resp.ReasonPhrase}). Body: {errorBody}");
        }
        var json = await resp.Content.ReadAsStringAsync();
        var results = ParseListFromJson(json);
        if (onlyOpen)
        {
            var openStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "open"
            };
            results = results.Where(p => !string.IsNullOrEmpty(p.Status) && openStatuses.Contains(p.Status)).ToList();
        }
        return results;
    }

    public async Task<List<ApiItem>> ListProjectGroupsAsync(string? projectId=null, int pageSize = 20, int pageNumber = 1)
    {
        // If filtering by company or project, use a large page size to get all results
        if (!string.IsNullOrEmpty(projectId))
        {
            pageSize = 1000;
        }
        var payload = new Dictionary<string, object?>
        {
            ["page"] = new { size = pageSize, number = pageNumber }
        };
        if (!string.IsNullOrEmpty(projectId))
        {
            var filter = new Dictionary<string, object?>();
            if (!string.IsNullOrEmpty(projectId)) filter["project_id"] = projectId;
            payload["filter"] = filter;
        }
        var resp = await _http.PostAsync("https://api.focus.teamleader.eu/projects-v2/projectGroups.list", JsonContent.Create(payload));
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Response status code does not indicate success: {(int)resp.StatusCode} ({resp.ReasonPhrase}). Body: {errorBody}");
        }
        var json = await resp.Content.ReadAsStringAsync();
        return ParseListFromJson(json);
    }

    public async Task<List<ApiItem>> ListAllProjectsAsync(int pageSize = 20, int pageNumber = 1)
    {
        var payload = new { page = new { size = pageSize, number = pageNumber } };
        // Projects v2 API uses a different base URL
        var resp = await _http.PostAsync("https://api.focus.teamleader.eu/projects-v2/projects.list", JsonContent.Create(payload));
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Response status code does not indicate success: {(int)resp.StatusCode} ({resp.ReasonPhrase}). Body: {errorBody}");
        }
        var json = await resp.Content.ReadAsStringAsync();
        return ParseListFromJson(json);
    }

    public async Task<ApiItem?> GetTaskV2ByIdAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        // Use the tasks.info endpoint which returns a single task object under 'data'
        var payload = new Dictionary<string, object?>
        {
            ["id"] = id
        };

        var resp = await _http.PostAsync("https://api.focus.teamleader.eu/projects-v2/tasks.info", JsonContent.Create(payload));
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Response status code does not indicate success: {(int)resp.StatusCode} ({resp.ReasonPhrase}). Body: {errorBody}");
        }

        var json = await resp.Content.ReadAsStringAsync();
        // Normalise response: tasks.info returns an object under 'data', while ParseListFromJson expects 'data' to be an array.
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var dataEl))
            {
                string normalized;
                if (dataEl.ValueKind == JsonValueKind.Object)
                {
                    normalized = "{\"data\": [" + dataEl.GetRawText() + "]}";
                }
                else
                {
                    normalized = json;
                }
                var list = ParseListFromJson(normalized);
                return list.FirstOrDefault();
            }
        }
        catch (JsonException)
        {
            // fall through to return null
        }

        return null;
    }

    public async Task<string?> AddProjectAsync(string companyId, string name)
    {
        var payload = new { company_id = companyId, name };
        var resp = await _http.PostAsJsonAsync("https://api.focus.teamleader.eu/projects-v2/projects.add", payload);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        var list = ParseListFromJson(json);
        return list.FirstOrDefault()?.Id;
    }

    // Create a project group (projects-v2). Uses projects-v2/projectGroups.create and returns the created group's id.
    public async Task<string?> AddProjectGroupV2Async(string projectId, string title)
    {
        var payload = new Dictionary<string, object?>
        {
            ["project_id"] = projectId,
            ["title"] = title
        };

        var resp = await _http.PostAsync("https://api.focus.teamleader.eu/projects-v2/projectGroups.create", JsonContent.Create(payload));
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("id", out var idp)) return idp.GetString();
                // Some responses may nest under "project_group"
                if (data.TryGetProperty("project_group", out var pg) && pg.ValueKind == JsonValueKind.Object && pg.TryGetProperty("id", out var pgid))
                {
                    return pgid.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // ignore and return null
        }
        return null;
    }

    // NOTE: Use ListTasksV2Async with projectId filter instead of the old v1 tasks.list by project.

    // Teamleader v2: Create task using projects-v2/tasks.create
    // The nextgen API requires a project_id and title. group_id is optional.
    public async Task<string?> AddTaskV2Async(string projectGroupId, string title, string? projectId = null, IEnumerable<string>? ticketIds = null)
    {
        // If ticket IDs provided, prefix them to the title (e.g. "#123, #456 : original title")
        if (ticketIds is not null)
        {
            var ids = ticketIds.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToArray();
            if (ids.Length > 0)
            {
                title = string.Join(", ", ids) + " : " + title;
            }
        }

        // project_id is required by the API; prefer provided projectId, otherwise try to omit
        var payload = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["work_type_id"] = "6b2c6563-aded-0eb0-a041-87e3cc2b3dca"
        };
        if (!string.IsNullOrEmpty(projectId)) payload["project_id"] = projectId;
        if (!string.IsNullOrEmpty(projectGroupId)) payload["group_id"] = projectGroupId;

        var resp = await _http.PostAsync("https://api.focus.teamleader.eu/projects-v2/tasks.create", JsonContent.Create(payload));
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("id", out var idp)) return idp.GetString();
                // Some responses may wrap the task object under "task"
                if (data.TryGetProperty("task", out var taskObj) && taskObj.ValueKind == JsonValueKind.Object && taskObj.TryGetProperty("id", out var tidp))
                {
                    return tidp.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // fall through to return null
        }
        return null;
    }

    public async Task<bool> AddTimeEntryAsync(ApiItem task, DateTime? start, DateTime? end, string description)
    {
        // The NextGen timeTracking.add endpoint expects a subject reference and
        // ISO-8601 timestamps. Use "subject": { "type": "nextgenTask", "id": "..." }
        // and include duration (seconds) when an end time is provided.
        var payload = new Dictionary<string, object?>
        {
            ["subject"] = new { type = "nextgenTask", id = task.Id },
            ["started_at"] = start?.ToString("yyyy-MM-dd'T'HH:mm:sszzz"),
            ["ended_at"] = end?.ToString("yyyy-MM-dd'T'HH:mm:sszzz"),
            ["description"] = string.IsNullOrEmpty(description) ? string.Join("\r\n", new[] { task.Name, task.Description }.Where(s => !string.IsNullOrWhiteSpace(s))) : description
        };

        var resp = await _http.PostAsJsonAsync("/timeTracking.add", payload);
        if (!resp.IsSuccessStatusCode)
        {
            // bubble up error details for debugging
            var body = await resp.Content.ReadAsStringAsync();
            throw new HttpRequestException($"timeTracking.add failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}");
        }
        return true;
    }

    public record TimeTrackingEntry(
        string Id,
        string? Description,
        DateTime? StartedOn,
        DateTime? EndedOn,
        int? DurationSeconds,
        string? SubjectType,
        string? SubjectId);

    public async Task<List<TimeTrackingEntry>> ListTimeTrackingAsync(int pageSize = 20, int pageNumber = 1)
    {
        var payload = new { page = new { size = pageSize, number = pageNumber } };
        // Time tracking uses the focus API base URL
        var resp = await _http.PostAsync("https://api.focus.teamleader.eu/timeTracking.list", JsonContent.Create(payload));
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await resp.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Response status code does not indicate success: {(int)resp.StatusCode} ({resp.ReasonPhrase}). Body: {errorBody}");
        }
        var json = await resp.Content.ReadAsStringAsync();
        return ParseTimeTrackingFromJson(json);
    }

    private static List<TimeTrackingEntry> ParseTimeTrackingFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<TimeTrackingEntry>();
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var el in data.EnumerateArray())
            {
                var id = el.TryGetProperty("id", out var idp) ? idp.GetString() ?? string.Empty : string.Empty;
                var description = el.TryGetProperty("description", out var dp) ? dp.GetString() : null;

                DateTime? startedOn = null;
                if (el.TryGetProperty("started_on", out var sop) && sop.ValueKind != JsonValueKind.Null)
                {
                    startedOn = DateTime.Parse(sop.GetString()!);
                }

                DateTime? endedOn = null;
                if (el.TryGetProperty("ended_on", out var eop) && eop.ValueKind != JsonValueKind.Null)
                {
                    endedOn = DateTime.Parse(eop.GetString()!);
                }

                int? duration = null;
                if (el.TryGetProperty("duration", out var durp) && durp.ValueKind != JsonValueKind.Null)
                {
                    // duration is a plain number in seconds
                    duration = durp.GetInt32();
                }

                string? subjectType = null;
                string? subjectId = null;
                if (el.TryGetProperty("subject", out var subj) && subj.ValueKind != JsonValueKind.Null)
                {
                    subjectType = subj.TryGetProperty("type", out var stp) ? stp.GetString() : null;
                    subjectId = subj.TryGetProperty("id", out var sip) ? sip.GetString() : null;
                }

                list.Add(new TimeTrackingEntry(id, description, startedOn, endedOn, duration, subjectType, subjectId));
            }
        }
        return list;
    }
}

