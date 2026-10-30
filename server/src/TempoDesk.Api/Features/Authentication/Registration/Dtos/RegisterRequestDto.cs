using System.ComponentModel.DataAnnotations;

namespace TempoDesk.Api.Controllers;

public sealed class RegisterRequestDto
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = "";

    [Required, MinLength(12), MaxLength(1024)]
    public string Password { get; init; } = "";
}
