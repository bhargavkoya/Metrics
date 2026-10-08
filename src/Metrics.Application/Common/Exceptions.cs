namespace Metrics.Application.Common;

/// <summary>Mapped to HTTP 400 by the API exception handler.</summary>
public class ValidationFailedException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public ValidationFailedException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] }) { }
}

/// <summary>Mapped to HTTP 409.</summary>
public class ConflictException(string message) : Exception(message);

/// <summary>Mapped to HTTP 401.</summary>
public class UnauthorizedException(string message) : Exception(message);

/// <summary>Mapped to HTTP 404.</summary>
public class NotFoundException(string message) : Exception(message);
