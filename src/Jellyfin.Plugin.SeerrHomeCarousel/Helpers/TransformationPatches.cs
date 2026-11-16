using Jellyfin.Plugin.SeerrHomeCarousel.Model;

namespace Jellyfin.Plugin.SeerrHomeCarousel.Helpers;

/// <summary>
/// File Transformation callbacks for injecting SeerrHomeCarousel assets into Jellyfin Web.
/// </summary>
public static class TransformationPatches
{
    private const string Marker = "<!-- SeerrHomeCarousel -->";

    /// <summary>
    /// Injects SeerrHomeCarousel stylesheet and script into index.html.
    /// </summary>
    /// <param name="payload">The transformation payload containing index.html contents.</param>
    /// <returns>The patched index.html contents.</returns>
    public static string IndexHtml(PatchRequestPayload payload)
    {
        string contents = payload.Contents ?? string.Empty;
        if (string.IsNullOrEmpty(contents) || contents.Contains(Marker, StringComparison.Ordinal))
        {
            return contents;
        }

        string version = SeerrHomeCarouselPlugin.Instance.GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0.0";
        string injection =
            $"{Marker}"
            + $"<link rel=\"stylesheet\" href=\"../SeerrHomeCarousel/carousel.css?v={version}\" />"
            + $"<script defer src=\"../SeerrHomeCarousel/carousel.js?v={version}\"></script>";

        int bodyEndIndex = contents.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyEndIndex >= 0)
        {
            return contents.Insert(bodyEndIndex, injection);
        }

        return contents + injection;
    }
}
