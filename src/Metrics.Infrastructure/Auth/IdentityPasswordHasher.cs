using Metrics.Application.Auth;
using Microsoft.AspNetCore.Identity;

namespace Metrics.Infrastructure.Auth;

/// <summary>Wraps ASP.NET's PBKDF2 hasher (salted, versioned); no hand-rolled crypto.</summary>
public class IdentityPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<object> _inner = new();
    private static readonly object Subject = new();

    public string Hash(string password) => _inner.HashPassword(Subject, password);

    public bool Verify(string hash, string password) =>
        _inner.VerifyHashedPassword(Subject, hash, password) != PasswordVerificationResult.Failed;
}
