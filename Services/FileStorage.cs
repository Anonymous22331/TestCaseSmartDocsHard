using Microsoft.Extensions.Options;
using TestCaseSmartDocsHard.Models;

namespace TestCaseSmartDocsHard.Services;

public sealed class FileStorage(IOptions<ProcessingOptions> options, ILogger<FileStorage> logger)
{
    private readonly string root = Path.GetFullPath(options.Value.StoragePath);

    public string CreatePath(string extension)
    {
        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "input" + extension);
    }

    public void Delete(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is null || Path.GetDirectoryName(directory) != root
            || !Guid.TryParseExact(Path.GetFileName(directory), "N", out _))
            throw new InvalidOperationException("The job directory must be inside the storage root.");
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Unable to remove temporary job files ({ErrorType}).", exception.GetType().Name);
        }
    }
}
