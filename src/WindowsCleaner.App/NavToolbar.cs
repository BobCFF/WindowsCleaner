using System.Drawing.Text;

namespace WindowsCleaner.App;

/// <summary>Horizontal icon toolbar with single selection. Icons are glyphs rendered from Segoe Fluent Icons / MDL2 Assets.</summary>
public sealed class NavToolbar : ToolStrip
{
    private const int IconSize = 32;
    private readonly List<(ToolStripButton Button, string Glyph)> _items = [];
    private readonly string? _iconFont = FindIconFont();

    public event EventHandler<string>? PageSelected;

    public NavToolbar()
    {
        Dock = DockStyle.Top;
        GripStyle = ToolStripGripStyle.Hidden;
        ImageScalingSize = new Size(Scale(IconSize), Scale(IconSize));
        Padding = new Padding(Scale(6), Scale(2), Scale(6), Scale(2));
        Stretch = true;
    }

    private static string? FindIconFont()
    {
        using var fonts = new InstalledFontCollection();
        var names = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Contains("Segoe Fluent Icons")) return "Segoe Fluent Icons";
        if (names.Contains("Segoe MDL2 Assets")) return "Segoe MDL2 Assets";
        return null; // text-only fallback
    }

    public void AddPage(string title, string glyph, bool alignRight = false)
    {
        var button = new ToolStripButton(title)
        {
            ImageScaling = ToolStripItemImageScaling.None,
            TextImageRelation = TextImageRelation.ImageAboveText,
            DisplayStyle = _iconFont is null ? ToolStripItemDisplayStyle.Text : ToolStripItemDisplayStyle.ImageAndText,
            AutoSize = false,
            Size = ButtonSize(),
            Alignment = alignRight ? ToolStripItemAlignment.Right : ToolStripItemAlignment.Left,
            Margin = new Padding(1),
        };
        button.Click += (_, _) => Select(title, raise: true);
        _items.Add((button, glyph));
        Items.Add(button);
        ApplyIcon(button, glyph);
    }

    // Absolute (not incremental) sizing from the current DPI, so re-applying after a DPI change is idempotent.
    private int Scale(int px) => (int)Math.Round(px * DeviceDpi / 96.0);
    private Size ButtonSize() => new(Scale(84), Scale(62));

    public void Select(string title, bool raise = false)
    {
        foreach (var (b, _) in _items) b.Checked = b.Text == title;
        if (raise) PageSelected?.Invoke(this, title);
    }

    private void ApplyIcon(ToolStripButton button, string glyph)
    {
        var old = button.Image;
        button.Image = _iconFont is null ? null : RenderGlyph(glyph);
        old?.Dispose();
    }

    private Bitmap RenderGlyph(string glyph)
    {
        var px = Math.Max(16, (int)Math.Round(IconSize * DeviceDpi / 96.0));
        var bmp = new Bitmap(px, px);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.Clear(Color.Transparent);
        using var font = new Font(_iconFont!, px * 0.62f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var brush = new SolidBrush(SystemColors.ControlText);
        g.DrawString(glyph, font, brush, new RectangleF(0, 0, px, px), format);
        return bmp;
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ImageScalingSize = new Size(Scale(IconSize), Scale(IconSize));
        foreach (var (b, glyph) in _items)
        {
            b.Size = ButtonSize();
            ApplyIcon(b, glyph);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (var (b, _) in _items) b.Image?.Dispose();
        base.Dispose(disposing);
    }
}
