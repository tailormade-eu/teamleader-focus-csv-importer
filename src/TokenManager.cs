using System.Text.Json;
using Microsoft.Extensions.Logging;

public static class TokenManager
{
    /// <summary>
    /// Loads a saved access token from auth_token.json if valid and not expired.
    /// </summary>
    public static async Task<string?> LoadSavedTokenAsync(string configDir, ILogger logger)
    {
        var tokenPath = Path.Combine(configDir, "auth_token.json");
        if (!File.Exists(tokenPath))
        {
            return null;
        }

        try
        {
            var tokenJson = await File.ReadAllTextAsync(tokenPath);
            var dto = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tokenJson);

            if (dto is null || !dto.TryGetValue("access_token", out var accessElem))
            {
                return null;
            }

            var accessToken = accessElem.GetString();
            var expiresIn = dto.TryGetValue("expires_in", out var expElem) ? expElem.GetInt32() : 3600;
            var obtainedAtStr = dto.TryGetValue("obtained_at", out var obtElem) ? obtElem.GetString() : null;

            if (string.IsNullOrWhiteSpace(accessToken) || !DateTime.TryParse(obtainedAtStr, out var obtainedAt))
            {
                return null;
            }

            // Check if token is expired (with 5 min buffer)
            var expiresAt = obtainedAt.AddSeconds(expiresIn);
            if (expiresAt <= DateTime.UtcNow.AddMinutes(5))
            {
                // Do not fail loudly here; AcquireTokenAsync will attempt a refresh if possible.
                return null;
            }

            logger.LogInformation("Using saved access token from auth_token.json");
            return accessToken;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load saved token");
            return null;
        }
    }

    /// <summary>
    /// Acquires a token from available sources in priority order.
    /// </summary>
    public static async Task<string?> AcquireTokenAsync(AppSettings cfg, string configDir, ILogger logger)
    {
        // 1. Check for saved auth token file
        var token = await LoadSavedTokenAsync(configDir, logger);
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token;
        }

        // Try to load saved auth details (including refresh_token) to attempt refresh
        try
        {
            var tokenPath = Path.Combine(configDir, "auth_token.json");
            if (File.Exists(tokenPath))
            {
                var tokenJson = await File.ReadAllTextAsync(tokenPath);
                var dto = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tokenJson);
                if (dto is not null && dto.TryGetValue("refresh_token", out var refreshElem))
                {
                    var refreshToken = refreshElem.GetString();
                    if (!string.IsNullOrWhiteSpace(refreshToken)
                        && cfg.Authentication is not null
                        && !string.IsNullOrWhiteSpace(cfg.Authentication.ClientId)
                        && !string.IsNullOrWhiteSpace(cfg.Authentication.ClientSecret))
                    {
                        logger.LogInformation("Saved token expired — attempting refresh using refresh_token...");
                        var tokenUrl = cfg.Authentication.TokenUrl ?? "https://focus.teamleader.eu/oauth2/access_token";
                        var refreshed = await OAuthClient.RefreshTokenAsync(tokenUrl, cfg.Authentication.ClientId, cfg.Authentication.ClientSecret, refreshToken);
                        if (refreshed is not null)
                        {
                            var tokenFile = Path.Combine(configDir, "auth_token.json");
                            OAuthClient.SaveAuthTokenToFile(refreshed, tokenFile);
                            logger.LogInformation("Refreshed token saved to {TokenPath}", tokenFile);
                            return refreshed.AccessToken;
                        }
                        logger.LogWarning("Refresh attempt failed. Please re-authenticate with --auth-test");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to attempt token refresh");
        }

        // 2. appsettings ApiToken
        if (!string.IsNullOrWhiteSpace(cfg.ApiToken))
        {
            logger.LogInformation("Using token from appsettings.json");
            return cfg.ApiToken;
        }

        // 3. Last resort: try client credentials (will likely fail for Teamleader)
        if (cfg.Authentication is not null 
            && !string.IsNullOrWhiteSpace(cfg.Authentication.ClientId) 
            && !string.IsNullOrWhiteSpace(cfg.Authentication.ClientSecret))
        {
            logger.LogInformation("No saved token found. Attempting OAuth client credentials flow...");
            var tokenUrl = cfg.Authentication.TokenUrl ?? "https://focus.teamleader.eu/oauth2/access_token";
            token = await OAuthClient.GetTokenAsync(tokenUrl, cfg.Authentication.ClientId, cfg.Authentication.ClientSecret);
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }
            logger.LogError("Failed to acquire OAuth token using client credentials.");
            logger.LogInformation("Run with --auth-test to authenticate interactively.");
            return null;
        }

        logger.LogError("No token found. Please authenticate using one of these methods:");
        logger.LogInformation("  1. Run: dotnet run -- --auth-test");
        logger.LogInformation("  2. Add ApiToken to appsettings.json");
        return null;
    }

    /// <summary>
    /// Parses query string from a redirect URL.
    /// </summary>
    public static Dictionary<string, string> ParseQueryString(Uri uri)
    {
        var qs = uri.Query.TrimStart('?');
        return qs.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split(new[] { '=' }, 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(k => Uri.UnescapeDataString(k[0]), v => Uri.UnescapeDataString(v[1]));
    }
}
