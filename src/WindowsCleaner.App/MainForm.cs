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
    private readonly MenuStrip _menu = new();
    private readonly ToolStripMenuItem _navLeftItem = new("Navigation: Left");
    private readonly ToolStripMenuItem _navTopItem = new("Navigation: Top");
    private readonly AppSettings _settings;

    public MainForm()
    {
        Text = "WindowsCleaner";
        Size = new Size(1000, 660);
        MinimumSize = new Size(820, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        var settings = AppSettings.Load();
        _settings = settings;

        // Docking z-order: the control added last docks first, so Fill goes in first, then nav, then the menu.
        Controls.Add(_content);
        Controls.Add(_nav);
        BuildMenu();
        Controls.Add(_menu);
        MainMenuStrip = _menu;
        ApplyNavPosition(settings.NavPosition);

        var reg = new RegistryAccess();
        var registryCleaner = new RegistryCleaner(reg, PathProbe.Exists, AppPaths.BackupsDir);

        AddPage("Cleaner", new CleanerPage(
            [JunkCleaner.CreateDefault(settings), BrowserCleaner.CreateDefault()],
            "Run Cleaner",
            "Selected files will be permanently deleted."));
        AddPage("Registry", new CleanerPage(
            [registryCleaner],
            "Fix selected issues",
            "A .reg backup of every affected key is saved first (Settings → Restore)."));

        AddPage("Startup", new StartupPage(StartupManager.CreateDefault(reg)));
        AddPage("Uninstall", new UninstallPage(new AppManager(reg)));
        AddPage("Settings", new SettingsPage(settings, registryCleaner));

        Show("Cleaner");
    }

    private void BuildMenu()
    {
        _navLeftItem.Click += (_, _) => ChooseNavPosition(NavPosition.Left);
        _navTopItem.Click += (_, _) => ChooseNavPosition(NavPosition.Top);
        var view = new ToolStripMenuItem("View");
        view.DropDownItems.AddRange([_navLeftItem, _navTopItem]);
        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => Close();
        var file = new ToolStripMenuItem("File");
        file.DropDownItems.AddRange([view, new ToolStripSeparator(), exit]);
        _menu.Items.Add(file);
    }

    private void ChooseNavPosition(NavPosition position)
    {
        ApplyNavPosition(position);
        _settings.NavPosition = position;
        try { _settings.Save(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not save the navigation setting: " + ex.Message,
                "WindowsCleaner", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // Reconfigures the same _nav panel (and its same button instances); pages and the shown page are untouched.
    private void ApplyNavPosition(NavPosition position)
    {
        _nav.SuspendLayout();
        if (position == NavPosition.Top)
        {
            _nav.Dock = DockStyle.Top;
            _nav.FlowDirection = FlowDirection.LeftToRight;
            _nav.WrapContents = false;
            _nav.Height = 56;
        }
        else
        {
            _nav.Dock = DockStyle.Left;
            _nav.FlowDirection = FlowDirection.TopDown;
            _nav.WrapContents = false;
            _nav.Width = 150;
        }
        _nav.ResumeLayout(true);
        _navLeftItem.Checked = position == NavPosition.Left;
        _navTopItem.Checked = position == NavPosition.Top;
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
