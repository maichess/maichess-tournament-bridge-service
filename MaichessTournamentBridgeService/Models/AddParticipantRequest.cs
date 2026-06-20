using System.Text.Json.Serialization;

namespace MaichessTournamentBridgeService.Models;

// Request body for POST /api/tournament/{id}/participants — add an already
// permanently-registered bot to a tournament by its registry id, without
// re-supplying its details. Only the director may do this.
internal sealed record AddParticipantRequest(
    [property: JsonPropertyName("botId")] string BotId);
