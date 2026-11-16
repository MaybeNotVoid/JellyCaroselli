using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SeerrHomeCarousel.Controllers;

/// <summary>
/// Serves injected frontend assets and client configuration for SeerrHomeCarousel.
/// </summary>
[ApiController]
[Route("[controller]")]
public class SeerrHomeCarouselController : ControllerBase
{
    private static readonly Assembly PluginAssembly = typeof(SeerrHomeCarouselPlugin).Assembly;
    private static readonly string ResourcePrefix = $"{typeof(SeerrHomeCarouselPlugin).Namespace}.Inject.";

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
            hideAvailable = cfg.HideAvailable
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
        "trending" or "popular" or "top-rated" or "upcoming" => source,
        _ => "upcoming"
    };

    private static string NormalizePosition(string? position) => position switch
    {
        "top" or "afterMyMedia" or "bottom" => position,
        _ => "afterMyMedia"
    };
}
