using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using WindowsCleaner.Core;

namespace WindowsCleaner.App;

public sealed class AboutPage : UserControl
{
    private const string RepoUrl = "https://github.com/BobCFF/WindowsCleaner";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly Button _check = new() { Text = "Check for updates", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(600, 0) };
    private readonly LinkLabel _download = new() { Text = "Open download page", AutoSize = true, Visible = false };
    private readonly Version _current;
    private string? _releaseUrl;

    public AboutPage()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(24);

        var infoVersion = typeof(AboutPage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var versionText = StripBuild(infoVersion) ?? typeof(AboutPage).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        _current = Version.TryParse(versionText.Split('-')[0], out var v) ? v : new Version(1, 0, 0);

        var title = new Label { Text = "WindowsCleaner", AutoSize = true, Font = new Font(Font.FontFamily, 20f, FontStyle.Bold) };
        var version = new Label { Text = "Version " + versionText, AutoSize = true, Margin = new Padding(3, 0, 3, 12) };
        var blurb = new Label
        {
            Text = "A Windows 11 cleaner: junk files, registry leftovers, startup items and uninstallers.",
            AutoSize = true, Margin = new Padding(3, 0, 3, 12),
        };
        var link = new LinkLabel { Text = RepoUrl, AutoSize = true, Margin = new Padding(3, 0, 3, 18) };
        link.LinkClicked += (_, _) => Open(RepoUrl);

        _check.Click += async (_, _) => await RunCheckAsync();
        _download.LinkClicked += (_, _) =>
        {
            if (_releaseUrl is not null && UpdateChecker.IsTrustedReleaseUrl(_releaseUrl, out var safe)) Open(safe);
        };

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        };
        panel.Controls.AddRange([title, version, blurb, link, _check, _status, _download]);
        Controls.Add(panel);
    }

    private static string? StripBuild(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var i = s.IndexOf('+');
        return i >= 0 ? s[..i] : s;
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no default browser: nothing sensible to do */ }
    }

    private async Task RunCheckAsync()
    {
        _check.Enabled = false;
        _download.Visible = false;
        _releaseUrl = null;
        _status.Text = "Checking...";
        try
        {
            var result = await new UpdateChecker(Http).CheckAsync(_current, CancellationToken.None);
            switch (result)
            {
                case UpdateResult.UpToDate:
                    _status.Text = $"You're up to date (v{_current.ToString(3)})";
                    break;
                case UpdateResult.UpdateAvailable u:
                    _status.Text = $"Update available: v{u.Latest}";
                    _releaseUrl = u.ReleaseUrl;
                    _download.Visible = true;
                    break;
                case UpdateResult.NoReleases:
                    _status.Text = "No releases published yet";
                    break;
                case UpdateResult.Failed f:
                    _status.Text = "Couldn't check for updates: " + f.Message;
                    break;
            }
        }
        catch (Exception ex)
        {
            _status.Text = "Couldn't check for updates: " + ex.Message;
        }
        finally
        {
            if (!IsDisposed) _check.Enabled = true;
        }
    }
}
