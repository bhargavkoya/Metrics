using Metrics.Application.Common;

namespace Metrics.Application.Documents;

/// <summary>
/// Pure upload rules: extension allowlist, size cap, magic-byte check and filename sanitising.
/// The client's filename and content type are never trusted for any decision or path.
/// </summary>
public static class FileValidator
{
    public const long MaxBytes = 10 * 1024 * 1024;
    public const int MaxNameLength = 260;
    public const int HeaderLength = 8;

    private static readonly Dictionary<string, (string ContentType, byte[][] Signatures)> Allowed =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = ("application/pdf", [[0x25, 0x50, 0x44, 0x46, 0x2D]]),
            [".png"] = ("image/png", [[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]]),
            [".jpg"] = ("image/jpeg", [[0xFF, 0xD8, 0xFF]]),
            [".jpeg"] = ("image/jpeg", [[0xFF, 0xD8, 0xFF]]),
            // OOXML files are ZIP containers; a deeper inspection is out of scope for the POC.
            [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", [[0x50, 0x4B, 0x03, 0x04]]),
            [".xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", [[0x50, 0x4B, 0x03, 0x04]]),
        };

    public static IReadOnlyCollection<string> AllowedExtensions => Allowed.Keys;

    /// <summary>Returns the display name (path stripped, control chars removed) and the lowercase extension.</summary>
    public static (string DisplayName, string Extension) ValidateName(string? fileName)
    {
        var name = Sanitize(fileName);
        var ext = Path.GetExtension(name).ToLowerInvariant();

        if (string.IsNullOrEmpty(Path.GetFileNameWithoutExtension(name)) || !Allowed.ContainsKey(ext))
            throw new ValidationFailedException("file",
                $"Unsupported file type. Allowed: {string.Join(", ", Allowed.Keys.Select(e => e.TrimStart('.').ToUpperInvariant()).Distinct())}.");

        return (name, ext);
    }

    public static string ContentTypeFor(string extension) => Allowed[extension.ToLowerInvariant()].ContentType;

    public static void ValidateSize(long length)
    {
        if (length == 0) throw new ValidationFailedException("file", "The file is empty.");
        if (length > MaxBytes) throw new ValidationFailedException("file", "The file exceeds the 10 MB limit.");
    }

    public static void ValidateSignature(string extension, ReadOnlySpan<byte> header)
    {
        foreach (var sig in Allowed[extension.ToLowerInvariant()].Signatures)
            if (header.Length >= sig.Length && header[..sig.Length].SequenceEqual(sig)) return;

        throw new ValidationFailedException("file", "The file content does not match its extension.");
    }

    private static string Sanitize(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "";

        // Handle both separators regardless of host OS, since browsers on Windows may send full paths.
        var name = fileName[(fileName.LastIndexOfAny(['/', '\\']) + 1)..];
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();

        if (name.Length <= MaxNameLength) return name;
        var ext = Path.GetExtension(name);
        return name[..(MaxNameLength - ext.Length)] + ext;
    }
}
