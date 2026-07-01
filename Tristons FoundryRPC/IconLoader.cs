// =============================================================================
//  Triston's FoundryRPC  —  IconLoader.cs
//  Loads the app icon that is embedded into the assembly (so it works even from
//  a single-file self-contained publish, where there is no loose app.ico on disk).
//
//  Author:  triston-dev   ·   https://github.com/triston-dev
//  Product: Triston's FoundryRPC
//  License: MIT (see LICENSE)
// =============================================================================

using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace TristonsFoundryRPC;

public static class IconLoader
{
    /// <summary>
    /// Load the tray icon at the OS small-icon size. Falls back to the exe's
    /// associated icon, then to a system icon, so this never returns null.
    /// </summary>
    public static Icon LoadTrayIcon(Logger? log = null)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames()
                             .FirstOrDefault(n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
            if (resName is not null)
            {
                using var stream = asm.GetManifestResourceStream(resName);
                if (stream is not null)
                {
                    var size = SystemInformation.SmallIconSize; // typically 16x16 (DPI-scaled)
                    return new Icon(stream, size);
                }
            }
        }
        catch (Exception ex)
        {
            log?.Error("Failed to load embedded tray icon", ex);
        }

        try
        {
            var exeIcon = Icon.ExtractAssociatedIcon(StartupManager.ExecutablePath);
            if (exeIcon is not null)
                return exeIcon;
        }
        catch { /* fall through */ }

        return SystemIcons.Application;
    }
}
