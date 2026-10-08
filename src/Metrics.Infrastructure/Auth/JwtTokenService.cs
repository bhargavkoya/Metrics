using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Metrics.Application.Auth;
using Metrics.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Metrics.Infrastructure.Auth;

public class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    public const string TeamClaim = "team";

    private readonly JwtOptions _opts = options.Value;

    public TokenResult Create(User user)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddHours(_opts.ExpiryHours);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("name", user.Name),
            new Claim(TeamClaim, user.Team.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var creds = new SigningCredentials(SigningKey(_opts), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(_opts.Issuer, _opts.Audience, claims, now, expires, creds);
        return new TokenResult(new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }

    public static SymmetricSecurityKey SigningKey(JwtOptions o) => new(Encoding.UTF8.GetBytes(o.Key));
}
