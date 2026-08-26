using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BLRP.ToolsHub;

internal sealed class MainForm : Form
{
    private static readonly Color Background = Color.FromArgb(12, 12, 28);
    private static readonly Color Card = Color.FromArgb(25, 25, 52);
    private static readonly Color Accent = Color.FromArgb(100, 149, 237);
    private static readonly Color AccentLight = Color.FromArgb(135, 206, 235);
    private readonly Label _updateStatus = Label("CHECKING FOR UPDATES...", 8, AccentLight);
    private readonly Button _updateButton;
    private ReleaseInfo? _release;

    public MainForm()
    {
        Text = $"BLRP Tools v{Application.ProductVersion.Split('+')[0]}";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(880, 620);
        ClientSize = new Size(980, 680);
        BackColor = Background;
        ForeColor = Color.White;
        Font = new Font("Cascadia Mono", 9F);
        _updateButton = Button("CHECK FOR UPDATES", async (_, _) => await UpdateClicked());
        BuildUi();
        Shown += async (_, _) => await CheckForUpdates(false);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(ClientRectangle, Background, Color.FromArgb(22, 22, 46), 135F);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    private void BuildUi()
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(30), BackColor = Color.Transparent, RowCount = 3 };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, RowCount = 2, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var logo = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom,
            Image = Image.FromFile(Path.Combine(AppContext.BaseDirectory, "BLRP_Logo.png")), Margin = new Padding(0, 0, 14, 6) };
        header.Controls.Add(logo, 0, 0);
        header.SetRowSpan(logo, 2);
        header.Controls.Add(Label("BLRP TOOLS", 22, Color.White, FontStyle.Bold), 1, 0);
        header.Controls.Add(Label("BADLANDSRP  /  SELECT A TOOL", 9, AccentLight, FontStyle.Bold), 1, 1);
        page.Controls.Add(header, 0, 0);

        var tools = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 3 };
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tools.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
        tools.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33F));
        tools.RowStyles.Add(new RowStyle(SizeType.Percent, 33.34F));
        tools.Controls.Add(ToolCard("ASSET STUDIO", "Weapon skins, cups, model previews and inventory images.", @"tools\AssetStudio\BLRP.AssetStudio.dll"), 0, 0);
        tools.Controls.Add(ToolCard("CLOTHING LOCATOR", "Find, preview and manage BadlandsRP clothing assets.", @"tools\ClothingLocator\BLRP.ClothingUtility.dll"), 1, 0);
        tools.Controls.Add(ToolCard("LIVERY TOOL", "Build and install vehicle liveries and metadata.", @"tools\LiveryTool\Badlands.LiveryTool.dll"), 0, 1);
        tools.Controls.Add(ToolCard("MAPPING DECONFLICTER", "Scan YMAP resources and identify mapping conflicts.", @"tools\MappingDeconflicter\YmapDeconflicter.dll"), 1, 1);
        tools.Controls.Add(ToolCard("GRZY CLOTH TOOL", "Build, inspect and preview GTA clothing packs.", @"tools\grzyClothTool-outfit\grzyClothTool.exe"), 0, 2);
        tools.Controls.Add(ToolCard("BADWALKER", "View and edit GTA maps, archives, models and metadata.", @"tools\BadWalker\CodeWalker.exe"), 1, 2);
        page.Controls.Add(tools, 0, 1);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        footer.Controls.Add(_updateStatus, 0, 0);
        _updateButton.Dock = DockStyle.Fill;
        footer.Controls.Add(_updateButton, 1, 0);
        page.Controls.Add(footer, 0, 2);
        Controls.Add(page);
    }

    private Control ToolCard(string title, string description, string executable)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Card, Margin = new Padding(7), Padding = new Padding(18) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.Controls.Add(Label(title, 12, AccentLight, FontStyle.Bold), 0, 0);
        var copy = Label(description, 9, Color.White);
        copy.AutoEllipsis = false;
        layout.Controls.Add(copy, 0, 1);
        var open = Button("OPEN TOOL", (_, _) => Launch(executable));
        open.Dock = DockStyle.Fill;
        layout.Controls.Add(open, 0, 2);
        panel.Controls.Add(layout);
        return panel;
    }

    private void Launch(string relativePath)
    {
        string path = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (!File.Exists(path))
        {
            MessageBox.Show(this, "This tool is missing from the installation. Reinstall BLRP Tools.\n\n" + path,
                "BLRP Tools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        bool managedTool = IsManagedTool(path);
        var start = new ProcessStartInfo(managedTool ? Environment.ProcessPath ?? Application.ExecutablePath : path)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(path)!
        };
        if (managedTool)
        {
            start.ArgumentList.Add("--run-tool");
            start.ArgumentList.Add(path);
        }
        Process.Start(start);
    }

    private static bool IsManagedTool(string path) =>
        Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase);

    private async Task UpdateClicked()
    {
        if (_release is null) { await CheckForUpdates(true); return; }
        string prompt = $"You are on v{Application.ProductVersion.Split('+')[0]}. " +
            $"Here is what you are missing in {_release.TagName}:\n\n{_release.Notes}\n\n" +
            "Install now? The Hub will restart automatically.";
        if (MessageBox.Show(this, prompt,
            "Install update", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        try
        {
            _updateButton.Enabled = false;
            _updateStatus.Text = "DOWNLOADING UPDATE...";
            string zip = Path.Combine(Path.GetTempPath(), "BLRP-Tools-" + Guid.NewGuid().ToString("N") + ".zip");
            using HttpClient client = Client();
            await File.WriteAllBytesAsync(zip, await client.GetByteArrayAsync(_release.DownloadUrl));
            string updaterDirectory = Path.Combine(Path.GetTempPath(), "BLRP-Tools-Updater-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(updaterDirectory);
            string updater = Path.Combine(updaterDirectory, "BLRP.Tools.Updater.exe");
            string launcherPath = Environment.ProcessPath ?? Application.ExecutablePath;
            File.Copy(launcherPath, updater);
            var start = new ProcessStartInfo(updater) { UseShellExecute = true };
            start.ArgumentList.Add("--apply-update");
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            start.ArgumentList.Add(zip);
            start.ArgumentList.Add(AppContext.BaseDirectory);
            start.ArgumentList.Add(launcherPath);
            Process.Start(start);
            Application.Exit();
        }
        catch (Exception exception)
        {
            _updateButton.Enabled = true;
            _updateStatus.Text = "UPDATE FAILED";
            MessageBox.Show(this, exception.Message, "BLRP Tools", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task CheckForUpdates(bool showCurrent)
    {
        try
        {
            using HttpClient client = Client();
            string json = await client.GetStringAsync("https://api.github.com/repos/Badlands-RP/BLRP-Tools/releases?per_page=50");
            GithubRelease[] releases = JsonSerializer.Deserialize<GithubRelease[]>(json) ?? [];
            Version current = Version.Parse(Application.ProductVersion.Split('+')[0]);
            ReleaseInfo? update = FindUpdate(releases, current);
            if (update is not null)
            {
                _release = update;
                _updateStatus.Text = $"UPDATE AVAILABLE  /  {update.TagName}";
                _updateButton.Text = "INSTALL UPDATE";
            }
            else
            {
                _updateStatus.Text = $"UP TO DATE  /  v{current}";
                if (showCurrent) MessageBox.Show(this, "BLRP Tools is up to date.", "BLRP Tools", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch
        {
            _updateStatus.Text = "UPDATE CHECK UNAVAILABLE";
        }
    }

    private static ReleaseInfo? FindUpdate(IEnumerable<GithubRelease> releases, Version current)
    {
        var pending = releases
            .Where(release => !release.Prerelease && Version.TryParse(release.TagName.TrimStart('v'), out Version? version) && version > current)
            .Select(release => (Release: release, Version: Version.Parse(release.TagName.TrimStart('v'))))
            .OrderBy(item => item.Version)
            .ToArray();
        if (pending.Length == 0) return null;
        GithubRelease latest = pending[^1].Release;
        GithubAsset? asset = latest.Assets.FirstOrDefault(item => item.Name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase));
        if (asset is null) return null;
        string notes = string.Join("\n\n", pending.Select(item =>
            $"{item.Release.TagName}\n{(string.IsNullOrWhiteSpace(item.Release.Body) ? "No release notes supplied." : item.Release.Body.Trim())}"));
        return new ReleaseInfo(latest.TagName, asset.DownloadUrl, notes);
    }

    private static HttpClient Client()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BLRP-Tools", Application.ProductVersion.Split('+')[0]));
        return client;
    }

    private static Label Label(string text, float size, Color color, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text, Dock = DockStyle.Fill, ForeColor = color, BackColor = Color.Transparent,
        Font = new Font("Cascadia Mono", size, style), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
    };

    private static Button Button(string text, EventHandler click)
    {
        var button = new Button { Text = text, BackColor = Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
            Font = new Font("Cascadia Mono", 8.5F, FontStyle.Bold), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderColor = AccentLight;
        button.Click += click;
        return button;
    }

    internal static bool SelfTest()
    {
        GithubAsset asset = new("BLRP-Tools-v1.0.4-win-x64.zip", "https://example.invalid/update.zip");
        ReleaseInfo? update = FindUpdate(
        [
            new GithubRelease("v1.0.4", "Fourth release", false, [asset]),
            new GithubRelease("v1.0.2", "Second release", false, []),
            new GithubRelease("v1.0.1", "Installed", false, [])
        ], new Version(1, 0, 1));
        return update is { TagName: "v1.0.4" } &&
            update.Notes.IndexOf("v1.0.2", StringComparison.Ordinal) < update.Notes.IndexOf("v1.0.4", StringComparison.Ordinal) &&
            IsManagedTool("tool.dll") &&
            !IsManagedTool("grzyClothTool.exe") &&
            !IsManagedTool("CodeWalker.exe");
    }

    private sealed record ReleaseInfo(string TagName, string DownloadUrl, string Notes);
    private sealed record GithubRelease([property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] GithubAsset[] Assets);
    private sealed record GithubAsset([property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl);
}
