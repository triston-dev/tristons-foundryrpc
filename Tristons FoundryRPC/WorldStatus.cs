// =============================================================================
//  Triston's FoundryRPC  —  WorldStatus.cs
//  Immutable snapshot of what Foundry is doing right now, parsed from the
//  bridge's get-world-info payload. Also defines what counts as a "meaningful
//  change" so we only push Discord updates when world id / active / user count
//  actually change.
//
//  Author:  triston-dev   ·   https://github.com/triston-dev
//  Product: Triston's FoundryRPC
//  License: MIT (see LICENSE)
// =============================================================================

using System;
using System.Text.Json;

namespace TristonsFoundryRPC;

/// <summary>A point-in-time view of the active Foundry world (or idle).</summary>
public sealed record WorldStatus
{
    public bool Active { get; init; }
    public string? WorldId { get; init; }
    public string? Title { get; init; }
    public string? SystemId { get; init; }
    public string? SystemVersion { get; init; }
    public string? FoundryVersion { get; init; }
    public int UsersActive { get; init; }

    /// <summary>The canonical "nothing is running" snapshot.</summary>
    public static readonly WorldStatus Idle = new() { Active = false };

    /// <summary>
    /// True when a Discord update is warranted relative to <paramref name="prev"/>.
    /// Per spec: only world id, active-state, or user-count changes matter.
    /// </summary>
    public bool IsMeaningfulChangeFrom(WorldStatus prev) =>
        Active != prev.Active ||
        !string.Equals(WorldId, prev.WorldId, StringComparison.Ordinal) ||
        UsersActive != prev.UsersActive;

    /// <summary>
    /// Parse the inner JSON string returned by get-world-info. The bridge wraps
    /// the world object as a JSON string inside result.content[0].text, so this
    /// receives that already-unwrapped string. Returns <see cref="Idle"/> if the
    /// payload has no usable world id.
    /// </summary>
    public static WorldStatus FromWorldInfoJson(string worldInfoJson)
    {
        using var doc = JsonDocument.Parse(worldInfoJson);
        var root = doc.RootElement;

        string? worldId = GetString(root, "id");
        string? title = GetString(root, "title");

        // No world id => Foundry is at /setup or otherwise has no world loaded.
        if (string.IsNullOrWhiteSpace(worldId))
            return Idle;

        string? systemId = null, systemVersion = null;
        if (root.TryGetProperty("system", out var sys) && sys.ValueKind == JsonValueKind.Object)
        {
            systemId = GetString(sys, "id");
            systemVersion = GetString(sys, "version");
        }

        string? foundryVersion = null;
        if (root.TryGetProperty("foundry", out var f) && f.ValueKind == JsonValueKind.Object)
            foundryVersion = GetString(f, "version");

        int usersActive = 0;
        if (root.TryGetProperty("users", out var users) && users.ValueKind == JsonValueKind.Object)
        {
            if (users.TryGetProperty("active", out var a) && a.TryGetInt32(out var av))
                usersActive = av;
        }

        return new WorldStatus
        {
            Active = true,
            WorldId = worldId,
            Title = title,
            SystemId = systemId,
            SystemVersion = systemVersion,
            FoundryVersion = foundryVersion,
            UsersActive = usersActive,
        };
    }

    private static string? GetString(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
