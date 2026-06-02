using System.Collections.Concurrent;
using MaichessTournamentBridgeService.Models;

namespace MaichessTournamentBridgeService.Services;

internal sealed class RegistrationStore
{
    private readonly ConcurrentDictionary<string, Registration> _registrations = new();

    internal Registration Save(Registration registration)
    {
        _registrations[registration.Id] = registration;
        return registration;
    }

    internal Registration? GetById(string id) =>
        _registrations.TryGetValue(id, out Registration? reg) ? reg : null;

    internal Registration? FindByTournament(string serverUrl, string tournamentId) =>
        _registrations.Values.FirstOrDefault(r =>
            r.ServerUrl == serverUrl && r.TournamentId == tournamentId);

    internal IReadOnlyList<Registration> GetAll() => [.. _registrations.Values];

    internal void AddGameMapping(string registrationId, GameMapping mapping)
    {
        if (_registrations.TryGetValue(registrationId, out Registration? reg))
        {
            reg.GameMappings.Add(mapping);
        }
    }
}
