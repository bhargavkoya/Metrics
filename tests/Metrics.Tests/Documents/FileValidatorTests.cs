using Metrics.Application.Common;
using Metrics.Application.Documents;

namespace Metrics.Tests.Documents;

public class FileValidatorTests
{
    [Theory]
    [InlineData("design.pdf", ".pdf")]
    [InlineData("Spec.DOCX", ".docx")]
    [InlineData("numbers.xlsx", ".xlsx")]
    [InlineData("shot.PNG", ".png")]
    [InlineData("photo.jpg", ".jpg")]
    [InlineData("photo.jpeg", ".jpeg")]
    [InlineData("my.report.v2.pdf", ".pdf")]
    public void AllowedNames_AreAccepted(string name, string ext)
    {
        var (display, e) = FileValidator.ValidateName(name);

        Assert.Equal(name, display);
        Assert.Equal(ext, e);
    }

    [Theory]
    [InlineData("malware.exe")]
    [InlineData("script.js")]
    [InlineData("page.html")]
    [InlineData("archive.pdf.exe")]
    [InlineData("noextension")]
    [InlineData(".pdf")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void DisallowedNames_AreRejected(string? name)
    {
        Assert.Throws<ValidationFailedException>(() => FileValidator.ValidateName(name));
    }

    [Theory]
    [InlineData(@"..\..\windows\evil.pdf", "evil.pdf")]
    [InlineData("../../etc/passwd.pdf", "passwd.pdf")]
    [InlineData(@"C:\Users\bob\Desktop\notes.pdf", "notes.pdf")]
    [InlineData("/var/tmp/x.png", "x.png")]
    public void PathComponents_AreStripped(string input, string expected)
    {
        Assert.Equal(expected, FileValidator.ValidateName(input).DisplayName);
    }

    [Fact]
    public void ControlCharacters_AreRemoved()
    {
        Assert.Equal("ab.pdf", FileValidator.ValidateName("a\r\nb\0.pdf").DisplayName);
    }

    [Fact]
    public void LongNames_AreTruncated_KeepingExtension()
    {
        var (display, ext) = FileValidator.ValidateName(new string('a', 400) + ".pdf");

        Assert.Equal(FileValidator.MaxNameLength, display.Length);
        Assert.EndsWith(".pdf", display);
        Assert.Equal(".pdf", ext);
    }

    [Fact]
    public void Size_BoundaryIsInclusive()
    {
        FileValidator.ValidateSize(FileValidator.MaxBytes);
        Assert.Throws<ValidationFailedException>(() => FileValidator.ValidateSize(FileValidator.MaxBytes + 1));
        Assert.Throws<ValidationFailedException>(() => FileValidator.ValidateSize(0));
    }

    [Theory]
    [InlineData(".pdf", new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 })]
    [InlineData(".png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })]
    [InlineData(".jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })]
    [InlineData(".jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 })]
    [InlineData(".docx", new byte[] { 0x50, 0x4B, 0x03, 0x04 })]
    [InlineData(".xlsx", new byte[] { 0x50, 0x4B, 0x03, 0x04 })]
    public void MatchingSignature_IsAccepted(string ext, byte[] header)
    {
        FileValidator.ValidateSignature(ext, header);
    }

    [Theory]
    [InlineData(".pdf", new byte[] { 0x4D, 0x5A, 0x90, 0x00 })] // Windows executable header renamed to .pdf
    [InlineData(".png", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })] // JPEG named .png
    [InlineData(".docx", new byte[] { 0x25, 0x50, 0x44, 0x46 })] // PDF named .docx
    [InlineData(".pdf", new byte[] { 0x25, 0x50 })]               // truncated
    [InlineData(".pdf", new byte[0])]
    public void MismatchedSignature_IsRejected(string ext, byte[] header)
    {
        Assert.Throws<ValidationFailedException>(() => FileValidator.ValidateSignature(ext, header));
    }

    [Fact]
    public void ContentType_ComesFromExtension_NotClient()
    {
        Assert.Equal("application/pdf", FileValidator.ContentTypeFor(".PDF"));
        Assert.Equal("image/jpeg", FileValidator.ContentTypeFor(".jpeg"));
    }
}
