using System.Runtime.Loader;
using Jellyfin.Plugin.SeerrHomeCarousel.Helpers;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.SeerrHomeCarousel.Services;

/// <summary>
/// Registers File Transformation patches when Jellyfin starts.
/// </summary>
public class StartupService : IScheduledTask
{
    private static readonly Guid IndexHtmlTransformationId = Guid.Parse("d1e2f3a4-5b6c-7d8e-9f0a-1b2c3d4e5f60");

    private readonly ILogger<SeerrHomeCarouselPlugin> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartupService"/> class.
    /// </summary>
    /// <param name="logger">Plugin logger.</param>
    public StartupService(ILogger<SeerrHomeCarouselPlugin> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "SeerrHomeCarousel Startup";

    /// <inheritdoc />
    public string Key => "Jellyfin.Plugin.SeerrHomeCarousel.Startup";

    /// <inheritdoc />
    public string Description => "Registers File Transformation patches for SeerrHomeCarousel.";

    /// <inheritdoc />
    public string Category => "Startup Services";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var fileTransformationAssembly = AssemblyLoadContext.All
            .SelectMany(x => x.Assemblies)
            .FirstOrDefault(x => x.FullName?.Contains(".FileTransformation", StringComparison.Ordinal) ?? false);

        if (fileTransformationAssembly == null)
        {
            _logger.LogWarning("SeerrHomeCarousel • File Transformation plugin not found. UI injection will not work.");
            return Task.CompletedTask;
        }

        Type? pluginInterfaceType = fileTransformationAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
        var registerTransformation = pluginInterfaceType?.GetMethod("RegisterTransformation");
        if (registerTransformation == null)
        {
            _logger.LogWarning("SeerrHomeCarousel • File Transformation RegisterTransformation method not found.");
            return Task.CompletedTask;
        }

        var payload = new JObject
        {
            ["id"] = IndexHtmlTransformationId,
            ["fileNamePattern"] = "index.html",
            ["callbackAssembly"] = GetType().Assembly.FullName,
            ["callbackClass"] = typeof(TransformationPatches).FullName,
            ["callbackMethod"] = nameof(TransformationPatches.IndexHtml)
        };

        registerTransformation.Invoke(null, new object?[] { payload });
        _logger.LogInformation("SeerrHomeCarousel • Registered index.html transformation.");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.StartupTrigger
        };
    }
}
