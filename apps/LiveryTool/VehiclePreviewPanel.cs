using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Badlands.LiveryTool;

internal sealed class VehiclePreviewPanel : UserControl
{
    private readonly TextBox gta = new() { Width = 340, PlaceholderText = "Local GTA V Legacy folder" };
    private readonly TextBox dlc = new() { Width = 120, PlaceholderText = "GTA DLC level" };
    private readonly Panel viewport = new() { Dock = DockStyle.Fill };
    private readonly Label message = new() { Dock = DockStyle.Bottom, Height = 65, Text = "Select a car to preview it. Select a queue row to apply its pending livery." };
    private readonly ProgressBar loading = new() { Dock = DockStyle.Bottom, Height = 6, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, Visible = false, AccessibleName = "Loading vehicle preview" };
    private int loadingGeneration;
    private readonly List<VehiclePreviewAssets> retained = [];
    private readonly string session = Path.Combine(Path.GetTempPath(), "BLRP-Livery-Viewer-" + Guid.NewGuid().ToString("N"));
    private readonly SemaphoreSlim loadLock = new(1);
    private Process? process;
    private readonly System.Windows.Forms.Timer poll = new() { Interval = 400 };
    private bool closed;
    private string lastDescription = "";
    private string requestedModel = "";
    private readonly LiveryPicker existingChoices = new() { Width = 760, AccessibleName = "Preview livery" };
    private bool updatingChoices;
    public string DisplayedVehicleKey { get; private set; } = "";
    public event Action? RefreshRequested;
    public event Action? PreviewLiveryRequested;
    public event Action? VehicleOnlyRequested;
    public event Action<ExistingLivery?, bool>? LiverySelectionChanged;
    private string CommandPath => Path.Combine(session, "scene.xml");
    private string StatusPath => Path.Combine(session, "status.txt");

