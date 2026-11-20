using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.SeerrHomeCarousel.Services;

/// <summary>
/// DTO representing a single carousel item returned to the Jellyfin web client.
/// </summary>
public class CarouselItemDto
{
    /// <summary>Gets or sets the TMDB numeric ID as string.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the display title.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the release year.</summary>
    public int? ProductionYear { get; set; }

    /// <summary>Gets or sets the ISO release date string.</summary>
    public string? PremiereDate { get; set; }

    /// <summary>Gets or sets provider IDs and poster paths.</summary>
    public Dictionary<string, string> ProviderIds { get; set; } = new();
}

/// <summary>
/// Fetches daily released Movies and TV Shows (from Seerr and/or TMDB) and proxies poster images.
/// </summary>
public class TmdbPosterService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TmdbPosterService> _logger;
    private readonly ConcurrentDictionary<string, string?> _posterPathCache = new();
    private readonly ConcurrentDictionary<string, (byte[] Bytes, string ContentType)> _imageCache = new();
    private readonly ConcurrentDictionary<string, (DateTime CreatedUtc, List<CarouselItemDto> Items)> _dailyCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbPosterService"/> class.
    /// </summary>
    public TmdbPosterService(IHttpClientFactory httpClientFactory, ILogger<TmdbPosterService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Returns a daily rotating list of already-released Movies or TV Shows (default 20).
    /// </summary>
    public async Task<List<CarouselItemDto>> GetDailyReleasedItemsAsync(
        string mediaType,
        string source,
        int targetCount,
        bool hideAvailable,
        CancellationToken cancellationToken)
    {
        string normalizedType = string.Equals(mediaType, "tv", StringComparison.OrdinalIgnoreCase) ? "tv" : "movie";
        int count = Math.Clamp(targetCount, 5, 50);
        DateTime todayUtc = DateTime.UtcNow.Date;
        string todayStr = todayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var (seerrUrl, seerrApiKey, seerrTmdbKey) = GetSeerrFinCredentials();
        string tmdbApiKey = GetEffectiveTmdbApiKey(seerrTmdbKey);

        string cacheKey = $"{todayStr}:{normalizedType}:{source}:{count}:{hideAvailable}:{(string.IsNullOrEmpty(tmdbApiKey) ? "0" : "1")}";
        if (_dailyCache.TryGetValue(cacheKey, out var cached) && (DateTime.UtcNow - cached.CreatedUtc) < TimeSpan.FromMinutes(30) && cached.Items.Count >= count)
        {
            return cached.Items;
        }

        int dayIndex = Math.Abs((int)(todayUtc - new DateTime(2025, 1, 1)).TotalDays);
        int page1 = (dayIndex % 5) + 1;
        int page2 = ((dayIndex + 2) % 5) + 1;
        int page3 = ((dayIndex * 3) % 8) + 1;

        var tasks = new List<Task<List<CarouselItemDto>>>();

        // 1. Direct Seerr API discovery (bypasses SeerrFin's restrictive originalLanguage filter)
        if (!string.IsNullOrEmpty(seerrUrl) && !string.IsNullOrEmpty(seerrApiKey))
        {
            string seerrEndpoint = normalizedType == "tv" ? "/api/v1/discover/tv" : "/api/v1/discover/movies";
            tasks.Add(FetchFromSeerrAsync(seerrUrl, seerrApiKey, $"{seerrEndpoint}?page={page1}", normalizedType, todayUtc, cancellationToken));
            tasks.Add(FetchFromSeerrAsync(seerrUrl, seerrApiKey, $"{seerrEndpoint}?page={page2}", normalizedType, todayUtc, cancellationToken));
            tasks.Add(FetchFromSeerrAsync(seerrUrl, seerrApiKey, $"/api/v1/discover/trending?page={page1}", normalizedType, todayUtc, cancellationToken));
            tasks.Add(FetchFromSeerrAsync(seerrUrl, seerrApiKey, $"/api/v1/discover/trending?page={page2}", normalizedType, cancellationToken: cancellationToken, todayUtc: todayUtc));
            if (page1 != 1)
            {
                tasks.Add(FetchFromSeerrAsync(seerrUrl, seerrApiKey, $"{seerrEndpoint}?page=1", normalizedType, todayUtc, cancellationToken));
            }
        }

        // 2. Direct TMDB Discover API (guarantees Italian localized titles/posters and strict release-date <= today filtering)
        if (!string.IsNullOrEmpty(tmdbApiKey))
        {
            string dateLteParam = normalizedType == "tv" ? $"first_air_date.lte={todayStr}" : $"primary_release_date.lte={todayStr}";
            string dateSort = normalizedType == "tv" ? "first_air_date.desc" : "primary_release_date.desc";
            string sortBy = source switch
            {
                "top-rated" => "vote_average.desc",
                _ => "popularity.desc"
            };
            int minVotes = source == "top-rated" ? 150 : 10;

            tasks.Add(FetchFromTmdbDiscoverAsync(tmdbApiKey, normalizedType, $"sort_by={sortBy}&{dateLteParam}&vote_count.gte={minVotes}&page={page1}", todayUtc, cancellationToken));
            tasks.Add(FetchFromTmdbDiscoverAsync(tmdbApiKey, normalizedType, $"sort_by={sortBy}&{dateLteParam}&vote_count.gte={minVotes}&page={page2}", todayUtc, cancellationToken));
            tasks.Add(FetchFromTmdbDiscoverAsync(tmdbApiKey, normalizedType, $"sort_by={dateSort}&{dateLteParam}&vote_count.gte=15&page={page3}", todayUtc, cancellationToken));
            if (page1 != 1)
            {
                tasks.Add(FetchFromTmdbDiscoverAsync(tmdbApiKey, normalizedType, $"sort_by=popularity.desc&{dateLteParam}&vote_count.gte=10&page=1", todayUtc, cancellationToken));
            }
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var mergedById = new Dictionary<string, CarouselItemDto>(StringComparer.Ordinal);
        foreach (var list in results)
        {
            foreach (var item in list)
            {
                if (string.IsNullOrEmpty(item.Id))
                {
                    continue;
                }

                if (!mergedById.TryGetValue(item.Id, out var existing))
                {
                    mergedById[item.Id] = item;
                }
                else
                {
                    // Merge Seerr status badge if one source came from Seerr and another from TMDB
                    if (
                        !existing.ProviderIds.ContainsKey("JellyseerrMediaStatus")
                        && item.ProviderIds.TryGetValue("JellyseerrMediaStatus", out string? status)
                    )
                    {
                        existing.ProviderIds["JellyseerrMediaStatus"] = status;
                    }
                }
            }
        }

        var filteredPool = mergedById.Values
            .Where(item =>
            {
                if (!hideAvailable)
                {
                    return true;
                }

                return !item.ProviderIds.TryGetValue("JellyseerrMediaStatus", out string? st) || st != "5";
            })
            .ToList();

        // Deterministic daily shuffle seeded by today's date + mediaType
        int seed = GetDeterministicSeed($"{todayStr}:{normalizedType}:{source}");
        var rng = new Random(seed);
        var shuffled = filteredPool.OrderBy(_ => rng.Next()).Take(count).ToList();

        if (shuffled.Count > 0)
        {
            if (_dailyCache.Count > 20)
            {
                _dailyCache.Clear();
            }

            _dailyCache[cacheKey] = (DateTime.UtcNow, shuffled);
        }

        return shuffled;
    }

    /// <summary>
    /// Downloads and caches the poster image bytes for a TMDB movie or TV show.
    /// </summary>
    public async Task<(byte[] Bytes, string ContentType)?> GetPosterAsync(
        string mediaType,
        int tmdbId,
        CancellationToken cancellationToken)
    {
        string normalizedType = string.Equals(mediaType, "tv", StringComparison.OrdinalIgnoreCase) ? "tv" : "movie";
        string cacheKey = $"{normalizedType}:{tmdbId}";

        if (_imageCache.TryGetValue(cacheKey, out var cachedImage))
        {
            return cachedImage;
        }

        var (_, _, seerrTmdbKey) = GetSeerrFinCredentials();
        string apiKey = GetEffectiveTmdbApiKey(seerrTmdbKey);

        string? posterPath = null;
        if (_posterPathCache.TryGetValue(cacheKey, out string? cachedPath) && !string.IsNullOrEmpty(cachedPath))
        {
            posterPath = cachedPath;
        }
        else if (!string.IsNullOrEmpty(apiKey))
        {
            posterPath = await ResolvePosterPathAsync(normalizedType, tmdbId, apiKey, cacheKey, cancellationToken)
                .ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(posterPath))
        {
            return null;
        }

        string normPath = posterPath.StartsWith('/') ? posterPath : "/" + posterPath;
        string imageUrl = $"https://image.tmdb.org/t/p/w500{normPath}";
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

    private async Task<List<CarouselItemDto>> FetchFromSeerrAsync(
        string seerrBaseUrl,
        string seerrApiKey,
        string relativePath,
        string expectedMediaType,
        DateTime todayUtc,
        CancellationToken cancellationToken)
    {
        var list = new List<CarouselItemDto>();
        try
        {
            string url = seerrBaseUrl.TrimEnd('/') + "/" + relativePath.TrimStart('/');
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", seerrApiKey);

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return list;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var root = JObject.Parse(json);
            var results = root["results"] as JArray;
            if (results == null)
            {
                return list;
            }

            foreach (var token in results.OfType<JObject>())
            {
                if (token.Value<bool?>("adult") == true)
                {
                    continue;
                }

                string? itemMediaType = token.Value<string>("mediaType");
                if (!string.IsNullOrEmpty(itemMediaType) && !string.Equals(itemMediaType, expectedMediaType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var dto = ParseCandidateItem(token, expectedMediaType, todayUtc);
                if (dto != null)
                {
                    list.Add(dto);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SeerrHomeCarousel • Seerr fetch failed for {Path}", relativePath);
        }

        return list;
    }

    private async Task<List<CarouselItemDto>> FetchFromTmdbDiscoverAsync(
        string apiKey,
        string mediaType,
        string queryParams,
        DateTime todayUtc,
        CancellationToken cancellationToken)
    {
        var list = new List<CarouselItemDto>();
        try
        {
            bool isBearer = apiKey.Contains('.', StringComparison.Ordinal);
            string url = $"https://api.themoviedb.org/3/discover/{mediaType}?language=it-IT&include_adult=false&{queryParams}";
            if (!isBearer)
            {
                url += $"&api_key={Uri.EscapeDataString(apiKey)}";
            }

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (isBearer)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return list;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var root = JObject.Parse(json);
            var results = root["results"] as JArray;
            if (results == null)
            {
                return list;
            }

            foreach (var token in results.OfType<JObject>())
            {
                if (token.Value<bool?>("adult") == true)
                {
                    continue;
                }

                var dto = ParseCandidateItem(token, mediaType, todayUtc);
                if (dto != null)
                {
                    list.Add(dto);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SeerrHomeCarousel • TMDB discover fetch failed for {MediaType}", mediaType);
        }

        return list;
    }

    private CarouselItemDto? ParseCandidateItem(JObject token, string mediaType, DateTime todayUtc)
    {
        int? id = token.Value<int?>("id");
        if (id == null || id <= 0)
        {
            return null;
        }

        string? dateStr = token.Value<string>("releaseDate")
            ?? token.Value<string>("release_date")
            ?? token.Value<string>("firstAirDate")
            ?? token.Value<string>("first_air_date");

        if (string.IsNullOrWhiteSpace(dateStr) || !DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime releaseDate))
        {
            return null;
        }

        // Strictly only already-released items (current date/year and earlier, never future)
        if (releaseDate.Date > todayUtc || releaseDate.Year > todayUtc.Year || releaseDate.Year < 1950)
        {
            return null;
        }

        string? posterPath = token.Value<string>("posterPath") ?? token.Value<string>("poster_path");
        string? backdropPath = token.Value<string>("backdropPath") ?? token.Value<string>("backdrop_path");
        if (string.IsNullOrWhiteSpace(posterPath) && string.IsNullOrWhiteSpace(backdropPath))
        {
            return null;
        }

        string? title = token.Value<string>("title")
            ?? token.Value<string>("name")
            ?? token.Value<string>("originalTitle")
            ?? token.Value<string>("original_title")
            ?? token.Value<string>("originalName")
            ?? token.Value<string>("original_name");

        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        string tmdbIdStr = id.Value.ToString(CultureInfo.InvariantCulture);
        string chosenPoster = !string.IsNullOrWhiteSpace(posterPath) ? posterPath : backdropPath!;
        _posterPathCache[$"{mediaType}:{tmdbIdStr}"] = chosenPoster;

        var providerIds = new Dictionary<string, string>
        {
            ["Tmdb"] = tmdbIdStr
        };

        if (!string.IsNullOrWhiteSpace(posterPath))
        {
            providerIds["TmdbPosterPath"] = posterPath;
        }

        if (!string.IsNullOrWhiteSpace(backdropPath))
        {
            providerIds["TmdbBackdropPath"] = backdropPath;
        }

        var mediaInfo = token["mediaInfo"] as JObject;
        int? status = mediaInfo?.Value<int?>("status");
        if (status == 6)
        {
            // Skip blocklisted items
            return null;
        }

        if (status != null && status > 1)
        {
            providerIds["JellyseerrMediaStatus"] = status.Value.ToString(CultureInfo.InvariantCulture);
        }

        return new CarouselItemDto
        {
            Id = tmdbIdStr,
            Name = title,
            ProductionYear = releaseDate.Year,
            PremiereDate = releaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ProviderIds = providerIds
        };
    }

    private static (string? SeerrUrl, string? SeerrApiKey, string? TmdbApiKey) GetSeerrFinCredentials()
    {
        try
        {
            var seerrFinAssembly = AssemblyLoadContext.All
                .SelectMany(x => x.Assemblies)
                .FirstOrDefault(x => x.FullName?.Contains("Jellyfin.Plugin.SeerrFin", StringComparison.OrdinalIgnoreCase) ?? false);

            Type? pluginType = seerrFinAssembly?.GetType("Jellyfin.Plugin.SeerrFin.SeerrFinPlugin");
            object? instance = pluginType?.GetProperty("Instance")?.GetValue(null);
            object? config = instance?.GetType().GetProperty("Configuration")?.GetValue(instance);
            if (config == null)
            {
                return (null, null, null);
            }

            Type cfgType = config.GetType();
            string? url = cfgType.GetProperty("JellyseerrUrl")?.GetValue(config) as string;
            string? key = cfgType.GetProperty("JellyseerrApiKey")?.GetValue(config) as string;
            string? tmdb = cfgType.GetProperty("TmdbApiKey")?.GetValue(config) as string;
            return (url?.Trim(), key?.Trim(), tmdb?.Trim());
        }
        catch
        {
            return (null, null, null);
        }
    }

    private static string GetEffectiveTmdbApiKey(string? seerrTmdbKey)
    {
        string ownKey = (SeerrHomeCarouselPlugin.Instance.Configuration.TmdbApiKey ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(ownKey))
        {
            return ownKey;
        }

        return seerrTmdbKey ?? string.Empty;
    }

    private static int GetDeterministicSeed(string text)
    {
        unchecked
        {
            int hash = (int)2166136261;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619;
            }

            return hash & int.MaxValue;
        }
    }

    private async Task<string?> ResolvePosterPathAsync(
        string mediaType,
        int tmdbId,
        string apiKey,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        if (_posterPathCache.TryGetValue(cacheKey, out string? cachedPath) && !string.IsNullOrEmpty(cachedPath))
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

        if (_posterPathCache.Count > 2000)
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
            return obj.Value<string>("poster_path") ?? obj.Value<string>("backdrop_path");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SeerrHomeCarousel • TMDB metadata lookup failed for {MediaType}/{TmdbId}", mediaType, tmdbId);
            return null;
        }
    }
}
