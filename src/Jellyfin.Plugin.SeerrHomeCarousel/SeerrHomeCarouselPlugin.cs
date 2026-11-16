using Jellyfin.Plugin.SeerrHomeCarousel.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SeerrHomeCarousel;

/// <summary>
/// Main plugin entry point for SeerrHomeCarousel.
/// </summary>
public class SeerrHomeCarouselPlugin : BasePlugin<PluginConfiguration>, IHasPluginConfiguration, IHasWebPages
{
    /// <summary>
    /// Gets the unique plugin identifier.
    /// </summary>
    public override Guid Id => Guid.Parse("a4d9f1c2-7e3b-4b8a-9c5d-2e6f1a0b3c4d");

    /// <summary>
    /// Gets the plugin display name.
    /// </summary>
    public override string Name => "SeerrHomeCarousel";

    /// <summary>
    /// Gets the active plugin instance.
    /// </summary>
    public static SeerrHomeCarouselPlugin Instance { get; private set; } = null!;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeerrHomeCarouselPlugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Jellyfin application paths.</param>
    /// <param name="xmlSerializer">XML serializer for plugin configuration.</param>
    public SeerrHomeCarouselPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Returns the plugin configuration page.
    /// </summary>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        string? prefix = GetType().Namespace;
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{prefix}.Configuration.configPage.html",
            EnableInMainMenu = true,
            DisplayName = "SeerrHomeCarousel",
            MenuIcon = "view_carousel"
        };
    }
}
