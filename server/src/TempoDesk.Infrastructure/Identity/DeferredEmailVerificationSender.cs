using TempoDesk.Application.Authentication;

namespace TempoDesk.Infrastructure.Identity;

internal sealed class DeferredEmailVerificationSender : IEmailVerificationSender
{
    public Task<bool> SendAsync(
        VerificationEmail message,
        CancellationToken cancellationToken) => Task.FromResult(false);
}
