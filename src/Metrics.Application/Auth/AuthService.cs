using System.Net.Mail;
using Metrics.Application.Common;
using Metrics.Domain;
using Metrics.Domain.Entities;

namespace Metrics.Application.Auth;

public class AuthService(IUserRepository users, IPasswordHasher hasher, ITokenService tokens) : IAuthService
{
    public const int MinPasswordLength = 8;

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        var email = NormalizeEmail(request.Email);
        if (await users.FindByEmailAsync(email, ct) is not null)
            throw new ConflictException("An account with this email already exists.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Name = request.Name.Trim(),
            PasswordHash = hasher.Hash(request.Password),
            Team = request.Team
        };
        await users.AddAsync(user, ct);
        return ToResponse(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        // One generic failure for unknown email and wrong password, so responses don't reveal which emails exist.
        var invalid = new UnauthorizedException("Invalid email or password.");

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password)) throw invalid;

        var user = await users.FindByEmailAsync(NormalizeEmail(request.Email), ct);
        if (user is null || !hasher.Verify(user.PasswordHash, request.Password)) throw invalid;

        return ToResponse(user);
    }

    public async Task<UserDto> GetCurrentAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId, ct)
                   ?? throw new UnauthorizedException("User no longer exists.");
        return ToDto(user);
    }

    private AuthResponse ToResponse(User user)
    {
        var token = tokens.Create(user);
        return new AuthResponse(token.Token, token.ExpiresAt, ToDto(user));
    }

    private static UserDto ToDto(User u) => new(u.Id, u.Email, u.Name, u.Team);

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static Dictionary<string, string[]> Validate(RegisterRequest r)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(r.Email) || !IsValidEmail(r.Email))
            errors["email"] = ["A valid email address is required."];
        else if (r.Email.Length > 256)
            errors["email"] = ["Email must be 256 characters or fewer."];

        if (string.IsNullOrEmpty(r.Password) || r.Password.Length < MinPasswordLength)
            errors["password"] = [$"Password must be at least {MinPasswordLength} characters."];

        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 200)
            errors["name"] = ["Name is required and must be 200 characters or fewer."];

        if (!Enum.IsDefined(r.Team))
            errors["team"] = ["Team must be Business or Technical."];

        return errors;
    }

    private static bool IsValidEmail(string email)
    {
        // MailAddress alone accepts display-name forms like "A <a@b.c>"; require the parsed address to equal the input.
        return MailAddress.TryCreate(email.Trim(), out var addr) && addr.Address == email.Trim();
    }
}
