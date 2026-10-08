using Metrics.Application.Auth;
using Metrics.Application.Common;
using Metrics.Domain;
using Metrics.Domain.Entities;
using Moq;

namespace Metrics.Tests.Auth;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<ITokenService> _tokens = new();
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => "hashed:" + p);
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((hash, p) => hash == "hashed:" + p);
        _tokens.Setup(t => t.Create(It.IsAny<User>()))
            .Returns(new TokenResult("tok", new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        _sut = new AuthService(_users.Object, _hasher.Object, _tokens.Object);
    }

    private static RegisterRequest Valid(string email = "A@Demo.local", Team team = Team.Technical) =>
        new(email, "longenough1", "  Alice  ", team);

    [Fact]
    public async Task Register_NormalizesEmail_TrimsName_AndStoresHashedPassword()
    {
        User? saved = null;
        _users.Setup(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => saved = u).Returns(Task.CompletedTask);

        var res = await _sut.RegisterAsync(Valid(), default);

        Assert.NotNull(saved);
        Assert.Equal("a@demo.local", saved!.Email);
        Assert.Equal("Alice", saved.Name);
        Assert.Equal("hashed:longenough1", saved.PasswordHash);
        Assert.Equal("tok", res.Token);
        Assert.Equal(Team.Technical, res.User.Team);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Throws409()
    {
        _users.Setup(u => u.FindByEmailAsync("a@demo.local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User());

        await Assert.ThrowsAsync<ConflictException>(() => _sut.RegisterAsync(Valid(), default));
        _users.Verify(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("", "longenough1", "Al", Team.Business, "email")]
    [InlineData("not-an-email", "longenough1", "Al", Team.Business, "email")]
    [InlineData("Bob <b@x.com>", "longenough1", "Al", Team.Business, "email")]
    [InlineData("a@x.com", "short", "Al", Team.Business, "password")]
    [InlineData("a@x.com", "longenough1", "  ", Team.Business, "name")]
    [InlineData("a@x.com", "longenough1", "Al", (Team)99, "team")]
    public async Task Register_InvalidInput_ThrowsValidationWithField(
        string email, string password, string name, Team team, string field)
    {
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(
            () => _sut.RegisterAsync(new RegisterRequest(email, password, name, team), default));

        Assert.Contains(field, ex.Errors.Keys);
    }

    [Fact]
    public async Task Login_Success_ReturnsToken()
    {
        _users.Setup(u => u.FindByEmailAsync("a@demo.local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Email = "a@demo.local", PasswordHash = "hashed:pw", Team = Team.Business });

        var res = await _sut.LoginAsync(new LoginRequest(" A@Demo.local ", "pw"), default);

        Assert.Equal("tok", res.Token);
        Assert.Equal(Team.Business, res.User.Team);
    }

    [Fact]
    public async Task Login_WrongPassword_And_UnknownEmail_FailIdentically()
    {
        _users.Setup(u => u.FindByEmailAsync("a@demo.local", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { PasswordHash = "hashed:pw" });
        _users.Setup(u => u.FindByEmailAsync("nobody@demo.local", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var wrongPw = await Assert.ThrowsAsync<UnauthorizedException>(
            () => _sut.LoginAsync(new LoginRequest("a@demo.local", "bad"), default));
        var unknown = await Assert.ThrowsAsync<UnauthorizedException>(
            () => _sut.LoginAsync(new LoginRequest("nobody@demo.local", "pw"), default));

        Assert.Equal(wrongPw.Message, unknown.Message);
    }

    [Fact]
    public async Task GetCurrent_UnknownUser_ThrowsUnauthorized()
    {
        _users.Setup(u => u.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _sut.GetCurrentAsync(Guid.NewGuid(), default));
    }
}
