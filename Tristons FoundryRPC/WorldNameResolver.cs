// =============================================================================
//  Triston's FoundryRPC  —  WorldNameResolver.cs
//  Turns a raw world id into the name to display, in priority order:
//    (a) a user override from config (worldId -> "Display Title"), else
//    (b) the pretty title the bridge already returns, else
//    (c) the raw world id.
//  Logs each new raw world id the first time it is seen so the user knows the
//  exact key to add to the override map.
//
//  Author:  triston-dev   ·   https://github.com/triston-dev
//  Product: Triston's FoundryRPC
//  License: MIT (see LICENSE)
// =============================================================================

using System;
using System.Collections.Generic;

namespace TristonsFoundryRPC;

public sealed class WorldNameResolver
{
    private readonly Config _config;
    private readonly Logger _log;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public WorldNameResolver(Config config, Logger log)
    {
        _config = config;
        _log = log;
    }

    /// <summary>Resolve the display name for a status snapshot.</summary>
    public string Resolve(WorldStatus status)
    {
        var id = status.WorldId;
        if (string.IsNullOrWhiteSpace(id))
            return "Foundry VTT";

        NoteFirstSighting(id, status.Title);

        // (a) explicit override wins
        if (_config.WorldDisplayNames.TryGetValue(id, out var overrideName) &&
            !string.IsNullOrWhiteSpace(overrideName))
            return overrideName;

        // (b) the title the bridge reports
        if (!string.IsNullOrWhiteSpace(status.Title))
            return status.Title!;

        // (c) fall back to the raw id
        return id!;
    }

    private void NoteFirstSighting(string id, string? title)
    {
        if (_seen.Add(id))
        {
            _log.Info(
                $"World id seen for the first time: \"{id}\" (title: \"{title ?? "<none>"}\"). " +
                $"To force a custom display name, add \"{id}\": \"Your Title\" under " +
                $"\"worldDisplayNames\" in {Config.ConfigPath}.");
        }
    }
}
