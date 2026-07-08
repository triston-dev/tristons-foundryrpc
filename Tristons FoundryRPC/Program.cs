// =============================================================================
//  Triston's FoundryRPC  —  Program.cs
//  Entry point: single-instance guard (mutex), config + logger bootstrap, and
//  the WinForms message loop hosting the tray app. Built as a WinExe, so there
//  is no console window.
//
//  Author:  triston-dev   ·   https://github.com/triston-dev
//  Product: Triston's FoundryRPC
//  License: MIT (see LICENSE)
// =============================================================================

using System;
using System.Threading;
using System.Windows.Forms;

namespace TristonsFoundryRPC;

internal static class Program
{
    // Per-user, per-session single-instance guard.
    private const string MutexName = @"Local\TristonsFoundryRPC_SingleInstance";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            try
            {
                MessageBox.Show(
                    "Triston's FoundryRPC is already running (see the system tray).",
                    "Triston's FoundryRPC",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch { /* headless edge case */ }
            return;
        }

        // Logger first (default level), then config, then align the level.
        var log = new Logger(Config.LogPath, LogLevel.Info);
        log.Info("==================== Triston's FoundryRPC starting ====================");

        // Tripwire: verify the banner actually reached the disk. During real-world
        // debugging we hit an environment where every file write from this process
        // silently vanished (no exception, no bytes) — likely security software.
        // Without this check that failure mode is invisible.
        try
        {
            if (!System.IO.File.Exists(Config.LogPath) ||
                DateTime.Now - System.IO.File.GetLastWriteTime(Config.LogPath) > TimeSpan.FromMinutes(1))
            {
                MessageBox.Show(
                    "Triston's FoundryRPC cannot write its settings/log files in " +
                    $"{Config.AppDataDir}.\n\nSettings changes will not be saved. This is " +
                    "usually caused by security software blocking the app — add an exclusion " +
                    "for it and start it again.",
                    "Triston's FoundryRPC",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch { /* the tripwire itself must never take the app down */ }

        Config config;
        try
        {
            config = Config.Load(log);
            log.Level = MapLevel(config.LogVerbosity);
            log.Info($"Config loaded. Bridge={config.BridgeHost}:{config.BridgePort}, " +
                     $"poll={config.PollIntervalSeconds}s, rpcEnabled={config.RpcEnabled}, " +
                     $"appId={config.DiscordApplicationId}.");
        }
        catch (Exception ex)
        {
            log.Error("Failed to load config", ex);
            config = new Config();
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { /* manifest also sets this */ }

        TrayApp? app = null;
        try
        {
            app = new TrayApp(config, log);
            Application.Run(app);
        }
        catch (Exception ex)
        {
            log.Error("Fatal error in message loop", ex);
            try
            {
                MessageBox.Show(
                    $"Triston's FoundryRPC hit a fatal error:\n\n{ex.Message}\n\nSee the log:\n{Config.LogPath}",
                    "Triston's FoundryRPC",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch { }
        }
        finally
        {
            app?.Dispose();
            log.Info("==================== Triston's FoundryRPC exited ====================");
            GC.KeepAlive(mutex); // keep the guard alive for the whole run
        }
    }

    private static LogLevel MapLevel(LogVerbosity v) => v switch
    {
        LogVerbosity.Error => LogLevel.Error,
        LogVerbosity.Debug => LogLevel.Debug,
        _ => LogLevel.Info,
    };
}
