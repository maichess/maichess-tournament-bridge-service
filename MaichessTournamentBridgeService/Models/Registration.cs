namespace MaichessTournamentBridgeService.Models;

internal sealed class Registration
{
    public required string Id { get; set; }

    public required string ServerUrl { get; set; }

    public required string TournamentId { get; set; }

    public required string TournamentName { get; set; }

    public required string MaichessBotId { get; set; }

    public required string MaichessUserId { get; set; }

    public required string Status { get; set; }

    public required string DirectorToken { get; set; }

    public required string BotToken { get; set; }

    public List<GameMapping> GameMappings { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
