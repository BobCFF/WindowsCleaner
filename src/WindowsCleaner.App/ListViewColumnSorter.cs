using System.Collections;

namespace WindowsCleaner.App;

/// <summary>Click-to-sort support for a details ListView. Toggles direction on repeated clicks.</summary>
public sealed class ListViewColumnSorter : IComparer
{
    private readonly ListView _list;
    private readonly Func<int, ListViewItem, ListViewItem, int?>? _custom;
    private int _column = -1;
    private bool _ascending = true;

    /// <param name="custom">Optional per-column comparer; return null to fall back to text compare.</param>
    public ListViewColumnSorter(ListView list, Func<int, ListViewItem, ListViewItem, int?>? custom = null)
    {
        _list = list;
        _custom = custom;
        list.ListViewItemSorter = this;
        list.ColumnClick += (_, e) =>
        {
            _ascending = e.Column != _column || !_ascending;
            _column = e.Column;
            list.Sort();
        };
    }

    public int Compare(object? x, object? y)
    {
        if (_column < 0 || x is not ListViewItem a || y is not ListViewItem b) return 0;
        var r = _custom?.Invoke(_column, a, b)
            ?? string.Compare(Text(a), Text(b), StringComparison.OrdinalIgnoreCase);
        return _ascending ? r : -r;
    }

    private string Text(ListViewItem i) => _column < i.SubItems.Count ? i.SubItems[_column].Text : "";
}
