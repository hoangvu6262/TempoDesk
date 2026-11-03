using System.ComponentModel.DataAnnotations;

namespace TempoDesk.Api.Controllers;

public sealed class VerifyEmailRequestDto
{
    [Required, MaxLength(512)]
    public string Token { get; init; } = "";
}
