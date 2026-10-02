using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace ShopWebApp.Services;

// Garde les vrais tokens (access/refresh) cote serveur, jamais dans le cookie.
// Le navigateur ne recoit qu'un session_id opaque (32 octets aleatoires) - un
// script XSS qui lirait le cookie n'obtiendrait qu'un identifiant inutile hors
// du serveur, jamais un token exploitable directement.
public class TokenStore
{
    private readonly IMemoryCache _cache;

    public TokenStore(IMemoryCache cache) => _cache = cache;

    public static string NewSessionId() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public void Set(string sessionId, StoredTokens tokens, TimeSpan ttl) =>
        _cache.Set($"tokens:{sessionId}", tokens, ttl);

    public StoredTokens? Get(string sessionId) =>
        _cache.TryGetValue($"tokens:{sessionId}", out StoredTokens? tokens) ? tokens : null;

    public void Remove(string sessionId) =>
        _cache.Remove($"tokens:{sessionId}");
}

public record StoredTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);
