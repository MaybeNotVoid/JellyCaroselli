using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SeerrHomeCarousel.Configuration;

/// <summary>
/// Configuration for SeerrHomeCarousel.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the New Movies carousel is enabled.
    /// </summary>
    public bool EnableMoviesCarousel { get; set; } = true;

    /// <summary>
    /// Gets or sets the title of the Movies carousel.
    /// </summary>
    public string MoviesTitle { get; set; } = "Nuovi Film su Seerr";

    /// <summary>
    /// Gets or sets the SeerrFin discovery feed for Movies (upcoming, trending, popular, top-rated).
    /// </summary>
    public string MoviesSource { get; set; } = "upcoming";

    /// <summary>
    /// Gets or sets a value indicating whether the New TV Shows carousel is enabled.
    /// </summary>
    public bool EnableTvCarousel { get; set; } = true;

    /// <summary>
    /// Gets or sets the title of the TV Shows carousel.
    /// </summary>
    public string TvTitle { get; set; } = "Nuove Serie TV su Seerr";

    /// <summary>
    /// Gets or sets the SeerrFin discovery feed for TV Shows (upcoming, trending, popular, top-rated).
    /// </summary>
    public string TvSource { get; set; } = "upcoming";

    /// <summary>
    /// Gets or sets the maximum number of items per carousel.
    /// </summary>
    public int MaxItems { get; set; } = 20;

    /// <summary>
    /// Gets or sets where the carousels appear on the Home screen (top, afterMyMedia, bottom).
    /// </summary>
    public string HomePosition { get; set; } = "afterMyMedia";

    /// <summary>
    /// Gets or sets a value indicating whether items already available in Jellyfin should be hidden.
    /// </summary>
    public bool HideAvailable { get; set; } = false;
}
