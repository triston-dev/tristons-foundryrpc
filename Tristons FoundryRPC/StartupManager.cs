// =============================================================================
//  Triston's FoundryRPC  —  StartupManager.cs
//  "Run at Windows startup" via a shortcut in the user's Startup folder
//  (shell:startup). Explorer launches these at logon in the normal interactive
//  context — chosen over the HKCU Run key after real-world debugging showed
//  Run-key/Task-Scheduler launches on some machines either silently not firing
//  or running the app with file writes discarded by security software.
//  Also migrates away from (deletes) the legacy Run-key entry older builds set.
//
//  Author:  triston-dev   ·   https://github.com/triston-dev
//  Product: Triston's FoundryRPC
//  License: MIT (see LICENSE)
// =============================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32;

namespace TristonsFoundryRPC;

public static class StartupManager
{
    private const string ShortcutName = "Tristons FoundryRPC.lnk";

    // Legacy (pre-1.0.1) mechanism — removed on any toggle so we never have both.
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Tristons FoundryRPC";

    /// <summary>Full path to the running executable (works for single-file publish).</summary>
    public static string ExecutablePath =>
        Environment.ProcessPath
        ?? Process.GetCurrentProcess().MainModule?.FileName
        ?? System.Windows.Forms.Application.ExecutablePath;

    private static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName);

    public static bool IsEnabled(Logger? log = null)
    {
        try
        {
            return File.Exists(ShortcutPath);
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
        RemoveLegacyRunKey(log);

        try
        {
            if (enabled)
            {
                CreateShortcut(ShortcutPath, ExecutablePath);
                log?.Info($"Enabled run-at-startup: {ShortcutPath} -> {ExecutablePath}");
            }
            else if (File.Exists(ShortcutPath))
            {
                File.Delete(ShortcutPath);
                log?.Info("Disabled run-at-startup (shortcut removed).");
            }
            return IsEnabled(log);
        }
        catch (Exception ex)
        {
            log?.Error("StartupManager.SetEnabled failed", ex);
            return IsEnabled(log);
        }
    }

    /// <summary>Delete the HKCU Run entry older versions created (best-effort).</summary>
    private static void RemoveLegacyRunKey(Logger? log)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(RunValueName) is not null)
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
                log?.Info("Removed legacy Run-key startup entry (migrated to Startup folder).");
            }
        }
        catch (Exception ex)
        {
            log?.Error("Legacy Run-key cleanup failed", ex);
        }
    }

    // -------------------------------------------------------------------------
    //  Minimal IShellLink COM interop — writes a .lnk without extra dependencies.
    // -------------------------------------------------------------------------
    private static void CreateShortcut(string lnkPath, string targetExe)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(targetExe);
        link.SetWorkingDirectory(Path.GetDirectoryName(targetExe) ?? "");
        link.SetDescription("Triston's FoundryRPC — Discord Rich Presence for Foundry VTT");

        var file = (IPersistFile)link;
        file.Save(lnkPath, true);
        Marshal.ReleaseComObject(link);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile,
                     int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath,
                             int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
