using System.Text.RegularExpressions;
using Metrics.Application.Documents;
using Microsoft.Extensions.Options;

namespace Metrics.Infrastructure.Documents;

public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Relative values are resolved against the API content root at startup.</summary>
    public string UploadsPath { get; set; } = "uploads";
}

/// <summary>
/// Stores blobs flat under one root, named only by a server-generated "{guid}{ext}".
/// Callers pass a stored name, never a path; both the name pattern and the resolved path are checked.
/// </summary>
public partial class LocalDiskFileStorage : IFileStorage
{
    // 32 hex chars (Guid "N") plus an allowlisted extension; anything else is rejected before touching disk.
    [GeneratedRegex(@"^[0-9a-f]{32}\.(pdf|docx|xlsx|png|jpg|jpeg)$", RegexOptions.CultureInvariant)]
    private static partial Regex StoredNamePattern();

    private readonly string _root;

    public LocalDiskFileStorage(IOptions<StorageOptions> options)
    {
        _root = Path.GetFullPath(options.Value.UploadsPath);
        Directory.CreateDirectory(_root);
    }

    public async Task SaveAsync(string storedName, Stream content, CancellationToken ct)
    {
        var path = Resolve(storedName);

        // CreateNew: never overwrite an existing blob. If this throws, nothing of ours exists yet, so no cleanup.
        await using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        try
        {
            await content.CopyToAsync(fs, ct);
        }
        catch
        {
            // Remove only the partial file this call created.
            await fs.DisposeAsync();
            TryDelete(path);
            throw;
        }
    }

    public Task<Stream?> OpenReadAsync(string storedName, CancellationToken ct)
    {
        var path = Resolve(storedName);
        if (!File.Exists(path)) return Task.FromResult<Stream?>(null);
        Stream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult<Stream?>(s);
    }

    public Task DeleteAsync(string storedName, CancellationToken ct)
    {
        TryDelete(Resolve(storedName));
        return Task.CompletedTask;
    }

    private string Resolve(string storedName)
    {
        if (!StoredNamePattern().IsMatch(storedName))
            throw new ArgumentException("Invalid stored file name.", nameof(storedName));

        var full = Path.GetFullPath(Path.Combine(_root, storedName));
        // Defense in depth: the resolved path must still live directly under the root.
        if (!string.Equals(Path.GetDirectoryName(full), _root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Resolved path escapes the uploads root.", nameof(storedName));
        return full;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* best effort */ }
    }
}
