using WindowsCleaner.Core;

namespace WindowsCleaner.App;

public sealed class MainForm : Form
{
    private readonly NavToolbar _nav = new();
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<string, Control> _pages = [];
    private readonly MenuStrip _menu = new();

    public MainForm()
    {
        Text = "WindowsCleaner";
        Size = new Size(1000, 660);
        MinimumSize = new Size(820, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        var settings = AppSettings.Load();

        // Docking z-order: the control added last docks first, so Fill goes in first, then the toolbar, then the menu.
        Controls.Add(_content);
        Controls.Add(_nav);
        BuildMenu();
        Controls.Add(_menu);
        MainMenuStrip = _menu;
        _nav.PageSelected += (_, title) => Show(title);

        var reg = new RegistryAccess();
        var registryCleaner = new RegistryCleaner(reg, PathProbe.Exists, AppPaths.BackupsDir);

        AddPage("Cleaner", "", new CleanerPage(
            [JunkCleaner.CreateDefault(settings), BrowserCleaner.CreateDefault()],
            "Run Cleaner",
            "Selected files will be permanently deleted."));
        AddPage("Registry", "", new CleanerPage(
            [registryCleaner],
            "Fix selected issues",
            "A .reg backup of every affected key is saved first (Settings → Restore)."));

        AddPage("Startup", "", new StartupPage(StartupManager.CreateDefault(reg)));
        AddPage("Uninstall", "", new UninstallPage(new AppManager(reg)));
        AddPage("Settings", "", new SettingsPage(settings, registryCleaner));
        AddPage("About", "", new AboutPage(), alignRight: true);

        Show("Cleaner");
    }

    private void BuildMenu()
    {
        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => Close();
        var file = new ToolStripMenuItem("File");
        file.DropDownItems.Add(exit);
        _menu.Items.Add(file);
    }

    public void AddPage(string title, string glyph, Control page, bool alignRight = false)
    {
        _pages[title] = page;
        _nav.AddPage(title, glyph, alignRight);
    }

    private void Show(string title)
    {
        _content.Controls.Clear();
        _content.Controls.Add(_pages[title]);
        _nav.Select(title);
    }
}
