using System.IdentityModel.Tokens.Jwt;
using Metrics.Domain;
using Metrics.Domain.Entities;
using Metrics.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Metrics.Tests.Auth;

public class JwtTokenServiceTests
{
    private static readonly JwtOptions Opts = new()
    {
        Key = "unit-test-key-that-is-long-enough-for-hs256-0123456789",
        Issuer = "iss",
        Audience = "aud",
        ExpiryHours = 2
    };

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static TokenValidationParameters Validation(string key) => new()
    {
        ValidIssuer = "iss",
        ValidAudience = "aud",
        IssuerSigningKey = JwtTokenService.SigningKey(new JwtOptions { Key = key }),
        ValidateLifetime = false
    };

    private static User Alice() => new() { Id = Guid.NewGuid(), Email = "a@x.com", Name = "Alice", Team = Team.Technical };

    [Fact]
    public void Create_IncludesExpectedClaims_AndExpiry()
    {
        var user = Alice();
        var res = new JwtTokenService(Options.Create(Opts), new FixedClock(Now)).Create(user);

        var jwt = new JwtSecurityTokenHandler { MapInboundClaims = false }.ReadJwtToken(res.Token);
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal("Technical", jwt.Claims.Single(c => c.Type == "team").Value);
        Assert.Equal("a@x.com", jwt.Claims.Single(c => c.Type == "email").Value);
        Assert.Equal(Now.UtcDateTime.AddHours(2), res.ExpiresAt);
        Assert.Equal(res.ExpiresAt, jwt.ValidTo);
    }

    [Fact]
    public void Token_ValidatesWithSameKey()
    {
        var res = new JwtTokenService(Options.Create(Opts), new FixedClock(Now)).Create(Alice());

        new JwtSecurityTokenHandler { MapInboundClaims = false }
            .ValidateToken(res.Token, Validation(Opts.Key), out var validated);

        Assert.NotNull(validated);
    }

    [Fact]
    public void TamperedToken_IsRejected()
    {
        var res = new JwtTokenService(Options.Create(Opts), new FixedClock(Now)).Create(Alice());
        var parts = res.Token.Split('.');
        // Flip a character in the payload; the signature no longer matches.
        var payload = parts[1].ToCharArray();
        payload[5] = payload[5] == 'A' ? 'B' : 'A';
        var tampered = $"{parts[0]}.{new string(payload)}.{parts[2]}";

        Assert.ThrowsAny<Exception>(() => new JwtSecurityTokenHandler { MapInboundClaims = false }
            .ValidateToken(tampered, Validation(Opts.Key), out _));
    }

    [Fact]
    public void TokenSignedWithDifferentKey_IsRejected()
    {
        var res = new JwtTokenService(Options.Create(Opts), new FixedClock(Now)).Create(Alice());

        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(() => new JwtSecurityTokenHandler { MapInboundClaims = false }
            .ValidateToken(res.Token, Validation("a-completely-different-key-of-sufficient-length-999"), out _));
    }
}
