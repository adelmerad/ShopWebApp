using System.Collections.Concurrent;

namespace ShopWebApp.Services;

public enum TokenRefreshOutcome
{
    Valid,
    ReauthRequired,   // refresh token mort : vraie fin de session, il faut se reconnecter
    TransientFailure  // panne reseau/SSO : on ne deconnecte pas pour un probleme temporaire
}

// Rafraichit l'access token de facon proactive (avant expiration reelle),
// avec un verrou par session : le SSO fait tourner les refresh tokens (chaque
// usage revoque l'ancien et en emet un nouveau), donc deux appels concurrents
// qui rafraichiraient en meme temps feraient echouer le second. Le verrou
// evite ca, et le double-check apres l'avoir acquis evite un rafraichissement
// inutile si un autre appel vient de le faire pendant l'attente.
public class TokenRefreshService
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(8);

    private readonly TokenStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public TokenRefreshService(TokenStore store, IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _store = store;
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    public async Task<TokenRefreshOutcome> EnsureValidAsync(string sessionId)
    {
        var tokens = _store.Get(sessionId);
        if (tokens is null)
            return TokenRefreshOutcome.ReauthRequired;

        if (tokens.ExpiresAt - DateTimeOffset.UtcNow > RefreshMargin)
            return TokenRefreshOutcome.Valid;

        var semaphore = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        try
        {
            // Un autre appel a peut-etre deja rafraichi pendant qu'on attendait le verrou.
            tokens = _store.Get(sessionId);
            if (tokens is null)
                return TokenRefreshOutcome.ReauthRequired;
            if (tokens.ExpiresAt - DateTimeOffset.UtcNow > RefreshMargin)
                return TokenRefreshOutcome.Valid;
            if (tokens.RefreshToken is null)
                return TokenRefreshOutcome.ReauthRequired;

            HttpResponseMessage response;
            try
            {
                var client = _httpClientFactory.CreateClient();
                response = await client.PostAsync(
                    $"{_config["Sso:Authority"]}/connect/token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = tokens.RefreshToken,
                        ["client_id"] = _config["Sso:ClientId"]!,
                        ["client_secret"] = _config["Sso:ClientSecret"]!
                    }));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return TokenRefreshOutcome.TransientFailure;
            }

            if (!response.IsSuccessStatusCode)
                return TokenRefreshOutcome.ReauthRequired;

            var refreshed = await response.Content.ReadFromJsonAsync<TokenResponse>();
            if (refreshed is null)
                return TokenRefreshOutcome.TransientFailure;

            _store.Set(sessionId,
                new StoredTokens(refreshed.AccessToken, refreshed.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresIn)),
                SessionTtl);

            return TokenRefreshOutcome.Valid;
        }
        finally
        {
            semaphore.Release();
        }
    }
}
