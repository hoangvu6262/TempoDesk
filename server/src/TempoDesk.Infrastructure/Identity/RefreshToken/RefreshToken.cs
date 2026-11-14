using System.Net;

namespace TempoDesk.Infrastructure.Identity;

public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public string? DeviceName { get; set; }
    public string? UserAgent { get; set; }
    public IPAddress? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
