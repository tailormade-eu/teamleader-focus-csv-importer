using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

public static class OAuthClient
{
    private static string? _cachedToken;
    private static DateTime _expiryUtc;

    public static async Task<string?> GetTokenAsync(string tokenUrl, string clientId, string clientSecret)
    {
        if (!string.IsNullOrWhiteSpace(_cachedToken) && DateTime.UtcNow < _expiryUtc.AddSeconds(-60))
            return _cachedToken;

        using var http = new HttpClient();

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        };

        using var content = new FormUrlEncodedContent(form);
        var resp = await http.PostAsync(tokenUrl, content);
        if (!resp.IsSuccessStatusCode) return null;

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("access_token", out var tokenProp)) return null;
        var token = tokenProp.GetString();

        int expiresIn = 3600;
        if (doc.RootElement.TryGetProperty("expires_in", out var expProp) && expProp.ValueKind == JsonValueKind.Number)
        {
            if (expProp.TryGetInt32(out var ei)) expiresIn = ei;
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            _cachedToken = token;
            _expiryUtc = DateTime.UtcNow.AddSeconds(expiresIn);
        }

        return _cachedToken;
    }

    public record AuthToken(string AccessToken, string? RefreshToken, int ExpiresIn, string? TokenType, DateTime ObtainedAt);

    public static async Task<AuthToken?> ExchangeAuthorizationCodeAsync(string tokenUrl, string clientId, string clientSecret, string code, string redirectUri)
    {
        using var http = new HttpClient();

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri
        };

        using var content = new FormUrlEncodedContent(form);
        var resp = await http.PostAsync(tokenUrl, content);
        if (!resp.IsSuccessStatusCode) return null;

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        var tokenType = root.TryGetProperty("token_type", out var tt) ? tt.GetString() : null;
        var expiresIn = 3600;
        if (root.TryGetProperty("expires_in", out var ei) && ei.ValueKind == JsonValueKind.Number)
        {
            if (ei.TryGetInt32(out var v)) expiresIn = v;
        }

        if (string.IsNullOrWhiteSpace(access)) return null;

        var tok = new AuthToken(access, refresh, expiresIn, tokenType, DateTime.UtcNow);
        // update client-credentials cache as well
        _cachedToken = tok.AccessToken;
        _expiryUtc = DateTime.UtcNow.AddSeconds(tok.ExpiresIn);
        return tok;
    }

    public static async Task<AuthToken?> RefreshTokenAsync(string tokenUrl, string clientId, string clientSecret, string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return null;
        using var http = new HttpClient();

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken
        };

        using var content = new FormUrlEncodedContent(form);
        var resp = await http.PostAsync(tokenUrl, content);
        if (!resp.IsSuccessStatusCode) return null;

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        var tokenType = root.TryGetProperty("token_type", out var tt) ? tt.GetString() : null;
        var expiresIn = 3600;
        if (root.TryGetProperty("expires_in", out var ei) && ei.ValueKind == JsonValueKind.Number)
        {
            if (ei.TryGetInt32(out var v)) expiresIn = v;
        }

        if (string.IsNullOrWhiteSpace(access)) return null;

        var tok = new AuthToken(access, refresh, expiresIn, tokenType, DateTime.UtcNow);
        // update in-memory cache
        _cachedToken = tok.AccessToken;
        _expiryUtc = DateTime.UtcNow.AddSeconds(tok.ExpiresIn);
        return tok;
    }

    public static void SaveAuthTokenToFile(AuthToken token, string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var dto = new Dictionary<string, object?>
        {
            ["access_token"] = token.AccessToken,
            ["refresh_token"] = token.RefreshToken,
            ["expires_in"] = token.ExpiresIn,
            ["token_type"] = token.TokenType,
            ["obtained_at"] = token.ObtainedAt.ToString("o")
        };
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }
}
