using Metrics.Domain.Entities;

namespace Metrics.Application.Auth;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public record TokenResult(string Token, DateTime ExpiresAt);

public interface ITokenService
{
    TokenResult Create(User user);
}

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct);
    Task<User?> FindByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
}

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<UserDto> GetCurrentAsync(Guid userId, CancellationToken ct);
}
