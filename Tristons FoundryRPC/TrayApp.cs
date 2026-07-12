// =============================================================================
//  Triston's FoundryRPC  —  TrayApp.cs
//  The tray UI: a NotifyIcon with a right-click menu, plus the wiring between
//  the status watcher, the world-name resolver, and the Discord presence
//  manager. Watcher events arrive on a background thread and are marshaled to
//  the UI thread via a hidden control.
//
//  Author:  triston-dev   ·   https://github.com/triston-dev
//  Product: Triston's FoundryRPC
//  License: MIT (see LICENSE)
// =============================================================================

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TristonsFoundryRPC;

public sealed class TrayApp : ApplicationContext
{
    private readonly Config _config;
    private readonly Logger _log;
    private readonly WorldNameResolver _resolver;
    private readonly FoundryHttpClient _foundry;
    private readonly FoundryStatusWatcher _watcher;
    private readonly DiscordPresenceManager _discord;

    private readonly Control _sync;           // UI-thread marshaling anchor
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _enableItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly Icon _icon;

    // UI state for balloon/change detection.
    private WorldStatus _lastStatus = WorldStatus.Idle;
    private string _lastName = "Foundry VTT";
    private string? _lastUiWorldId;
    private bool _lastActive;
    private bool _exiting;

    public TrayApp(Config config, Logger log)
    {
        _config = config;
        _log = log;

        _resolver = new WorldNameResolver(config, log);
        _foundry = new FoundryHttpClient(config, log);
        _watcher = new FoundryStatusWatcher(config, _foundry, log);
        _discord = new DiscordPresenceManager(config, log);

        // Force handle creation on the current (UI) thread so BeginInvoke works.
        _sync = new Control();
        _ = _sync.Handle;

        _icon = IconLoader.LoadTrayIcon(log);
        (_menu, _statusItem, _enableItem, _startupItem) = BuildMenu();

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "Triston's FoundryRPC — starting…",
            Visible = true,
            ContextMenuStrip = _menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowAbout();

        _watcher.StatusChanged += OnStatusChanged;

        _discord.Start();
        _watcher.Start();
        _log.Info("Tray app initialized.");
    }

