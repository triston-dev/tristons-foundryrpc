// =============================================================================
//  Triston's FoundryRPC  —  StartupManager.cs
//  "Run at Windows startup" via the per-user Run key
//  (HKCU\Software\Microsoft\Windows\CurrentVersion\Run). No elevation needed.
//
//  Author:  triston-dev   ·   https://github.com/triston-dev
//  Product: Triston's FoundryRPC
//  License: MIT (see LICENSE)
// =============================================================================

using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace TristonsFoundryRPC;

public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Tristons FoundryRPC";

    /// <summary>Full path to the running executable (works for single-file publish).</summary>
    public static string ExecutablePath =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? System.Windows.Forms.Application.ExecutablePath;

    public static bool IsEnabled(Logger? log = null)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is not null;
        }
        catch (Exception ex)
        {
            log?.Error("StartupManager.IsEnabled failed", ex);
            return false;
        }
    }

    /// <summary>Enable or disable auto-start. Returns the resulting state.</summary>
    public static bool SetEnabled(bool enabled, Logger? log = null)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null)
                return IsEnabled(log);

            if (enabled)
            {
                // Quote the path so spaces (e.g. "Tristons FoundryRPC.exe") survive.
                key.SetValue(ValueName, $"\"{ExecutablePath}\"");
                log?.Info($"Enabled run-at-startup: {ExecutablePath}");
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                log?.Info("Disabled run-at-startup.");
            }
            return enabled;
        }
        catch (Exception ex)
        {
            log?.Error("StartupManager.SetEnabled failed", ex);
            return IsEnabled(log);
        }
    }
}
