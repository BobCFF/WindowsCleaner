using WindowsCleaner.Core;

namespace WindowsCleaner.App;

public sealed class StartupPage : UserControl
{
    private readonly StartupManager _manager;
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(8, 8, 0, 0) };

    public StartupPage(StartupManager manager)
    {
        _manager = manager;
        Dock = DockStyle.Fill;
        _list.Columns.Add("Name", 200);
        _list.Columns.Add("Status", 80);
        _list.Columns.Add("Source", 120);
        _list.Columns.Add("Command", 420);
        _ = new ListViewColumnSorter(_list);

        var refresh = new Button { Text = "Refresh", AutoSize = true };
        var enable = new Button { Text = "Enable", AutoSize = true };
        var disable = new Button { Text = "Disable", AutoSize = true };
        refresh.Click += async (_, _) => await Reload();
        enable.Click += async (_, _) => await Toggle(true);
        disable.Click += async (_, _) => await Toggle(false);

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        bar.Controls.AddRange([refresh, enable, disable, _status]);
        Controls.Add(_list);
        Controls.Add(bar);
        HandleCreated += async (_, _) => await Reload();
    }

    private async Task Reload()
    {
        _status.Text = "Loading…";
        try
        {
            var entries = await Task.Run(_manager.List);
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var e in entries)
            {
                var row = new ListViewItem(e.Name) { Tag = e };
                row.SubItems.Add(e.Enabled ? "Enabled" : "Disabled");
                row.SubItems.Add(e.Source);
                row.SubItems.Add(e.Command);
                _list.Items.Add(row);
            }
            _list.EndUpdate();
            _status.Text = $"{entries.Count} startup items";
        }
        catch (Exception ex)
        {
            _status.Text = "Failed to load.";
            MessageBox.Show(this, ex.Message, "WindowsCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task Toggle(bool enable)
    {
        if (_list.SelectedItems.Count == 0) return;
        var entry = (StartupEntry)_list.SelectedItems[0].Tag!;
        try { await Task.Run(() => _manager.SetEnabled(entry, enable)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "WindowsCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        await Reload();
    }
}
