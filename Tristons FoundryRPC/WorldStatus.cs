// =============================================================================
//  Triston's FoundryRPC  —  WorldStatus.cs
//  Immutable snapshot of what Foundry is doing right now, parsed from the
//  server's /api/status endpoint. Also defines what counts as a "meaningful
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

    /// <summary>The world id/slug from /api/status (e.g. "campaign_2"), NOT the title.</summary>
    public string? WorldId { get; init; }

    /// <summary>Pretty title when known (scraped from the join page); often null.</summary>
    public string? Title { get; init; }

    public string? SystemId { get; init; }
    public string? SystemVersion { get; init; }
    public string? FoundryVersion { get; init; }
    public int UsersActive { get; init; }

    /// <summary>Base URL of the server that reported this status (null when idle).</summary>
    public string? SourceUrl { get; init; }

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
    /// Parse Foundry v13's GET /api/status response:
    ///   { "active": true, "version": "13.351", "world": "campaign_2",
    ///     "system": "dnd5e", "systemVersion": "...", "users": 1, "uptime": ... }
    /// "active": false or a missing/empty "world" both mean no world is loaded
    /// (Foundry sitting at the setup screen still answers with active:false).
    /// NOTE: "uptime" is deliberately ignored — it is server process uptime, not
    /// session time; the watcher tracks the session start locally.
    /// Throws JsonException on malformed input (caller treats that as idle).
    /// </summary>
    public static WorldStatus FromApiStatusJson(string json, string sourceUrl)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        bool active = root.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.True;
        string? world = GetString(root, "world");

        if (!active || string.IsNullOrWhiteSpace(world))
            return Idle;

        int users = 0;
        if (root.TryGetProperty("users", out var u) && u.ValueKind == JsonValueKind.Number)
            u.TryGetInt32(out users);

        return new WorldStatus
        {
            Active = true,
            WorldId = world,
            Title = null, // /api/status has no title; enriched from the join page
            SystemId = GetString(root, "system"),
            SystemVersion = GetString(root, "systemVersion"),
            FoundryVersion = GetString(root, "version"),
            UsersActive = users,
            SourceUrl = sourceUrl,
        };
    }

    private static string? GetString(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
