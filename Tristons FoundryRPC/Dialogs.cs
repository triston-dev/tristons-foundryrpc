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
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace TristonsFoundryRPC;

/// <summary>Modal editor for the bridge host and port.</summary>
public sealed class BridgeConfigForm : Form
{
    private readonly TextBox _host = new();
    private readonly NumericUpDown _port = new();

    public string BridgeHost => _host.Text.Trim();
    public int BridgePort => (int)_port.Value;

    public BridgeConfigForm(string host, int port)
    {
        Text = "Configure Foundry Bridge";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(360, 170);
        Font = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;

        var info = new Label
        {
            Text = "The MCP bridge control channel (Triston's Bridge Fork).\nDefault: 127.0.0.1 : 31414",
            Left = 12, Top = 12, Width = 336, Height = 36, AutoSize = false,
        };

        var hostLabel = new Label { Text = "Host:", Left = 12, Top = 60, Width = 50, TextAlign = ContentAlignment.MiddleLeft };
        _host.Left = 70; _host.Top = 56; _host.Width = 278; _host.Text = host;

        var portLabel = new Label { Text = "Port:", Left = 12, Top = 96, Width = 50, TextAlign = ContentAlignment.MiddleLeft };
        _port.Left = 70; _port.Top = 92; _port.Width = 100;
        _port.Minimum = 1; _port.Maximum = 65535; _port.Value = Math.Clamp(port, 1, 65535);

        var ok = new Button { Text = "Save", Left = 176, Top = 130, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 268, Top = 130, Width = 80, DialogResult = DialogResult.Cancel };

        AcceptButton = ok;
        CancelButton = cancel;

        Controls.AddRange(new Control[] { info, hostLabel, _host, portLabel, _port, ok, cancel });
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
