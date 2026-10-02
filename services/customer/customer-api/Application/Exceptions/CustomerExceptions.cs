namespace CustomerApi.Application.Exceptions;

/// <summary>404 — also returned for records outside the caller's visibility, so their existence is not revealed (§2).</summary>
public class NotFoundException(string message) : Exception(message);

/// <summary>401.</summary>
public class UnauthorizedException(string message) : Exception(message);

/// <summary>403 — only for actions the caller may never perform, never for record visibility.</summary>
public class ForbiddenException(string message) : Exception(message);

/// <summary>
/// 409. <see cref="ConflictingRecordId"/> / <see cref="ConflictingRecordName"/> name the record that
/// blocks the save, so the client can show it (DUP-1, DEL-2, AC-3, AC-4, AC-14).
/// </summary>
public class ConflictException(string message) : Exception(message)
{
    public Guid? ConflictingRecordId { get; init; }
    public string? ConflictingRecordName { get; init; }

    public static ConflictException Duplicate(string message, Guid id, string name) =>
        new(message) { ConflictingRecordId = id, ConflictingRecordName = name };
}

/// <summary>400 with per-field errors. Field names are preserved so the response can name them (CF-3, AC-2, AC-10).</summary>
public class CustomerValidationException : Exception
{
    public CustomerValidationException(string message) : base(message)
        => Errors = new Dictionary<string, string[]> { [""] = [message] };

    public CustomerValidationException(string field, string message) : base($"{field}: {message}")
        => Errors = new Dictionary<string, string[]> { [field] = [message] };

    public CustomerValidationException(IDictionary<string, string[]> errors)
        : base(string.Join("; ", errors.SelectMany(e => e.Value.Select(m => $"{e.Key}: {m}"))))
        => Errors = errors;

    public IDictionary<string, string[]> Errors { get; }
}
