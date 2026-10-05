using System.Diagnostics;
using WindowsCleaner.Core;

namespace WindowsCleaner.App;

public sealed class SettingsPage : UserControl
{
    public SettingsPage(AppSettings settings, RegistryCleaner registry)
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(12);

        var days = new NumericUpDown { Minimum = 0, Maximum = 365, Value = settings.LogRetentionDays, Width = 70 };
        var save = new Button { Text = "Save", AutoSize = true };
        save.Click += (_, _) =>
        {
            settings.LogRetentionDays = (int)days.Value;
            settings.Save();
            MessageBox.Show(this, "Saved. Restart the app for this to take effect.", "WindowsCleaner");
        };

        var openBackups = new Button { Text = "Open backup folder", AutoSize = true };
        openBackups.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppPaths.BackupsDir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.BackupsDir}\"") { UseShellExecute = true });
        };

        var restore = new Button { Text = "Restore registry backup…", AutoSize = true };
        restore.Click += (_, _) =>
        {
            Directory.CreateDirectory(AppPaths.BackupsDir);
            using var dialog = new FolderBrowserDialog
            {
                Description = "Choose a backup folder to restore",
                InitialDirectory = AppPaths.BackupsDir,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (MessageBox.Show(this, $"Import all .reg files from\n{dialog.SelectedPath}?", "WindowsCleaner",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                registry.Restore(dialog.SelectedPath);
                MessageBox.Show(this, "Backup restored.", "WindowsCleaner");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "WindowsCleaner", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };

        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        panel.Controls.Add(new Label { Text = "Delete log files older than (days):", AutoSize = true });
        panel.Controls.Add(days);
        panel.Controls.Add(save);
        panel.Controls.Add(new Label { Text = " ", AutoSize = true });
        panel.Controls.Add(new Label { Text = $"Registry backups: {AppPaths.BackupsDir}", AutoSize = true });
        panel.Controls.Add(openBackups);
        panel.Controls.Add(restore);
        Controls.Add(panel);
    }
}
