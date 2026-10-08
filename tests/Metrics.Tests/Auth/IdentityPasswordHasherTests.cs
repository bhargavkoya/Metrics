using Metrics.Infrastructure.Auth;

namespace Metrics.Tests.Auth;

public class IdentityPasswordHasherTests
{
    private readonly IdentityPasswordHasher _sut = new();

    [Fact]
    public void Hash_IsSalted_AndNotPlaintext()
    {
        var a = _sut.Hash("secret-pw-1");
        var b = _sut.Hash("secret-pw-1");

        Assert.NotEqual(a, b);
        Assert.DoesNotContain("secret-pw-1", a);
    }

    [Fact]
    public void Verify_AcceptsCorrect_RejectsWrong()
    {
        var hash = _sut.Hash("secret-pw-1");

        Assert.True(_sut.Verify(hash, "secret-pw-1"));
        Assert.False(_sut.Verify(hash, "secret-pw-2"));
    }
}
