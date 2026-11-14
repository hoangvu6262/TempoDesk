namespace TempoDesk.Application.Authentication;

public interface IAuthenticationTokenService
{
    Task<AuthenticationTokenResult> IssueAsync(
        Guid userId,
        SessionMetadata metadata,
        CancellationToken cancellationToken = default);

    Task<AuthenticationTokenResult?> RefreshAsync(
        string rawToken,
        CancellationToken cancellationToken = default);
}
