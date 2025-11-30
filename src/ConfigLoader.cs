/// <summary>
/// Strongly-typed configuration for the application.
/// </summary>
public class AppSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public string? ApiToken { get; set; }
    public AuthenticationSettings? Authentication { get; set; }
    public ImportSettings? Import { get; set; }
}

public class AuthenticationSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string? RedirectUri { get; set; }
    public string? AuthorizeUrl { get; set; }
    public string? TokenUrl { get; set; }
}

public class ImportSettings
{
    public bool CreateCompanies { get; set; } = true;
    public bool CreateProjects { get; set; } = true;
    public bool CreateGroups { get; set; } = true;
    public bool CreateTasks { get; set; } = true;
}
