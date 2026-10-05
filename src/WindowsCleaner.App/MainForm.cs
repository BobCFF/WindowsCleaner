using WindowsCleaner.Core;

namespace WindowsCleaner.App;

public sealed class MainForm : Form
{
    private readonly FlowLayoutPanel _nav = new()
    {
        Dock = DockStyle.Left, Width = 150, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, Padding = new Padding(6),
    };
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<string, Control> _pages = [];

    public MainForm()
    {
        Text = "WindowsCleaner";
        Size = new Size(1000, 660);
        MinimumSize = new Size(820, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        Controls.Add(_content);
        Controls.Add(_nav);

        var settings = AppSettings.Load();
        var reg = new RegistryAccess();
        var registryCleaner = new RegistryCleaner(reg, p => File.Exists(p) || Directory.Exists(p), AppPaths.BackupsDir);

        AddPage("Cleaner", new CleanerPage(
            [JunkCleaner.CreateDefault(settings), BrowserCleaner.CreateDefault()],
            "Run Cleaner",
            "Selected files will be permanently deleted."));
        AddPage("Registry", new CleanerPage(
            [registryCleaner],
            "Fix selected issues",
            "A .reg backup of every affected key is saved first (Settings → Restore)."));

        Show("Cleaner");
    }

    public void AddPage(string title, Control page)
    {
        _pages[title] = page;
        var button = new Button { Text = title, Width = 130, Height = 36, FlatStyle = FlatStyle.System };
        button.Click += (_, _) => Show(title);
        _nav.Controls.Add(button);
    }

    private void Show(string title)
    {
        _content.Controls.Clear();
        _content.Controls.Add(_pages[title]);
    }
}