    public VehiclePreviewPanel()
    {
        var settings = ToolSettingsStore.Load();
        gta.Text = settings.GtaFolder;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(8), WrapContents = false };
        var connection = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, Visible = false, Padding = new Padding(8) };
        var browse = new LiveryButton { Text = "GTA folder" };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Local GTA V Legacy installation" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                gta.Text = dialog.SelectedPath;
                var current = ToolSettingsStore.Load();
                ToolSettingsStore.Save(current with { GtaFolder = gta.Text, CreateBackups = false });
                RefreshRequested?.Invoke();
            }
        };
        connection.Controls.AddRange([gta, browse, new Label { Text = "DLC level", AutoSize = true }, dlc]);
        bar.Controls.Add(new Label { Text = "Real vehicle preview", AutoSize = true, Padding = new Padding(0, 8, 12, 0) });
        var setup = new LiveryButton { Text = "Game files" };
        setup.Click += (_, _) => connection.Visible = !connection.Visible;
        bar.Controls.Add(setup);
        var refresh = new LiveryButton { Text = "Refresh" };
        refresh.Click += (_, _) => RefreshRequested?.Invoke();
        bar.Controls.Add(refresh);
        var artwork = new LiveryButton { Text = "Preview livery…", Primary = true };
        artwork.Click += (_, _) => PreviewLiveryRequested?.Invoke();
        var carOnly = new LiveryButton { Text = "Car only" };
        carOnly.Click += (_, _) => VehicleOnlyRequested?.Invoke();
        bar.Controls.Add(artwork);
        bar.Controls.Add(carOnly);
        var liveryBar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 88, Padding = new Padding(8), ColumnCount = 2 };
        liveryBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65));
        liveryBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        existingChoices.Dock = DockStyle.Fill;
        liveryBar.Controls.Add(new Label { Text = "Livery", AutoSize = true, Padding = new Padding(0, 7, 8, 0) }, 0, 0);
        liveryBar.Controls.Add(existingChoices, 1, 0);
        existingChoices.SelectionChanged += () =>
        {
            if (!updatingChoices && existingChoices.SelectedChoice is LiveryChoice choice) LiverySelectionChanged?.Invoke(choice.Existing, choice.Pending);
        };
        Controls.Add(viewport); Controls.Add(connection); Controls.Add(liveryBar); Controls.Add(bar); Controls.Add(loading); Controls.Add(message);
        Directory.CreateDirectory(session);
        poll.Tick += (_, _) =>
        {
            try
            {
                if (File.Exists(StatusPath))
                {
                    string text = File.ReadAllText(StatusPath);
                    if (text.StartsWith("ERROR:") && requestedModel.Length > 0) { message.Text = text; loading.Visible = false; }
                    else if (text.StartsWith("LOADED:"))
                    {
                        string loaded = text[7..].Trim();
                        if (loaded != requestedModel) return;
                        foreach (var old in retained.Where(asset => asset.ModelPath != loaded).ToArray()) { old.Dispose(); retained.Remove(old); }
                        message.Text = lastDescription;
                        loading.Visible = false;
                    }
                }
                if (process?.HasExited == true)
                {
                    if (requestedModel.Length > 0) { message.Text = "Preview renderer closed. Select a queue item to start it again."; loading.Visible = false; }
                    process.Dispose(); process = null;
                }
            }
            catch (IOException) { }
        };
        poll.Start();
    }

    public void SetExistingLiveries(CatalogVehicle vehicle, ExistingLivery[] liveries, bool pendingAvailable, bool usePending, string? selectedKey)
    {
        updatingChoices = true;
        try
        {
            DisplayedVehicleKey = vehicle.Key;
            var choices = new List<LiveryChoice> { new("car-only", "Car only", "View the vehicle without a livery", "Base vehicle") };
            if (pendingAvailable) choices.Add(new("pending", "Pending artwork", "Artwork from the selected queue row", "Your artwork", Pending: true));
            choices.AddRange(liveries.Select(livery => LiveryChoice.From(livery)));
            existingChoices.Context = vehicle.Model + " · preview";
            existingChoices.SetChoices(choices, choices.FirstOrDefault(choice => selectedKey != null && choice.Existing?.Key == selectedKey)?.Key ??
                choices.FirstOrDefault(choice => usePending && choice.Pending)?.Key ?? "car-only");
        }
        finally { updatingChoices = false; }
    }

    public int BeginLoading()
    {
        requestedModel = "";
        message.Text = "Loading vehicle and livery selection…";
        loading.Visible = true;
        return ++loadingGeneration;
    }

    public void CancelLoading(int generation)
    {
        if (generation != loadingGeneration || closed) return;
        requestedModel = "";
        loading.Visible = false;
        message.Text = "Preview loading cancelled.";
    }

    public async Task LoadVehicle(VehicleCatalog catalog, CatalogVehicle vehicle, LiveryImport? request, CancellationToken token, ExistingLivery? existing = null, int generation = 0)
    {
        if (closed) return;
        if (generation == 0) generation = BeginLoading();
        await loadLock.WaitAsync(token);
        VehiclePreviewAssets? assets = null;
        try
        {
            message.Text = "Preparing real vehicle in temporary staging…";
            if (dlc.Text.Length == 0 && catalog.GameBuild == "3095") dlc.Text = "mp2023_02";
            assets = await VehiclePreviewAssets.Prepare(catalog, vehicle, request, gta.Text, dlc.Text.Trim(), token,
                new Progress<string>(text => { if (!closed && generation == loadingGeneration) message.Text = text; }), existing);
            token.ThrowIfCancellationRequested();
            if (closed) return;
            lastDescription = assets.Description;
            string temporary = CommandPath + ".tmp";
            new XDocument(new XElement("Scene", new XAttribute("model", assets.ModelPath), new XAttribute("vehicle", vehicle.Key))).Save(temporary);
            File.Move(temporary, CommandPath, true);
            requestedModel = assets.ModelPath;
            if (File.Exists(StatusPath)) File.Delete(StatusPath);
            retained.Add(assets); assets = null;
            StartRenderer();
            message.Text = "Loading model and textures into the 3D renderer…";
        }
        finally { assets?.Dispose(); loadLock.Release(); }
    }

    private void StartRenderer()
    {
        if (process != null && !process.HasExited) return;
        string? directory = FindRenderer();
        if (directory == null) throw new FileNotFoundException("The bundled BLRP.LiveryPreview.exe or BadWalker renderer is missing. Use the BLRP Tools package containing the preview helper; no CodeWalker source checkout is needed.");
        var info = new ProcessStartInfo(Path.Combine(directory, "BLRP.LiveryPreview.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory };
        foreach (string argument in new[] { viewport.Handle.ToInt64().ToString(CultureInfo.InvariantCulture), Environment.ProcessId.ToString(CultureInfo.InvariantCulture), CommandPath, StatusPath }) info.ArgumentList.Add(argument);
        process = Process.Start(info) ?? throw new IOException("Could not start the bundled preview renderer.");
    }

    internal static string? FindRenderer()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            foreach (string candidate in new[] { Path.Combine(directory.FullName, "tools", "BadWalker"), Path.Combine(directory.FullName, "release", "livery-studio-preview", "tools", "BadWalker"), Path.Combine(directory.FullName, "release", "livery-preview", "tools", "BadWalker") })
                if (File.Exists(Path.Combine(candidate, "BLRP.LiveryPreview.exe")) && File.Exists(Path.Combine(candidate, "CodeWalker.exe"))) return candidate;
        }
        return null;
    }

    public void Clear(string text)
    {
        StopRenderer();
        // A previous load acknowledgement must not hide a preparation error after its renderer stopped.
        requestedModel = "";
        loading.Visible = false;
        lastDescription = "";
        message.Text = text;
    }

    private void StopRenderer()
    {
        if (process != null)
        {
            try
            {
                if (!process.HasExited)
                {
                    File.WriteAllText(Path.Combine(session, "close"), "");
                    if (!process.WaitForExit(2500)) { process.Kill(); process.WaitForExit(1500); }
                }
            }
            finally { process.Dispose(); process = null; }
        }
        foreach (var asset in retained) asset.Dispose();
        retained.Clear();
        string close = Path.Combine(session, "close");
        if (File.Exists(close)) File.Delete(close);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            closed = true; poll.Dispose(); StopRenderer();
            if (Directory.Exists(session)) Directory.Delete(session, true);
        }
        base.Dispose(disposing);
    }
}
