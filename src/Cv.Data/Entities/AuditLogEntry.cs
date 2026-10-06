namespace Cv.Data.Entities;

/// <summary>Logins, restores, publishes and downloads. Content edits are already recorded by the version history.</summary>
public sealed class AuditLogEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Upper snake case, e.g. <c>PUBLISH</c>, <c>DOWNLOAD</c>.</summary>
    public required string Action { get; init; }

    public Guid? SubjectId { get; init; }

    /// <summary>JSON object.</summary>
    public string Details { get; init; } = "{}";

    /// <summary>Null for anonymous or system actions.</summary>
    public Guid? ActorId { get; init; }

    public DateTimeOffset CreatedAt { get; private set; }
}
