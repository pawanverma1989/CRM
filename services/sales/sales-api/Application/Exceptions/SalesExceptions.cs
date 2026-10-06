namespace SalesApi.Application.Exceptions;

/// <summary>404 — also returned for records outside the caller's visibility (AC-12).</summary>
public class NotFoundException(string message) : Exception(message);

/// <summary>401.</summary>
public class UnauthorizedException(string message) : Exception(message);

/// <summary>403 — only for actions the caller may never perform, never for record visibility.</summary>
public class ForbiddenException(string message) : Exception(message);

/// <summary>
/// 409. <see cref="ConflictingRecordId"/> / <see cref="ConflictingRecordName"/> name the record that
/// blocks the save.
/// </summary>
public class ConflictException(string message) : Exception(message)
{
    public Guid? ConflictingRecordId { get; init; }
    public string? ConflictingRecordName { get; init; }

    public static ConflictException Duplicate(string message, Guid id, string? name = null) =>
        new(message) { ConflictingRecordId = id, ConflictingRecordName = name };
}

/// <summary>400 with per-field errors.</summary>
public class SalesValidationException : Exception
{
    public SalesValidationException(string message) : base(message)
        => Errors = new Dictionary<string, string[]> { [""] = [message] };

    public SalesValidationException(string field, string message) : base($"{field}: {message}")
        => Errors = new Dictionary<string, string[]> { [field] = [message] };

    public SalesValidationException(IDictionary<string, string[]> errors)
        : base(string.Join("; ", errors.SelectMany(e => e.Value.Select(m => $"{e.Key}: {m}"))))
        => Errors = errors;

    public IDictionary<string, string[]> Errors { get; }
}
