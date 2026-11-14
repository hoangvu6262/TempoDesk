using System.ComponentModel.DataAnnotations;

namespace TempoDesk.Api.Controllers;

public sealed class ResetPasswordRequestDto
{
    [Required, MaxLength(512)]
    public string Token { get; init; } = "";

    [Required, MinLength(12), MaxLength(1024)]
    public string NewPassword { get; init; } = "";
}
