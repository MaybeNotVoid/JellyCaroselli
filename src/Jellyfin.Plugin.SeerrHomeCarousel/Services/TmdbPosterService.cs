using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.SeerrHomeCarousel.Services;

/// <summary>
/// Fetches and caches poster images from the TMDB API.
/// </summary>
public class TmdbPosterService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TmdbPosterService> _logger;
    private readonly ConcurrentDictionary<string, string?> _posterPathCache = new();
    private readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> _imageCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbPosterService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger instance.</param>
    public TmdbPosterService(IHttpClientFactory httpClientFactory, ILogger<TmdbPosterService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Downloads and caches the poster image bytes for a TMDB movie or TV show.
    /// </summary>
    /// <param name="mediaType">Either "movie" or "tv".</param>
    /// <param name="tmdbId">The TMDB numeric identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The image bytes and MIME type, or null if unavailable.</returns>
    public async Task<(byte[] Bytes, string ContentType)?> GetPosterAsync(
        string mediaType,
        int tmdbId,
        CancellationToken cancellationToken)
    {
        string normalizedType = mediaType == "tv" ? "tv" : "movie";
        string cacheKey = $"{normalizedType}:{tmdbId}";

        if (_imageCache.TryGetValue(cacheKey, out var cachedImage))
        {
            return cachedImage;
        }

        string apiKey = (SeerrHomeCarouselPlugin.Instance.Configuration.TmdbApiKey ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            return null;
        }

        string? posterPath = await ResolvePosterPathAsync(normalizedType, tmdbId, apiKey, cacheKey, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(posterPath))
        {
            return null;
        }

        string imageUrl = $"https://image.tmdb.org/t/p/w500{posterPath}";
        try
        {
            using var client = _httpClientFactory.CreateClient();
            using var response = await client.GetAsync(imageUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            string contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            var entry = (bytes, contentType);

            if (_imageCache.Count > 500)
            {
                _imageCache.Clear();
            }

            _imageCache[cacheKey] = entry;
            return entry;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SeerrHomeCarousel • Failed to download TMDB poster {Url}", imageUrl);
            return null;
        }
    }

    private async Task<string?> ResolvePosterPathAsync(
        string mediaType,
        int tmdbId,
        string apiKey,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        if (_posterPathCache.TryGetValue(cacheKey, out string? cachedPath))
        {
            return cachedPath;
        }

        string? path = await FetchPosterPathFromTmdbAsync(mediaType, tmdbId, apiKey, "it-IT", cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(path))
        {
            path = await FetchPosterPathFromTmdbAsync(mediaType, tmdbId, apiKey, null, cancellationToken)
                .ConfigureAwait(false);
        }

        if (_posterPathCache.Count > 1000)
        {
            _posterPathCache.Clear();
        }

        _posterPathCache[cacheKey] = path;
        return path;
    }

    private async Task<string?> FetchPosterPathFromTmdbAsync(
        string mediaType,
        int tmdbId,
        string apiKey,
        string? language,
        CancellationToken cancellationToken)
    {
        bool isBearer = apiKey.Contains('.', StringComparison.Ordinal);
        string query = string.IsNullOrEmpty(language) ? string.Empty : $"?language={language}";
        if (!isBearer)
        {
            query = string.IsNullOrEmpty(query) ? $"?api_key={Uri.EscapeDataString(apiKey)}" : $"{query}&api_key={Uri.EscapeDataString(apiKey)}";
        }

        string url = $"https://api.themoviedb.org/3/{mediaType}/{tmdbId}{query}";

        try
        {
            using var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (isBearer)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var obj = JObject.Parse(json);
            return obj.Value<string>("poster_path");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SeerrHomeCarousel • TMDB metadata lookup failed for {MediaType}/{TmdbId}", mediaType, tmdbId);
            return null;
        }
    }
}
