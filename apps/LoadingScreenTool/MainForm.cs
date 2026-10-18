using System.Text.Json;

namespace BLRP.LoadingScreenTool;

internal sealed class MainForm : Form
{
    private readonly TextBox _project = new() { Dock = DockStyle.Fill };
    private readonly TextBox _active = new() { Dock = DockStyle.Fill };
    private readonly Button _process = new() { Text = "PROCESS IMAGES", Dock = DockStyle.Fill, Height = 40 };
    private readonly Button _export = new() { Text = "EXPORT TO ACTIVE FOLDER", Dock = DockStyle.Fill, Height = 40 };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BadlandsRP", "LoadingScreenTool", "settings.json");

    private sealed record Settings(string ProjectFolder, string ActiveFolder);

    internal MainForm()
    {
        Text = "Loading Screen Tool";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 410);
        ClientSize = new Size(820, 460);
        Font = new Font("Cascadia Mono", 9F);
        BackColor = Color.FromArgb(12, 12, 28);
        ForeColor = Color.White;

        Settings saved = LoadSettings();
        _project.Text = saved.ProjectFolder;
        _active.Text = saved.ActiveFolder;
        BuildUi();
        _project.Leave += (_, _) => SaveSettings();
        _active.Leave += (_, _) => SaveSettings();
        _process.Click += async (_, _) => await RunAsync((project, _) =>
        {
            var result = LoadingScreenWorkflow.Process(project);
            return $"Ready: {result.Total} numbered WebPs. Kept {result.Existing} in order; added {result.Added}.";
        });
        _export.Click += async (_, _) => await RunAsync((project, active) =>
        {
            var result = LoadingScreenWorkflow.Export(project, active);
            return $"Exported {result.Copied} WebPs; removed {result.Removed} stale numbered files from the active folder.";
        });
    }

    private void BuildUi()
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 7 };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(new Label { Text = "LOADING SCREEN TOOL", Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.LightSkyBlue, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        page.Controls.Add(new Label { Text = "Select the exact project image folder and the active server backgrounds folder.",
            Dock = DockStyle.Fill, ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        page.Controls.Add(FolderRow("PROJECT IMAGE FOLDER", _project), 0, 2);
        page.Controls.Add(FolderRow("ACTIVE SERVER FOLDER", _active), 0, 3);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(0, 8, 0, 8) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        StyleButton(_process);
        StyleButton(_export);
        _process.Margin = new Padding(0, 0, 8, 0);
        _export.Margin = new Padding(8, 0, 0, 0);
        actions.Controls.Add(_process, 0, 0);
        actions.Controls.Add(_export, 1, 0);
        page.Controls.Add(actions, 0, 4);
        _status.Text = "Process keeps the order of existing numbered WebPs and appends new images.";
        _status.ForeColor = Color.LightSkyBlue;
        page.Controls.Add(_status, 0, 5);
        page.Controls.Add(new Label { Text = "Supported: PNG, JPEG, BMP, GIF, WebP. To remove an image, delete its numbered WebP. Export syncs numbered WebPs only.",
            Dock = DockStyle.Fill, ForeColor = Color.Gainsboro, TextAlign = ContentAlignment.TopLeft }, 0, 6);
        Controls.Add(page);
    }

    private Control FolderRow(string title, TextBox field)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = Color.LightSkyBlue,
            TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        layout.SetColumnSpan(layout.Controls[0], 2);
        field.BackColor = Color.FromArgb(25, 25, 52);
        field.ForeColor = Color.White;
        layout.Controls.Add(field, 0, 1);
        var browse = new Button { Text = "BROWSE", Dock = DockStyle.Fill };
        StyleButton(browse);
        browse.Click += (_, _) => Browse(field, title);
        layout.Controls.Add(browse, 1, 1);
        return layout;
    }

    private void Browse(TextBox field, string title)
    {
        using var dialog = new FolderBrowserDialog { Description = title, UseDescriptionForTitle = true };
        if (Directory.Exists(field.Text)) dialog.SelectedPath = field.Text;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        field.Text = dialog.SelectedPath;
        SaveSettings();
    }

    private async Task RunAsync(Func<string, string, string> operation)
    {
        string project = _project.Text.Trim();
        string active = _active.Text.Trim();
        SaveSettings();
        _process.Enabled = _export.Enabled = false;
        _status.Text = "Working...";
        try
        {
            string result = await Task.Run(() => operation(project, active));
            _status.Text = result;
            MessageBox.Show(this, result, "Loading Screen Tool", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            _status.Text = "Operation failed: " + error.Message;
            MessageBox.Show(this, error.Message, "Loading Screen Tool", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _process.Enabled = _export.Enabled = true; }
    }

    private static void StyleButton(Button button)
    {
        button.BackColor = Color.FromArgb(100, 149, 237);
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.LightSkyBlue;
        button.UseVisualStyleBackColor = false;
    }

    private Settings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) ?? new Settings("", "");
        }
        catch (Exception) { /* Damaged settings should not prevent launch. */ }
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string project = Path.Combine(desktop, "loading screen");
        string active = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GitHub", "BadlandsRP",
            "resources", "blrp_loadingscreen", "backgrounds", "halloween");
        return new Settings(Directory.Exists(project) ? project : "", Directory.Exists(active) ? active : "");
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings(_project.Text.Trim(), _active.Text.Trim())));
        }
        catch (Exception error) { _status.Text = "Could not save folder settings: " + error.Message; }
    }
}
