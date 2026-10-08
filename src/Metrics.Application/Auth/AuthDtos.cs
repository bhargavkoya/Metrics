using Metrics.Domain;

namespace Metrics.Application.Auth;

public record RegisterRequest(string Email, string Password, string Name, Team Team);

public record LoginRequest(string Email, string Password);

public record UserDto(Guid Id, string Email, string Name, Team Team);

public record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);
