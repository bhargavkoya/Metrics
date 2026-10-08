using System.Text;
using Metrics.Infrastructure.Documents;
using Microsoft.Extensions.Options;

namespace Metrics.Tests.Documents;

public class LocalDiskFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "metrics-storage-" + Guid.NewGuid().ToString("N"));
    private readonly LocalDiskFileStorage _sut;

    public LocalDiskFileStorageTests()
    {
        _sut = new LocalDiskFileStorage(Options.Create(new StorageOptions { UploadsPath = _root }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static string NewName(string ext = ".pdf") => $"{Guid.NewGuid():N}{ext}";
    private static MemoryStream Bytes(string s) => new(Encoding.UTF8.GetBytes(s));

    [Fact]
    public async Task SaveThenOpen_RoundTrips()
    {
        var name = NewName();
        await _sut.SaveAsync(name, Bytes("hello"), default);

        await using var s = await _sut.OpenReadAsync(name, default);
        using var reader = new StreamReader(s!);

        Assert.Equal("hello", await reader.ReadToEndAsync());
        Assert.True(File.Exists(Path.Combine(_root, name)));
    }

    [Fact]
    public async Task Save_ExistingName_DoesNotOverwrite()
    {
        var name = NewName();
        await _sut.SaveAsync(name, Bytes("first"), default);

        await Assert.ThrowsAsync<IOException>(() => _sut.SaveAsync(name, Bytes("second"), default));

        await using var s = await _sut.OpenReadAsync(name, default);
        using var reader = new StreamReader(s!);
        Assert.Equal("first", await reader.ReadToEndAsync());
    }

    [Theory]
    [InlineData("../evil.pdf")]
    [InlineData("..\\evil.pdf")]
    [InlineData("sub/0123456789abcdef0123456789abcdef.pdf")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("report.pdf")]                                  // client-style name, not a generated one
    [InlineData("0123456789abcdef0123456789abcdef.exe")]        // extension not allowlisted
    [InlineData("0123456789ABCDEF0123456789ABCDEF.pdf")]        // wrong case; generated names are lowercase
    [InlineData("")]
    public async Task InvalidStoredNames_AreRejected_ForEveryOperation(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.SaveAsync(name, Bytes("x"), default));
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.OpenReadAsync(name, default));
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.DeleteAsync(name, default));
    }

    [Fact]
    public async Task Open_Missing_ReturnsNull()
    {
        Assert.Null(await _sut.OpenReadAsync(NewName(), default));
    }

    [Fact]
    public async Task Delete_RemovesFile_AndMissingIsNoOp()
    {
        var name = NewName();
        await _sut.SaveAsync(name, Bytes("x"), default);

        await _sut.DeleteAsync(name, default);
        await _sut.DeleteAsync(name, default);

        Assert.False(File.Exists(Path.Combine(_root, name)));
    }
}
