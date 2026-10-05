using WindowsCleaner.Core;

namespace WindowsCleaner.App;

/// <summary>Analyze → tick items → run. Used for both the junk/browser page and the registry page.</summary>
public sealed class CleanerPage : UserControl
{
    private sealed record Entry(ICleaner Cleaner, CleanItem Item);

    private readonly IReadOnlyList<ICleaner> _cleaners;
    private readonly string _confirmNote;
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true,
        ShowGroups = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
    };
    private readonly Button _analyze = new() { Text = "Analyze", AutoSize = true };
    private readonly Button _run = new() { AutoSize = true, Enabled = false };
    private readonly ProgressBar _progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Width = 110 };
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(8, 8, 0, 0) };

    public CleanerPage(IReadOnlyList<ICleaner> cleaners, string runLabel, string confirmNote)
    {
        _cleaners = cleaners;
        _confirmNote = confirmNote;
        _run.Text = runLabel;
        Dock = DockStyle.Fill;

        _list.Columns.Add("Item", 520);
        _list.Columns.Add("Size", 100, HorizontalAlignment.Right);
        _list.ItemChecked += (_, _) => UpdateSummary();
        _analyze.Click += OnAnalyze;
        _run.Click += OnRun;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        bar.Controls.AddRange([_analyze, _run, _progress, _status]);
        Controls.Add(_list);   // Fill first, then Top, so docking gives Top its strip and Fill the rest
        Controls.Add(bar);
    }

    private void SetBusy(bool busy, string? text = null)
    {
        _analyze.Enabled = !busy;
        _run.Enabled = !busy && _list.CheckedItems.Count > 0;
        _progress.Visible = busy;
        if (text is not null) _status.Text = text;
    }

    private IEnumerable<Entry> Checked() =>
        _list.CheckedItems.Cast<ListViewItem>().Select(i => (Entry)i.Tag!);

    private void UpdateSummary()
    {
        var chosen = Checked().ToList();
        _run.Enabled = _analyze.Enabled && chosen.Count > 0;
        var total = chosen.Sum(e => e.Item.SizeBytes);
        _status.Text = chosen.Count == 0 ? "" : total > 0
            ? $"{chosen.Count} selected, {SizeFormat.Format(total)}"
            : $"{chosen.Count} selected";
    }

    private async void OnAnalyze(object? sender, EventArgs e)
    {
        SetBusy(true, "Analyzing…");
        _list.Items.Clear();
        _list.Groups.Clear();
        try
        {
            foreach (var cleaner in _cleaners)
            {
                var items = await cleaner.ScanAsync();
                _list.BeginUpdate();
                foreach (var item in items)
                {
                    var group = _list.Groups.Cast<ListViewGroup>().FirstOrDefault(g => g.Header == item.Category);
                    if (group is null) { group = new ListViewGroup(item.Category); _list.Groups.Add(group); }
                    var row = new ListViewItem(item.Description, group) { Tag = new Entry(cleaner, item) };
                    row.SubItems.Add(item.SizeBytes > 0 ? SizeFormat.Format(item.SizeBytes) : "–");
                    row.Checked = item.SelectedByDefault;
                    _list.Items.Add(row);
                }
                _list.EndUpdate();
            }
            SetBusy(false);
            UpdateSummary();
            if (_list.Items.Count == 0) _status.Text = "Nothing to clean.";
        }
        catch (Exception ex)
        {
            SetBusy(false, "Analyze failed.");
            MessageBox.Show(this, ex.Message, "WindowsCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnRun(object? sender, EventArgs e)
    {
        var chosen = Checked().ToList();
        if (chosen.Count == 0) return;
        var total = chosen.Sum(x => x.Item.SizeBytes);
        var size = total > 0 ? $", {SizeFormat.Format(total)}" : "";
        var answer = MessageBox.Show(this,
            $"{chosen.Count} items{size}.\n\n{_confirmNote}\n\nContinue?",
            "WindowsCleaner", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;

        SetBusy(true, "Cleaning…");
        try
        {
            var progress = new Progress<string>(s => _status.Text = s);
            var result = CleanResult.Empty;
            foreach (var g in chosen.GroupBy(x => x.Cleaner))
                result = result.Plus(await g.Key.CleanAsync(g.Select(x => x.Item), progress));

            var msg = $"Removed {result.Deleted} items, freed {SizeFormat.Format(result.BytesFreed)}.";
            if (result.Skipped > 0) msg += $"\nSkipped {result.Skipped} (in use or protected).";
            if (result.Errors.Count > 0) msg += "\n\n" + string.Join("\n", result.Errors.Take(5));
            _list.Items.Clear();
            _list.Groups.Clear();
            SetBusy(false, "Done. Analyze again to refresh.");
            MessageBox.Show(this, msg, "WindowsCleaner", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetBusy(false, "Clean failed.");
            MessageBox.Show(this, ex.Message, "WindowsCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
