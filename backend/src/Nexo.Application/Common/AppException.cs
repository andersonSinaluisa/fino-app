namespace Nexo.Application.Common;

/// <summary>Base type for expected application failures. Mapped to RFC 9457 problem details.</summary>
public abstract class AppException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public abstract int StatusCode { get; }
}

public sealed class NotFoundException(string resource, object key)
    : AppException("not_found", $"{resource} '{key}' was not found.")
{
    public override int StatusCode => 404;
}

public sealed class ForbiddenException(string message = "You do not have access to this resource.")
    : AppException("forbidden", message)
{
    public override int StatusCode => 403;
}

public sealed class ValidationException(string message, IReadOnlyDictionary<string, string[]>? errors = null)
    : AppException("validation_failed", message)
{
    public override int StatusCode => 400;

    public IReadOnlyDictionary<string, string[]> Errors { get; } =
        errors ?? new Dictionary<string, string[]>();

    public static ValidationException For(string field, string message) =>
        new($"'{field}' is invalid.", new Dictionary<string, string[]> { [field] = [message] });
}

public sealed class ConflictException(string message)
    : AppException("conflict", message)
{
    public override int StatusCode => 409;
}

public sealed class UnauthorizedException(string message = "Invalid credentials.")
    : AppException("unauthorized", message)
{
    public override int StatusCode => 401;
}

public sealed class UnsupportedFileException(string message)
    : AppException("unsupported_file", message)
{
    public override int StatusCode => 415;
}
