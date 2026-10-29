using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TempoDesk.Application.Authentication;
using TempoDesk.Infrastructure.Configuration;
using TempoDesk.Infrastructure.Identity;

namespace TempoDesk.Application.Tests;

public sealed class EmailDeliveryTests
{
    [Fact]
    public void IdentityRegistersTheLocalSmtpVerificationSender()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTempoDeskIdentity();
        var descriptor = Assert.Single(services, service =>
            service.ServiceType == typeof(IEmailVerificationSender));

        Assert.Equal(
            "SmtpEmailVerificationSender",
            descriptor.ImplementationType?.Name);
    }

    [Fact]
    public async Task DisabledEmailReportsDeliveryUnavailableWithoutConnecting()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTempoDeskIdentity();
        services.AddSingleton<IOptions<EmailOptions>>(
            Options.Create(new EmailOptions()));
        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IEmailVerificationSender>();

        var delivered = await sender.SendAsync(
            new VerificationEmail(
                "person@example.com",
                "raw-token",
                DateTimeOffset.UtcNow.AddMinutes(30)),
            CancellationToken.None);

        Assert.False(delivered);
    }
}
