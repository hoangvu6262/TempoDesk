using System.Text.Json;

namespace TempoDesk.Api.Controllers;

public sealed record CreateProjectRequestDto(
    string? Name,
    string? Description);

public sealed record UpdateProjectRequestDto(
    JsonElement? Name,
    JsonElement? Description);
