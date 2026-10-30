using System.ComponentModel.DataAnnotations;

namespace TempoDesk.Api.Controllers;

public sealed class ResendVerificationRequestDto
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = "";
}
