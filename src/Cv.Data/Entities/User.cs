namespace Cv.Data.Entities;

/// <summary>The CV owner. Single-user today; modelled as a table so audit columns are real foreign keys.</summary>
public sealed class User
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
