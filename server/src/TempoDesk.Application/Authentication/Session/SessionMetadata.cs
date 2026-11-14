using System.Net;

namespace TempoDesk.Application.Authentication;

public sealed record SessionMetadata(
    string? UserAgent,
    IPAddress? IpAddress)
{
    public static SessionMetadata Empty { get; } = new(null, null);
}
