public class WorkEntry
{
    public string Company { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public string Task { get; init; } = string.Empty;
    public string[] Extras { get; init; } = Array.Empty<string>();
    public DateTime? Start { get; init; }
    public DateTime? End { get; init; }
    public string Notes { get; init; } = string.Empty;
}
