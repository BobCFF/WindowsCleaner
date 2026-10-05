using WindowsCleaner.Core;

namespace WindowsCleaner.App;

public sealed class UninstallPage : UserControl
{
    private readonly AppManager _apps;
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(8, 8, 0, 0) };

    public UninstallPage(AppManager apps)
    {
        _apps = apps;
        Dock = DockStyle.Fill;
        _list.Columns.Add("Name", 320);
        _list.Columns.Add("Publisher", 200);
        _list.Columns.Add("Version", 100);
        _list.Columns.Add("Size", 90, HorizontalAlignment.Right);
        _list.Columns.Add("Installed", 90);
        _ = new ListViewColumnSorter(_list, (col, a, b) => col == 3
            ? ((InstalledApp)a.Tag!).SizeBytes.CompareTo(((InstalledApp)b.Tag!).SizeBytes) : null);

        var refresh = new Button { Text = "Refresh", AutoSize = true };
        var uninstall = new Button { Text = "Uninstall…", AutoSize = true };
        refresh.Click += async (_, _) => await Reload();
        uninstall.Click += OnUninstall;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        bar.Controls.AddRange([refresh, uninstall, _status]);
        Controls.Add(_list);
        Controls.Add(bar);
        HandleCreated += async (_, _) => await Reload();
    }

    private async Task Reload()
    {
        _status.Text = "Loading…";
        try
        {
            var apps = await Task.Run(_apps.List);
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var a in apps)
            {
                var row = new ListViewItem(a.Name) { Tag = a };
                row.SubItems.Add(a.Publisher);
                row.SubItems.Add(a.Version);
                row.SubItems.Add(a.SizeBytes > 0 ? SizeFormat.Format(a.SizeBytes) : "");
                row.SubItems.Add(a.InstallDateDisplay);
                _list.Items.Add(row);
            }
            _list.EndUpdate();
            _status.Text = $"{apps.Count} apps";
        }
        catch (Exception ex)
        {
            _status.Text = "Failed to load.";
            MessageBox.Show(this, ex.Message, "WindowsCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnUninstall(object? sender, EventArgs e)
    {
        if (_list.SelectedItems.Count == 0) return;
        var app = (InstalledApp)_list.SelectedItems[0].Tag!;
        if (MessageBox.Show(this, $"Run the uninstaller for \"{app.Name}\"?", "WindowsCleaner",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { _apps.Uninstall(app); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "WindowsCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
