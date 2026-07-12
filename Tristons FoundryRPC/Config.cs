// =============================================================================
//  Triston's FoundryRPC  —  Config.cs
//  Strongly-typed settings, persisted as JSON at
//  %APPDATA%\Tristons FoundryRPC\config.json. Tolerant of missing/renamed
//  fields; a corrupt file is backed up and replaced with defaults so the
//  "Edit World Display Names" menu item always opens a valid file.
//
//  Author:  triston-dev   ·   https://github.com/triston-dev
//  Product: Triston's FoundryRPC
//  License: MIT (see LICENSE)
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TristonsFoundryRPC;

public sealed class Config
{
    // ----- Data source: Foundry server base URLs, polled at GET <url>/api/status -----
    // One entry per world/server, e.g. "https://my-world.sqyre.app" or
    // "http://localhost:30000". All are polled in parallel; the first (in list
    // order) reporting an active world wins.
    public List<string> FoundryServers { get; set; } = new();

    /// <summary>How often to poll the servers for the active world, in seconds.</summary>
    public int PollIntervalSeconds { get; set; } = 15;

    /// <summary>
    /// Hysteresis: how many consecutive failed polls before we declare IDLE and
    /// clear presence. Prevents flicker when a server blips. 2 misses ≈ ~30s grace.
    /// </summary>
    public int IdleAfterMissedPolls { get; set; } = 2;

    // ----- Discord Rich Presence -----
    public bool RpcEnabled { get; set; } = true;

    /// <summary>
    /// Discord Application ID. Defaults to the app created specifically for this
    /// project (see <see cref="DiscordPresenceManager.DefaultApplicationId"/>).
    /// </summary>
    public string DiscordApplicationId { get; set; } = DiscordPresenceManager.DefaultApplicationId;

    /// <summary>Details line template. Tokens: {world} {system} {users} {version}.</summary>
    public string DetailsTemplate { get; set; } = "Running {world}";

    /// <summary>State line template. Tokens: {world} {system} {users} {version}.</summary>
    public string StateTemplate { get; set; } = "{users} online";

    /// <summary>Toggle the State line on/off independently of its template.</summary>
    public bool ShowState { get; set; } = true;

    /// <summary>
    /// Large image: either a full https image URL (verified working on Discord
    /// as of 2026-07) or an art-asset key uploaded in the Discord Developer
    /// Portal (Rich Presence → Art Assets). Defaults to the official Foundry
    /// logo URL so presence art works with zero portal setup.
    /// </summary>
    public string LargeImageKey { get; set; } = "https://foundryvtt.com/static/assets/icons/fvtt.png";

    /// <summary>Hover tooltip for the large image. Supports the same tokens.</summary>
    public string LargeImageText { get; set; } = "Foundry VTT — {world}";

    /// <summary>Optional small image asset key (e.g. a game-system icon). Empty = skip.</summary>
    public string SmallImageKey { get; set; } = "";

    /// <summary>Hover tooltip for the small image. Supports the same tokens.</summary>
    public string SmallImageText { get; set; } = "";

    // ----- Display-name overrides -----
    // Map a raw world id (e.g. "test-game-fe5eb2a8") to a pretty title you want
    // shown. Normally unnecessary — the bridge already returns the world's real
    // title — but use this to override it. The raw world id is written to the log
    // the first time it is seen so you know exactly what key to add here.
    public Dictionary<string, string> WorldDisplayNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ----- UX / diagnostics -----
    public bool ShowBalloonOnWorldChange { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogVerbosity LogVerbosity { get; set; } = LogVerbosity.Info;

    // ---------------------------------------------------------------------------
    //  Paths
    // ---------------------------------------------------------------------------
    public const string ProductFolderName = "Tristons FoundryRPC";

    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProductFolderName);

    public static string ConfigPath => Path.Combine(AppDataDir, "config.json");
    public static string LogPath => Path.Combine(AppDataDir, "foundryrpc.log");

    // ---------------------------------------------------------------------------
    //  Load / Save
    // ---------------------------------------------------------------------------
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Load config from disk, creating a default file if none exists. A file that
    /// fails to parse is backed up to config.json.bad and replaced with defaults.
    /// Never throws.
    /// </summary>
    public static Config Load(Logger? log = null)
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);

            if (!File.Exists(ConfigPath))
            {
                var fresh = new Config();
                fresh.Save(log);
                log?.Info($"Created default config at {ConfigPath}");
                return fresh;
            }

            var json = File.ReadAllText(ConfigPath);
            var cfg = JsonSerializer.Deserialize<Config>(json, JsonOpts);
            if (cfg is null)
                throw new InvalidDataException("Config deserialized to null.");

            cfg.Normalize();
            return cfg;
        }
        catch (Exception ex)
        {
            log?.Error("Config.Load failed; using defaults", ex);
            try
            {
                if (File.Exists(ConfigPath))
                    File.Copy(ConfigPath, ConfigPath + ".bad", overwrite: true);
            }
            catch { /* best effort */ }

            var fallback = new Config();
            fallback.Save(log);
            return fallback;
        }
    }

    /// <summary>Persist to disk. Never throws.</summary>
    public void Save(Logger? log = null)
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            var json = JsonSerializer.Serialize(this, JsonOpts);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            log?.Error("Config.Save failed", ex);
        }
    }

    /// <summary>Clamp values into sane ranges after loading.</summary>
    private void Normalize()
    {
        if (PollIntervalSeconds < 5) PollIntervalSeconds = 5;
        if (PollIntervalSeconds > 3600) PollIntervalSeconds = 3600;
        if (IdleAfterMissedPolls < 1) IdleAfterMissedPolls = 1;
        if (string.IsNullOrWhiteSpace(DiscordApplicationId))
            DiscordApplicationId = DiscordPresenceManager.DefaultApplicationId;
        FoundryServers ??= new();
        WorldDisplayNames ??= new(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>Serializable mirror of <see cref="LogLevel"/> for config files.</summary>
public enum LogVerbosity
{
    Error = 0,
    Info = 1,
    Debug = 2,
}
