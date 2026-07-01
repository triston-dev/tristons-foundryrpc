// =============================================================================
//  Triston's FoundryRPC  —  DiscordPresenceManager.cs
//  Wraps the maintained DiscordRichPresence library (Lachee / discord-rpc-csharp).
//  Renders the configurable Details/State/asset templates, sets the elapsed
//  timer from the locally-detected launch time, and clears presence when idle.
//  Tolerates Discord being closed: the underlying client auto-reconnects, and we
//  re-apply the last presence on OnReady.
//
//  Author:  triston-dev   ·   https://github.com/triston-dev
//  Product: Triston's FoundryRPC
//  License: MIT (see LICENSE)
// =============================================================================

using System;
using System.Text;
using System.Threading;
using DiscordRPC;

namespace TristonsFoundryRPC;

public sealed class DiscordPresenceManager : IDisposable
{
    // === Discord Application ID ==============================================
    // NEW Discord application created specifically for Triston's FoundryRPC.
    // Upload the large/small art assets under this app in the Discord Developer
    // Portal (Rich Presence → Art Assets) and reference their keys in config.
    public const string DefaultApplicationId = "1521954755885273259";
    // =========================================================================

    // Discord caps Details/State/asset-text at 128 bytes; keep a safe margin.
    private const int MaxFieldBytes = 120;

    private readonly Config _config;
    private readonly Logger _log;
    private readonly object _gate = new();

    private DiscordRpcClient? _client;
    private System.Threading.Timer? _pump;
    private bool _enabled;

    // Remembered so we can re-apply after a reconnect (OnReady).
    private WorldStatus? _lastStatus;
    private string _lastDisplayName = "Foundry VTT";
    private DateTime? _lastLaunchedUtc;

    public DiscordPresenceManager(Config config, Logger log)
    {
        _config = config;
        _log = log;
    }

    /// <summary>Initialize the client if RPC is enabled in config.</summary>
    public void Start()
    {
        lock (_gate)
        {
            _enabled = _config.RpcEnabled;
            if (_enabled)
                EnsureClient();
        }
    }

    public bool Enabled
    {
        get { lock (_gate) return _enabled; }
    }

