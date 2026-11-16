namespace Jellyfin.Plugin.SeerrHomeCarousel.Model;

/// <summary>
/// Payload passed by the File Transformation plugin to patch callbacks.
/// </summary>
public class PatchRequestPayload
{
    /// <summary>
    /// Gets or sets the original file contents.
    /// </summary>
    public string? Contents { get; set; }
}
