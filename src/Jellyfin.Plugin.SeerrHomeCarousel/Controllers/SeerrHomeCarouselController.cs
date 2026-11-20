using System.Reflection;
using System.Text;
using Jellyfin.Plugin.SeerrHomeCarousel.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SeerrHomeCarousel.Controllers;

/// <summary>
/// Serves injected frontend assets, TMDB posters, and client configuration for SeerrHomeCarousel.
/// </summary>
[ApiController]
[Route("[controller]")]
public class SeerrHomeCarouselController : ControllerBase
{
    private static readonly Assembly PluginAssembly = typeof(SeerrHomeCarouselPlugin).Assembly;
    private static readonly string ResourcePrefix = $"{typeof(SeerrHomeCarouselPlugin).Namespace}.Inject.";
    private readonly TmdbPosterService _tmdbPosterService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeerrHomeCarouselController"/> class.
    /// </summary>
    /// <param name="tmdbPosterService">TMDB poster service.</param>
    public SeerrHomeCarouselController(TmdbPosterService tmdbPosterService)
    {
        _tmdbPosterService = tmdbPosterService;
    }

    /// <summary>
    /// Serves the injected carousel JavaScript.
    /// </summary>
    [HttpGet("carousel.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    public ActionResult GetCarouselJs() => ServeEmbeddedResource("carousel.js", "application/javascript; charset=utf-8");

    /// <summary>
    /// Serves the injected carousel stylesheet.
    /// </summary>
    [HttpGet("carousel.css")]
    [AllowAnonymous]
    [Produces("text/css")]
    public ActionResult GetCarouselCss() => ServeEmbeddedResource("carousel.css", "text/css; charset=utf-8");

    /// <summary>
    /// Returns the daily rotating list of already-released Movies or TV Shows (default 20).
    /// </summary>
    [HttpGet("daily/{mediaType}")]
    [Authorize]
    public async Task<ActionResult> GetDailyReleasedItems(string mediaType, CancellationToken cancellationToken)
    {
        var cfg = SeerrHomeCarouselPlugin.Instance.Configuration;
        bool isTv = string.Equals(mediaType, "tv", StringComparison.OrdinalIgnoreCase);
        string source = NormalizeSource(isTv ? cfg.TvSource : cfg.MoviesSource);
        int count = Math.Clamp(cfg.MaxItems, 5, 50);

        var items = await _tmdbPosterService
            .GetDailyReleasedItemsAsync(isTv ? "tv" : "movie", source, count, cfg.HideAvailable, cancellationToken)
            .ConfigureAwait(false);

        return Ok(new
        {
            Items = items,
            TotalRecordCount = items.Count
        });
    }

    /// <summary>
    /// Proxies and caches poster images from TMDB using the configured API key/token.
    /// </summary>
    [HttpGet("poster/{mediaType}/{tmdbId:int}")]
    [AllowAnonymous]
    public async Task<ActionResult> GetTmdbPoster(string mediaType, int tmdbId, CancellationToken cancellationToken)
    {
        var result = await _tmdbPosterService.GetPosterAsync(mediaType, tmdbId, cancellationToken).ConfigureAwait(false);
        if (result == null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=86400";
        return File(result.Value.Bytes, result.Value.ContentType);
    }

    /// <summary>
    /// Returns the active carousel configuration for the web client.
    /// </summary>
    [HttpGet("client-config")]
    [Authorize]
    public ActionResult GetClientConfig()
    {
        var cfg = SeerrHomeCarouselPlugin.Instance.Configuration;
        return Ok(new
        {
            enableMoviesCarousel = cfg.EnableMoviesCarousel,
            moviesTitle = string.IsNullOrWhiteSpace(cfg.MoviesTitle) ? "Nuovi Film su Seerr" : cfg.MoviesTitle,
            moviesSource = NormalizeSource(cfg.MoviesSource),
            enableTvCarousel = cfg.EnableTvCarousel,
            tvTitle = string.IsNullOrWhiteSpace(cfg.TvTitle) ? "Nuove Serie TV su Seerr" : cfg.TvTitle,
            tvSource = NormalizeSource(cfg.TvSource),
            maxItems = Math.Clamp(cfg.MaxItems, 5, 50),
            homePosition = NormalizePosition(cfg.HomePosition),
            hideAvailable = cfg.HideAvailable,
            hasTmdbKey = !string.IsNullOrWhiteSpace(cfg.TmdbApiKey)
        });
    }

    private ActionResult ServeEmbeddedResource(string fileName, string contentType)
    {
        using Stream? stream = PluginAssembly.GetManifestResourceStream(ResourcePrefix + fileName);
        if (stream == null)
        {
            return NotFound();
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Content(reader.ReadToEnd(), contentType);
    }

    private static string NormalizeSource(string? source) => source switch
    {
        "daily" or "trending" or "popular" or "top-rated" => source,
        _ => "daily"
    };

    private static string NormalizePosition(string? position) => position switch
    {
        "top" or "afterMyMedia" or "bottom" => position,
        _ => "afterMyMedia"
    };
}