    /// <summary>Turn presence on/off at runtime (tray toggle).</summary>
    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_enabled == enabled)
                return;
            _enabled = enabled;
            _log.Info($"Rich Presence {(enabled ? "enabled" : "disabled")}.");

            if (enabled)
            {
                EnsureClient();
                if (_lastStatus is { Active: true })
                    ApplyPresence(_lastStatus, _lastDisplayName, _lastLaunchedUtc);
            }
            else
            {
                ClearInternal();
            }
        }
    }

    /// <summary>
    /// Update presence for a new status. <paramref name="displayName"/> is the
    /// already-resolved world title. Clears presence when the status is idle.
    /// </summary>
    public void Update(WorldStatus status, string displayName, DateTime? launchedUtc)
    {
        lock (_gate)
        {
            _lastStatus = status;
            _lastDisplayName = displayName;
            _lastLaunchedUtc = launchedUtc;

            if (!_enabled)
                return;

            EnsureClient();

            if (!status.Active)
                ClearInternal();
            else
                ApplyPresence(status, displayName, launchedUtc);
        }
    }

    /// <summary>Clear presence (idle / shutdown).</summary>
    public void Clear()
    {
        lock (_gate)
            ClearInternal();
    }

    // -------------------------------------------------------------------------

    private void EnsureClient()
    {
        if (_client is { IsDisposed: false })
            return;

        var appId = string.IsNullOrWhiteSpace(_config.DiscordApplicationId)
            ? DefaultApplicationId
            : _config.DiscordApplicationId.Trim();

        _client = new DiscordRpcClient(appId);

        _client.OnReady += (_, e) =>
        {
            _log.Info($"Discord ready (user: {e.User?.Username ?? "?"}). Application ID {appId}.");
            // Re-apply presence in case we connected after the world was already up.
            lock (_gate)
            {
                if (_enabled && _lastStatus is { Active: true })
                    ApplyPresence(_lastStatus, _lastDisplayName, _lastLaunchedUtc);
            }
        };
        _client.OnConnectionEstablished += (_, _) => _log.Debug("Discord pipe connection established.");
        _client.OnConnectionFailed += (_, _) => _log.Debug("Discord not available yet (is Discord running?). Will keep retrying.");
        _client.OnError += (_, e) => _log.Error($"Discord RPC error: {e.Code} — {e.Message}");
        _client.OnClose += (_, e) => _log.Debug($"Discord pipe closed: {e.Reason}");

        // Initialize starts the connection thread, which auto-reconnects if Discord
        // is not running yet. Returns true once the pipe machinery is set up.
        _client.Initialize();

        // Pump the message queue so our event handlers actually fire.
        _pump ??= new System.Threading.Timer(_ => PumpSafe(), null, 1000, 1000);

        _log.Info($"Discord client initialized (app id {appId}).");
    }

    private void PumpSafe()
    {
        try
        {
            var c = _client;
            if (c is { IsInitialized: true, IsDisposed: false })
                c.Invoke();
        }
        catch
        {
            // Invoke can race with Dispose during shutdown — ignore.
        }
    }

    private void ApplyPresence(WorldStatus status, string displayName, DateTime? launchedUtc)
    {
        if (_client is not { IsDisposed: false })
            return;

        try
        {
            var rp = new RichPresence();

            var details = CleanField(Render(_config.DetailsTemplate, status, displayName));
            if (details is not null)
                rp.Details = details;

            if (_config.ShowState)
            {
                var state = CleanField(Render(_config.StateTemplate, status, displayName));
                if (state is not null)
                    rp.State = state;
            }

            if (launchedUtc.HasValue)
                rp.Timestamps = new Timestamps(launchedUtc.Value.ToUniversalTime());

            Assets? assets = null;
            if (!string.IsNullOrWhiteSpace(_config.LargeImageKey))
            {
                assets = new Assets
                {
                    LargeImageKey = _config.LargeImageKey.Trim(),
                    LargeImageText = CleanField(Render(_config.LargeImageText, status, displayName)),
                };
            }
            if (!string.IsNullOrWhiteSpace(_config.SmallImageKey))
            {
                assets ??= new Assets();
                assets.SmallImageKey = _config.SmallImageKey.Trim();
                assets.SmallImageText = CleanField(Render(_config.SmallImageText, status, displayName));
            }
            if (assets is not null)
                rp.Assets = assets;

            _client!.SetPresence(rp);
            _log.Info($"Presence → Details=\"{rp.Details}\" State=\"{rp.State}\" " +
                      $"since={launchedUtc?.ToLocalTime():HH:mm:ss}");
        }
        catch (Exception ex)
        {
            _log.Error("Failed to set Discord presence", ex);
        }
    }

    private void ClearInternal()
    {
        try
        {
            if (_client is { IsInitialized: true, IsDisposed: false })
            {
                _client.ClearPresence();
                _log.Debug("Discord presence cleared.");
            }
        }
        catch (Exception ex)
        {
            _log.Error("Failed to clear Discord presence", ex);
        }
    }

    private static string Render(string? template, WorldStatus s, string world) =>
        (template ?? string.Empty)
            .Replace("{world}", world ?? string.Empty)
            .Replace("{system}", s.SystemId ?? string.Empty)
            .Replace("{users}", s.UsersActive.ToString())
            .Replace("{version}", s.FoundryVersion ?? string.Empty);

    /// <summary>Trim, drop empties (Discord needs ≥2 chars), clamp to byte limit.</summary>
    private static string? CleanField(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        if (value.Length < 2)
            return null;
        return ClampBytes(value, MaxFieldBytes);
    }

    private static string ClampBytes(string s, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(s) <= maxBytes)
            return s;
        // Remove trailing characters until we fit (handles multi-byte safely).
        var span = s.AsSpan();
        int len = span.Length;
        while (len > 0 && Encoding.UTF8.GetByteCount(span[..len]) > maxBytes)
            len--;
        return span[..len].ToString();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _pump?.Dispose(); } catch { }
            _pump = null;

            try
            {
                if (_client is { IsDisposed: false })
                {
                    // Clear presence on clean shutdown, then dispose.
                    if (_client.IsInitialized)
                        _client.ClearPresence();
                    _client.Dispose();
                    _log.Info("Discord client disposed; presence cleared.");
                }
            }
            catch (Exception ex)
            {
                _log.Error("Error disposing Discord client", ex);
            }
            finally
            {
                _client = null;
            }
        }
    }
}
