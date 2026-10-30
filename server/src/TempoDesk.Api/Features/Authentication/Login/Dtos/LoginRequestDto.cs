using System.ComponentModel.DataAnnotations;

namespace TempoDesk.Api.Controllers;

public sealed class LoginRequestDto
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = "";

    [Required, MaxLength(1024)]
    public string Password { get; init; } = "";
}
