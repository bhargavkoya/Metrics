using Metrics.Application.Common;
using Metrics.Application.Documents;
using Metrics.Domain.Entities;
using Moq;

namespace Metrics.Tests.Documents;

public class DocumentServiceTests
{
    private readonly Mock<IDocumentRepository> _repo = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly DocumentService _sut;
    private static readonly Guid AutomationId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2030, 5, 1, 9, 0, 0, TimeSpan.Zero);

    public DocumentServiceTests()
    {
        _repo.Setup(r => r.AutomationExistsAsync(AutomationId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.GetDtoAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new DocumentDto(id, "x", "application/pdf", 1, "Alice", Now.UtcDateTime));
        _sut = new DocumentService(_repo.Object, _storage.Object, new FixedClock(Now));
    }

    private static MemoryStream Pdf(int extraBytes = 100)
    {
        var bytes = new byte[extraBytes + 5];
        new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }.CopyTo(bytes, 0);
        return new MemoryStream(bytes);
    }

    [Fact]
    public async Task Upload_Success_StoresUnderGuidName_NotClientName_AndTouchesActivity()
    {
        AutomationDocument? saved = null;
        DateTime? activity = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<AutomationDocument>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<AutomationDocument, DateTime, CancellationToken>((d, a, _) => { saved = d; activity = a; })
            .Returns(Task.CompletedTask);

        await _sut.UploadAsync(AutomationId, UserId, @"C:\x\..\Design Doc.PDF", Pdf(), default);

        Assert.NotNull(saved);
        Assert.Equal("Design Doc.PDF", saved!.OriginalFileName);
        Assert.Matches(@"^[0-9a-f]{32}\.pdf$", saved.StoredFileName);
        Assert.Equal("application/pdf", saved.ContentType);
        Assert.Equal(105, saved.SizeBytes);
        Assert.Equal(UserId, saved.UploadedBy);
        Assert.Equal(Now.UtcDateTime, activity);
        _storage.Verify(s => s.SaveAsync(saved.StoredFileName, It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Upload_UnknownAutomation_ThrowsNotFound_WithoutTouchingStorage()
    {
        _repo.Setup(r => r.AutomationExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.UploadAsync(Guid.NewGuid(), UserId, "a.pdf", Pdf(), default));
        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Upload_BadExtension_Rejected_NothingStored()
    {
        await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.UploadAsync(AutomationId, UserId, "a.exe", Pdf(), default));
        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Upload_SignatureMismatch_Rejected_NothingStored()
    {
        var notPdf = new MemoryStream([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00]);

        await Assert.ThrowsAsync<ValidationFailedException>(() => _sut.UploadAsync(AutomationId, UserId, "a.pdf", notPdf, default));
        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Upload_ExactlyAtLimit_Succeeds_OneByteOver_Rejected()
    {
        _repo.Setup(r => r.AddAsync(It.IsAny<AutomationDocument>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _sut.UploadAsync(AutomationId, UserId, "ok.pdf", Pdf((int)FileValidator.MaxBytes - 5), default);

        await Assert.ThrowsAsync<ValidationFailedException>(
            () => _sut.UploadAsync(AutomationId, UserId, "big.pdf", Pdf((int)FileValidator.MaxBytes - 4), default));
    }

    [Fact]
    public async Task Upload_EmptyFile_Rejected()
    {
        await Assert.ThrowsAsync<ValidationFailedException>(
            () => _sut.UploadAsync(AutomationId, UserId, "e.pdf", new MemoryStream(), default));
    }

    [Fact]
    public async Task Upload_WhenDbInsertFails_DeletesStoredBlob_AndRethrows()
    {
        string? stored = null;
        _storage.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<string, Stream, CancellationToken>((n, _, _) => stored = n).Returns(Task.CompletedTask);
        _repo.Setup(r => r.AddAsync(It.IsAny<AutomationDocument>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.UploadAsync(AutomationId, UserId, "a.pdf", Pdf(), default));

        _storage.Verify(s => s.DeleteAsync(stored!, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Download_DocumentOfAnotherAutomation_IsNotFound()
    {
        // The repository scopes the lookup by (automationId, documentId); a mismatch yields null.
        _repo.Setup(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AutomationDocument?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.OpenAsync(Guid.NewGuid(), Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Download_BlobMissingOnDisk_IsNotFound()
    {
        var doc = new AutomationDocument { StoredFileName = "0123456789abcdef0123456789abcdef.pdf", OriginalFileName = "a.pdf" };
        _repo.Setup(r => r.FindAsync(AutomationId, doc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        _storage.Setup(s => s.OpenReadAsync(doc.StoredFileName, It.IsAny<CancellationToken>())).ReturnsAsync((Stream?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.OpenAsync(AutomationId, doc.Id, default));
    }

    [Fact]
    public async Task Delete_RemovesRowAndBlob_AndTouchesActivity()
    {
        var doc = new AutomationDocument { Id = Guid.NewGuid(), AutomationId = AutomationId, StoredFileName = "0123456789abcdef0123456789abcdef.pdf" };
        _repo.Setup(r => r.FindAsync(AutomationId, doc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        await _sut.DeleteAsync(AutomationId, doc.Id, default);

        _repo.Verify(r => r.RemoveAsync(doc, Now.UtcDateTime, It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync(doc.StoredFileName, It.IsAny<CancellationToken>()), Times.Once);
    }
}
