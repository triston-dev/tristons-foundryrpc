// =============================================================================
//  Triston's FoundryRPC  —  Dialogs.cs
//  Small code-only WinForms dialogs: bridge host/port editor and the About box
//  (author attribution + clickable GitHub link).
//
//  Author:  triston-dev   ·   https://github.com/triston-dev
//  Product: Triston's FoundryRPC
//  License: MIT (see LICENSE)
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TristonsFoundryRPC;

/// <summary>Modal editor for the list of Foundry server base URLs.</summary>
public sealed class ServersConfigForm : Form
{
    private readonly TextBox _urls = new();

    /// <summary>Non-empty, trimmed URLs, one per textbox line.</summary>
    public List<string> ServerUrls =>
        _urls.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    public ServersConfigForm(IEnumerable<string> current)
    {
        Text = "Configure Foundry Server URLs";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(460, 260);
        Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;

        var info = new Label
        {
            Text = "One Foundry server URL per line (the address you open to play).\n" +
                   "Examples:  https://my-world.sqyre.app   ·   http://localhost:30000\n" +
                   "All are checked each poll; the first with an active world is shown.",
            Left = 12, Top = 12, Width = 436, Height = 52, AutoSize = false,
        };

        _urls.Left = 12; _urls.Top = 70; _urls.Width = 436; _urls.Height = 140;
        _urls.Multiline = true;
        _urls.ScrollBars = ScrollBars.Vertical;
        _urls.AcceptsReturn = true;
        _urls.WordWrap = false;
        _urls.Lines = current.ToArray();

        var ok = new Button { Text = "Save", Left = 276, Top = 222, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 368, Top = 222, Width = 80, DialogResult = DialogResult.Cancel };

        AcceptButton = ok;
        CancelButton = cancel;

        Controls.AddRange(new Control[] { info, _urls, ok, cancel });
    }
}

/// <summary>About box with author credit and a clickable GitHub link.</summary>
public sealed class AboutForm : Form
{
    private const string AuthorUrl = "https://github.com/triston-dev";

    public AboutForm(string versionText)
    {
        Text = "About Triston's FoundryRPC";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(400, 210);
        Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;

        try { Icon = IconLoader.LoadTrayIcon(); } catch { /* non-fatal */ }

        var title = new Label
        {
            Text = "Triston's FoundryRPC",
            Font = new Font(Font.FontFamily, 12f, FontStyle.Bold),
            Left = 16, Top = 16, Width = 368, Height = 26, AutoSize = false,
        };

        var desc = new Label
        {
            Text = "Discord Rich Presence for the Foundry VTT world you are running.\n" + versionText,
            Left = 16, Top = 48, Width = 368, Height = 48, AutoSize = false,
        };

        var authorLabel = new Label
        {
            Text = "Author:  triston-dev",
            Left = 16, Top = 104, Width = 368, Height = 22, AutoSize = false,
        };

        var link = new LinkLabel
        {
            Text = AuthorUrl,
            Left = 16, Top = 128, Width = 368, Height = 22, AutoSize = false,
        };
        link.LinkClicked += (_, _) => OpenUrl(AuthorUrl);

        var ok = new Button { Text = "Close", Left = 304, Top = 168, Width = 80, DialogResult = DialogResult.OK };
        AcceptButton = ok;
        CancelButton = ok;

        Controls.AddRange(new Control[] { title, desc, authorLabel, link, ok });
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // If the shell cannot open a browser, silently ignore.
        }
    }
}