    // -------------------------------------------------------------------------
    //  Menu construction
    // -------------------------------------------------------------------------
    private (ContextMenuStrip, ToolStripMenuItem status, ToolStripMenuItem enable, ToolStripMenuItem startup) BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var statusItem = new ToolStripMenuItem("Idle — no active world") { Enabled = false };
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());

        var enableItem = new ToolStripMenuItem("Enable Rich Presence", null, OnToggleEnable)
        {
            CheckOnClick = true,
            Checked = _config.RpcEnabled,
        };
        menu.Items.Add(enableItem);

        menu.Items.Add(new ToolStripMenuItem("Configure Foundry Server URLs…", null, OnConfigureServers));

        var overrides = new ToolStripMenuItem("World Display Names");
        overrides.DropDownItems.Add(new ToolStripMenuItem("Edit config.json…", null, (_, _) => OpenConfigFile()));
        overrides.DropDownItems.Add(new ToolStripMenuItem("Open config folder…", null, (_, _) => OpenPath(Config.AppDataDir)));
        menu.Items.Add(overrides);

        var startupItem = new ToolStripMenuItem("Run at Windows startup", null, OnToggleStartup)
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled(_log),
        };
        menu.Items.Add(startupItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("About…", null, (_, _) => ShowAbout()));
        menu.Items.Add(new ToolStripMenuItem("View log…", null, (_, _) => OpenLog()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit", null, OnQuit));

        return (menu, statusItem, enableItem, startupItem);
    }

    // -------------------------------------------------------------------------
    //  Watcher -> UI + Discord
    // -------------------------------------------------------------------------
    private void OnStatusChanged(object? sender, WorldStatusChangedEventArgs e)
    {
        try
        {
            var status = e.Status;
            var name = _resolver.Resolve(status);

            // Discord manager is internally thread-safe; call it directly.
            _discord.Update(status, name, e.LaunchedUtc);

            _lastStatus = status;
            _lastName = name;

            RunOnUi(() => UpdateUi(status, name));
        }
        catch (Exception ex)
        {
            _log.Error("OnStatusChanged failed", ex);
        }
    }

    private void UpdateUi(WorldStatus status, string name)
    {
        if (status.Active)
        {
            var usersPart = status.UsersActive > 0 ? $" ({status.UsersActive} online)" : "";
            _statusItem.Text = $"Running: {name}{usersPart}";
            _notifyIcon.Text = Truncate($"Foundry: {name}", 63);

            bool worldChanged = !_lastActive ||
                                !string.Equals(_lastUiWorldId, status.WorldId, StringComparison.Ordinal);
            if (worldChanged && _config.ShowBalloonOnWorldChange)
                _notifyIcon.ShowBalloonTip(4000, "Foundry VTT", $"Now running: {name}", ToolTipIcon.Info);

            _lastUiWorldId = status.WorldId;
            _lastActive = true;
        }
        else
        {
            _statusItem.Text = _discord.Enabled ? "Idle — no active world" : "Idle — presence off";
            _notifyIcon.Text = "Triston's FoundryRPC — idle";

            if (_lastActive && _config.ShowBalloonOnWorldChange)
                _notifyIcon.ShowBalloonTip(3000, "Foundry VTT", "World closed — presence cleared.", ToolTipIcon.Info);

            _lastUiWorldId = null;
            _lastActive = false;
        }
    }

    // -------------------------------------------------------------------------
    //  Menu handlers
    // -------------------------------------------------------------------------
    private void OnToggleEnable(object? sender, EventArgs e)
    {
        _config.RpcEnabled = _enableItem.Checked;
        _config.Save(_log);
        _discord.SetEnabled(_config.RpcEnabled);

        if (_config.RpcEnabled)
            _watcher.RequestImmediatePoll();

        // Refresh the status label to reflect the new enabled state.
        RunOnUi(() => UpdateUi(_lastStatus, _lastName));
    }

    private void OnConfigureServers(object? sender, EventArgs e)
    {
        using var form = new ServersConfigForm(_config.FoundryServers);
        if (form.ShowDialog() == DialogResult.OK)
        {
            _config.FoundryServers = form.ServerUrls;
            _config.Save(_log);
            _log.Info($"Foundry servers set: [{string.Join(", ", _config.FoundryServers)}]");
            _watcher.RequestImmediatePoll(); // FoundryHttpClient reads config live.
        }
    }

    private void OnToggleStartup(object? sender, EventArgs e)
    {
        var actual = StartupManager.SetEnabled(_startupItem.Checked, _log);
        _startupItem.Checked = actual; // reflect the real registry state
    }

    private void OnQuit(object? sender, EventArgs e)
    {
        _log.Info("Quit selected from tray menu.");
        ExitApp();
    }

    private void ShowAbout()
    {
        try
        {
            using var about = new AboutForm($"Version {Application.ProductVersion}");
            about.ShowDialog();
        }
        catch (Exception ex)
        {
            _log.Error("About dialog failed", ex);
        }
    }

    private void OpenConfigFile()
    {
        try
        {
            // Config.Load() guarantees the file exists; open it in the default editor.
            if (!File.Exists(Config.ConfigPath))
                _config.Save(_log);
            OpenPath(Config.ConfigPath);
        }
        catch (Exception ex)
        {
            _log.Error("Open config failed", ex);
        }
    }

    private void OpenLog()
    {
        if (File.Exists(_log.FilePath))
            OpenPath(_log.FilePath);
        else
            OpenPath(Config.AppDataDir);
    }

    private void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to open {path}", ex);
            // Fallback: for files, try notepad.
            try
            {
                if (File.Exists(path))
                    Process.Start("notepad.exe", $"\"{path}\"");
            }
            catch { /* give up quietly */ }
        }
    }

    // -------------------------------------------------------------------------
    //  Helpers / lifecycle
    // -------------------------------------------------------------------------
    private void RunOnUi(Action action)
    {
        try
        {
            if (_sync.IsHandleCreated && _sync.InvokeRequired)
                _sync.BeginInvoke(action);
            else
                action();
        }
        catch (Exception ex)
        {
            _log.Error("UI marshal failed", ex);
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    private void ExitApp()
    {
        if (_exiting)
            return;
        _exiting = true;

        try { _notifyIcon.Visible = false; } catch { }
        try { _watcher.StatusChanged -= OnStatusChanged; } catch { }

        // Stop polling (cancels any in-flight socket call / delay).
        try { _watcher.StopAsync().GetAwaiter().GetResult(); } catch (Exception ex) { _log.Error("Watcher stop failed", ex); }

        // Clear + dispose Discord presence.
        try { _discord.Dispose(); } catch (Exception ex) { _log.Error("Discord dispose failed", ex); }

        try { _foundry.Dispose(); } catch { }
        try { _notifyIcon.Dispose(); } catch { }
        try { _icon.Dispose(); } catch { }

        _log.Info("Clean shutdown complete.");
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_exiting)
        {
            // Application.Run returned without a menu Quit (rare) — clean up.
            try { _watcher.StopAsync().GetAwaiter().GetResult(); } catch { }
            try { _discord.Dispose(); } catch { }
            try { _foundry.Dispose(); } catch { }
            try { _notifyIcon.Dispose(); } catch { }
            try { _icon.Dispose(); } catch { }
            try { _watcher.Dispose(); } catch { }
            try { _sync.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }
}
