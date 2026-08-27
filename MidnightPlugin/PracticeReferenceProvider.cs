using MidnightPlugin.Core;

namespace MidnightPlugin;

public sealed record PracticeReferenceEntry(
    string Job,
    string FilePath,
    bool IsUserProvided,
    PracticeReferenceRotation Rotation);

public sealed class PracticeReferenceProvider
{
    private readonly string bundledDirectory;
    private readonly Dictionary<string, PracticeReferenceEntry> references = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> errors = [];

    public PracticeReferenceProvider(string assemblyDirectory, string configDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);

        bundledDirectory = Path.Combine(assemblyDirectory, "References");
        UserDirectory = Path.Combine(configDirectory, "references");
        Reload();
    }

    public string UserDirectory { get; }
    public IReadOnlyCollection<PracticeReferenceEntry> References => references.Values.ToArray();
    public IReadOnlyList<string> Errors => errors.ToArray();

    public void Reload()
    {
        references.Clear();
        errors.Clear();

        TryLoadDirectory(bundledDirectory, isUserProvided: false, "bundled reference folder");
        try
        {
            Directory.CreateDirectory(UserDirectory);
            TryLoadDirectory(UserDirectory, isUserProvided: true, "user reference folder");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Could not access the user reference folder: {exception.Message}");
        }
    }

    private void TryLoadDirectory(string directory, bool isUserProvided, string label)
    {
        try
        {
            LoadDirectory(directory, isUserProvided);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Could not access the {label}: {exception.Message}");
        }
    }

    public PracticeReferenceLoadResult GetForJob(string? job)
    {
        var normalizedJob = job?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedJob))
        {
            return new(null, "The current job is unavailable.");
        }

        return references.TryGetValue(normalizedJob, out var entry)
            ? new(entry.Rotation, null)
            : new(null, $"No practice reference is installed for {normalizedJob.ToUpperInvariant()}.");
    }

    private void LoadDirectory(string directory, bool isUserProvided)
    {
        if (!Directory.Exists(directory)) return;

        foreach (var filePath in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            PracticeReferenceLoadResult result;
            try
            {
                result = PracticeReferenceCatalog.Load(File.ReadAllText(filePath));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(filePath)}: {exception.Message}");
                continue;
            }

            if (!result.IsValid)
            {
                errors.Add($"{Path.GetFileName(filePath)}: {result.Error}");
                continue;
            }

            var rotation = result.Rotation!;
            var entry = new PracticeReferenceEntry(rotation.Job, filePath, isUserProvided, rotation);
            if (references.TryGetValue(rotation.Job, out var existing) && existing.IsUserProvided == isUserProvided)
            {
                errors.Add(
                    $"{Path.GetFileName(filePath)}: duplicate {rotation.Job} reference; " +
                    $"using {Path.GetFileName(existing.FilePath)}.");
                continue;
            }

            // User files intentionally override the bundled reference for the same job.
            references[rotation.Job] = entry;
        }
    }
}
