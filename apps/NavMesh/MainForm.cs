using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BLRP.NavMesh;

internal sealed class MainForm : Form
{
    private readonly TabControl editor = new() { Dock = DockStyle.Fill, Multiline = true };
    private readonly FlowLayoutPanel toolbar = new() { Dock = DockStyle.Fill, WrapContents = false };
    private readonly CheckBox advanced = new() { Text = "Advanced settings", AutoSize = true, Margin = new Padding(18, 8, 0, 0) };
    private readonly CheckBox autoFit = new() { Text = "Fit the area automatically before previewing", AutoSize = true };
    private readonly Label gameStatus = Caption("Checking for GTA V Legacy…");
    private readonly Label areaHint = Caption("The preview area will be fitted to your selected map.");
    private readonly Label exportStatus = Caption("Generate a preview first. Export becomes available after the input checks pass.");
    private readonly Label resourceHint = Caption("Choose the resource folder containing fxmanifest.lua. Dependencies are found in its resources tree when available.");
    private readonly TextBox issueDetails = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical };
    private readonly ToolTip tips = new() { AutoPopDelay = 15000 };
    private readonly NumericUpDown floorHeight = Number(0, -100000, 100000, 2);
    private readonly CheckBox collisionView = new() { Text = "Show collision", AutoSize = true, Margin = new Padding(10, 7, 0, 0) };
    private TabPage[] advancedPages = [];
    private TabPage exportPage = null!;
    private readonly string preferencesPath;
    private readonly UserPreferences preferences;
    private bool applyingSettings;
    private bool previewReadyForExport;
    private string[] issueMessages = [];
    private readonly ListBox resources = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly CheckedListBox maps = new() { Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true };
    private readonly CheckedListBox entitySets = new() { Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true };
    private readonly NumericUpDown build = Number(3095, 1, 100000, 0);
    private readonly NumericUpDown[] area = Enumerable.Range(0, 6).Select(i => Number(i < 3 ? (i == 2 ? -1 : 0) : (i == 5 ? 4 : 10), -100000, 100000)).ToArray();
    private readonly TextBox output = new() { Dock = DockStyle.Fill };
    private readonly TextBox baseline = new() { Dock = DockStyle.Fill };
    private readonly CheckBox compose = new() { Text = "Include original surrounding navigation", AutoSize = true };
    private readonly TextBox conflicts = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox useGame = new() { Text = "Include assets from game archives", AutoSize = true };
    private readonly CheckBox automaticDlc = new() { Text = "Find DLC and matching updates automatically", AutoSize = true };
    private string preparedGameKey = "";
    private string[] discoveryIssues = [];
    private readonly TextBox gameFolder = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly NumericUpDown archiveBuild = Number(3095, 1, 100000, 0);
    private readonly TextBox sourceNotes = new() { Dock = DockStyle.Fill };
    private readonly CheckBox validated = new() { Text = "Archive selection verified for this game build", AutoSize = true };
    private readonly BindingList<ArchiveInput> archiveItems = [];
    private readonly DataGridView archives = Grid();
    private readonly PropertyGrid agent = new() { Dock = DockStyle.Fill, ToolbarVisible = false, HelpVisible = true, PropertySort = PropertySort.NoSort };
    private readonly CheckBox interior = new() { Text = "Interior navigation", AutoSize = true };
    private readonly CheckBox islands = new() { Text = "Allow intentionally disconnected navigation", AutoSize = true };
    private readonly TextBox dependencies = new() { Dock = DockStyle.Fill };
    private readonly TextBox ignored = new() { Dock = DockStyle.Fill };
    private readonly TextBox review = new() { Dock = DockStyle.Fill, Multiline = true };
    private readonly TextBox flags = new() { Dock = DockStyle.Fill, Text = "0, 0, 0, 0, 0" };
    private readonly DataGridView collisions = Grid();
    private readonly NavPreview preview = new() { Dock = DockStyle.Fill };
    private readonly TextBox log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly ListBox issues = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly Label summary = Caption("Start with your map\nAdd a map resource on the left, then generate a preview.");
    private readonly Label status = Caption("Ready");
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly Button generate;
    private readonly Button package;
    private readonly Button cancel;
    private BakeSettings settings = new();
    private string? projectPath;
    private string? latestOutput;
    private CancellationTokenSource? operation;
    private bool closeAfterOperation;
    private HelpForm? help;

    public MainForm(string? initialPath = null, string? preferencesPath = null)
    {
        this.preferencesPath = preferencesPath ?? UserPreferences.DefaultPath;
        preferences = UserPreferences.Load(this.preferencesPath);
        Text = "BLRP NavMesh";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 720);
        ClientSize = new Size(1340, 890);
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.F1) return;
            TryUi(ShowHelp); e.Handled = true; e.SuppressKeyPress = true;
        };
        generate = AsyncButton("GENERATE PREVIEW", () => BakeClicked(true));
        package = AsyncButton("BUILD RESOURCE", () => BakeClicked(false));
        cancel = Button("CANCEL", () => { operation?.Cancel(); cancel!.Enabled = false; status.Text = "Stopping after the current step…"; });
        cancel.Enabled = false;
        BuildLayout();
        BlrpTheme.Apply(this);
        void ReadableFonts(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                control.Font = new Font("Segoe UI", Math.Max(10, control.Font.Size), control.Font.Style);
                ReadableFonts(control);
            }
        }
        ReadableFonts(this);
        foreach (var tab in editor.TabPages.Cast<TabPage>()) { tab.UseVisualStyleBackColor = false; tab.BackColor = BlrpTheme.Background; }
        foreach (var list in new ListBox[] { resources, maps, entitySets, issues }) { list.BackColor = BlrpTheme.Input; list.ForeColor = Color.White; }
        agent.ViewBackColor = BlrpTheme.Input; agent.ViewForeColor = Color.White;
        agent.HelpBackColor = BlrpTheme.Card; agent.HelpForeColor = Color.White;
        agent.LineColor = BlrpTheme.Card;
        ConfigureInputEvents();
        advanced.CheckedChanged += (_, _) => SetAdvanced(advanced.Checked);
        SetAdvanced(false);
        ApplySettings(new BakeSettings { GameBuild = Math.Clamp(preferences.GameBuild, 1, 100000), Min = [0, 0, -1], Max = [10, 10, 4], AutoFitArea = true,
            OutputDirectory = string.IsNullOrWhiteSpace(preferences.ResultsFolder) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BLRP", "NavMesh") : preferences.ResultsFolder });
        FormClosing += (_, e) =>
        {
            if (operation == null) return;
            e.Cancel = true; closeAfterOperation = true; operation.Cancel();
            status.Text = "Finishing the current step before closing…";
        };
        FormClosed += (_, _) =>
        {
            try
            {
                if (GameSource.IsLegacyDirectory(gameFolder.Text)) preferences.GtaFolder = gameFolder.Text;
                preferences.GameBuild = (int)build.Value; preferences.ResultsFolder = output.Text;
                preferences.Save(this.preferencesPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Preferences must not prevent closing. */ }
            tips.Dispose();
        };
        Shown += (_, _) => { if (initialPath != null) TryUi(() => LoadProject(initialPath)); };
    }

    private void BuildLayout()
    {
        var page = Table(); page.Padding = new Padding(18);
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        var heading = Caption("NAVMESH  /  Custom map navigation"); heading.Font = new Font("Cascadia Mono", 17, FontStyle.Bold);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        header.Controls.Add(heading, 0, 0);
        var helpButton = Button("HELP / F1", ShowHelp); helpButton.Dock = DockStyle.Fill;
        header.Controls.Add(helpButton, 1, 0);
        page.Controls.Add(header, 0, 0);
        toolbar.Controls.Add(Button("NEW", () =>
        {
            projectPath = null; latestOutput = null; preview.Clear(); SetIssues([]); log.Clear();
            ApplySettings(new BakeSettings { GameBuild = (int)build.Value, Min = [0, 0, -1], Max = [10, 10, 4], OutputDirectory = output.Text, AutoFitArea = true });
            Text = "BLRP NavMesh"; summary.ForeColor = Color.LightSkyBlue;
            summary.Text = "Start with your map\nAdd a map resource on the left, then generate a preview.";
        }));
        toolbar.Controls.Add(Button("OPEN PROJECT", () => PickFile("Bake projects|*.json", path => LoadProject(path))));
        toolbar.Controls.Add(Button("SAVE PROJECT", SaveProject));
        toolbar.Controls.Add(Button("OPEN RESULTS", () => { if (latestOutput != null) OpenFolder(latestOutput); }));
        toolbar.Controls.Add(Button("OPEN PREVIOUS RESULT", () => PickFile("Bake reports|report.json", path => ShowReport(Path.GetDirectoryName(path)!))));
        toolbar.Controls.Add(advanced);
        page.Controls.Add(toolbar, 0, 1);

        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1300, 700), SplitterDistance = 580,
            Panel1MinSize = 480, Panel2MinSize = 400 };
        split.Panel1.Padding = new Padding(0, 0, 10, 0); split.Panel1.Controls.Add(editor);
        BuildMapTab(); BuildAreaTab(); BuildArchiveTab(); BuildNavigationTab(); BuildCollisionTab();
        advancedPages = editor.TabPages.Cast<TabPage>().Skip(1).ToArray();
        BuildExportTab(); exportPage = editor.TabPages[^1];
        var result = Table();
        result.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        result.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        result.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        result.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        summary.Padding = new Padding(8); result.Controls.Add(summary, 0, 0);
        result.Controls.Add(preview, 0, 1);
        var viewControls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var slice = new CheckBox { Text = "Floor height", AutoSize = true, Margin = new Padding(6, 7, 0, 0) };
        floorHeight.Width = 90; floorHeight.Dock = DockStyle.None;
        void SetSlice() { preview.FloorHeight = slice.Checked ? (float)floorHeight.Value : null; preview.Invalidate(); }
        slice.CheckedChanged += (_, _) => SetSlice(); floorHeight.ValueChanged += (_, _) => SetSlice();
        viewControls.Controls.AddRange([Button("FIT VIEW", preview.Fit), slice, floorHeight]);
        collisionView.CheckedChanged += (_, _) => TryUi(() => LoadPreview(collisionView.Checked));
        viewControls.Controls.Add(collisionView); result.Controls.Add(viewControls, 0, 2);
        var details = new TabControl { Dock = DockStyle.Fill };
        var issueTab = new TabPage("Needs attention");
        var issueLayout = Table(); issueLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); issueLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        issueLayout.Controls.Add(issues, 0, 0); issueLayout.Controls.Add(issueDetails, 0, 1); issueTab.Controls.Add(issueLayout);
        issues.SelectedIndexChanged += (_, _) =>
        {
            if (issues.SelectedItem is IssueGroup group)
                issueDetails.Text = group.NextStep + Environment.NewLine + Environment.NewLine + "Details:" + Environment.NewLine + string.Join(Environment.NewLine, group.Messages);
        };
        var logTab = new TabPage("Technical log"); logTab.Controls.Add(log);
        details.TabPages.AddRange([issueTab, logTab]); result.Controls.Add(details, 0, 3);
        split.Panel2.Controls.Add(result); page.Controls.Add(split, 0, 2);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Padding = new Padding(0, 10, 0, 0) };
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        actions.Controls.Add(progress, 0, 0); actions.Controls.Add(generate, 1, 0); actions.Controls.Add(package, 2, 0); actions.Controls.Add(cancel, 3, 0);
        foreach (var button in new[] { generate, package, cancel }) button.Dock = DockStyle.Fill;
        page.Controls.Add(actions, 0, 3); page.Controls.Add(status, 0, 4); Controls.Add(page);
    }

    private void BuildMapTab()
    {
        var body = Tab("Map & preview");
        FullRow(body, Caption("1. Choose a map    →    2. Generate a preview    →    3. Review & export"), 48);
        var gameRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); gameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        gameRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        gameRow.Controls.Add(gameStatus, 0, 0); var gameButton = Button("GTA SETTINGS", () =>
        {
            if (!useGame.Checked && archiveItems.Count == 0) ChooseGameFolder();
            else { advanced.Checked = true; editor.SelectedTab = advancedPages[1]; }
        });
        gameButton.Dock = DockStyle.Fill; gameButton.AutoSize = false; gameRow.Controls.Add(gameButton, 1, 0);
        FullRow(body, gameRow, 72);
        Row(body, "Map resources", ListWithActions(resources, () => PickFolder(path => AddResource(path)), () =>
        {
            if (resources.SelectedItem is not string root) return;
            resources.Items.Remove(root);
            foreach (string path in maps.Items.Cast<string>().Where(p => Path.GetRelativePath(root, p) is var relative && !relative.StartsWith("..")).ToArray()) maps.Items.Remove(path);
            InputsChanged();
        }, "ADD MAP RESOURCE"), 120);
        FullRow(body, resourceHint, 48);
        Row(body, "Placements", ListWithActions(maps, () => PickFiles("Map placements|*.ymap", paths =>
        { foreach (string path in paths) if (!maps.Items.Contains(path)) maps.Items.Add(path, true); }), () =>
        { if (maps.SelectedIndex >= 0) maps.Items.RemoveAt(maps.SelectedIndex); InputsChanged(); }), 105);
        Row(body, "Server build", build, 34);
        Row(body, "Save results to", FolderField(output), 36);
        FullRow(body, autoFit, 32);
        FullRow(body, areaHint, 44);
        FullRow(body, Caption("Click GENERATE PREVIEW below. The tool finds the map area and checks its collision. Your map files stay unchanged."), 52);
    }

    private void BuildAreaTab()
    {
        var body = Tab("Area & layout");
        FullRow(body, Caption("Optional overrides. Normal previews fit the area automatically. Editing coordinates turns automatic fitting off."), 58);
        Row(body, "World area", AsyncButton("READ MAP BOUNDS & ENTITY SETS", ReadMapDetails), 40);
        var bounds = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3 };
        bounds.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        for (int i = 0; i < 3; i++) bounds.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        for (int i = 0; i < 3; i++) { bounds.Controls.Add(Caption(new[] { "X", "Y", "Z" }[i]), i + 1, 0); bounds.Controls.Add(area[i], i + 1, 1); bounds.Controls.Add(area[i + 3], i + 1, 2); }
        bounds.Controls.Add(Caption("Min"), 0, 1); bounds.Controls.Add(Caption("Max"), 0, 2);
        Row(body, "Replacement box (m)", bounds, 96);
        Row(body, "Active entity sets", entitySets, 115);
        Row(body, "", Caption("Unchecked sets are excluded. Match the sets enabled on your server."), 42);
    }

    private void BuildExportTab()
    {
        var body = Tab("Export");
        FullRow(body, Caption("3. Create a server resource"), 38);
        FullRow(body, exportStatus, 120);
        FullRow(body, Caption("A preview can contain only part of the map. Export requires complete collision and the original surrounding navigation for your server build."), 90);
        Row(body, "", compose, 32);
        Row(body, "Original navigation", FolderField(baseline), 36);
        Row(body, "", AsyncButton("PREPARE ORIGINAL NAVIGATION", CaptureBaseline), 40);
        FullRow(body, Caption("Original tiles are saved in a new folder inside your results folder. After preparing them, generate another preview to check the connections."), 75);
        Row(body, "Conflict scan folders", conflicts, 66);
        Row(body, "", Button("ADD SCAN FOLDER", () => PickFolder(path => conflicts.AppendText((conflicts.Text.Length == 0 ? "" : Environment.NewLine) + path))), 36);
    }

    private void BuildArchiveTab()
    {
        var body = Tab("Game sources");
        Row(body, "", useGame, 34);
        Row(body, "", automaticDlc, 34);
        Row(body, "GTA Legacy folder", gameFolder, 36);
        Row(body, "", Button("CHANGE GTA FOLDER", ChooseGameFolder), 40);
        Row(body, "", Caption("Preview preparation finds DLC for the server build using local GTA/FiveM files. A newer update is never substituted. Turn automatic discovery off to author a source set manually."), 80);
        Row(body, "Archive build", archiveBuild, 34);
        Row(body, "Source notes", sourceNotes, 60);
        archives.AutoGenerateColumns = false;
        archives.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Path", HeaderText = "Archive file", Width = 250 });
        archives.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "LogicalPath", HeaderText = "Original archive path", Width = 190 });
        archives.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Sha256", HeaderText = "SHA-256", Width = 440 });
        archives.DataSource = archiveItems;
        Row(body, "Archives, in load order", archives, 225);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        buttons.Controls.AddRange([
            Button("ADD", () => PickFiles("GTA archives|*.rpf;override*|All files|*.*", paths =>
            {
                foreach (string path in paths)
                {
                    string name = Path.GetFileName(path);
                    string logical = name.Contains("+update+update2.rpf_", StringComparison.OrdinalIgnoreCase) ? "update/update2.rpf" :
                        name.Contains("+update+update.rpf_", StringComparison.OrdinalIgnoreCase) ? "update/update.rpf" : name;
                    archiveItems.Add(new ArchiveInput { Path = path, LogicalPath = logical });
                }
            })),
            Button("REMOVE", () => { if (archives.CurrentRow?.DataBoundItem is ArchiveInput item) archiveItems.Remove(item); }),
            Button("UP", () => MoveArchive(-1)), Button("DOWN", () => MoveArchive(1)),
            AsyncButton("CALCULATE HASHES", HashArchives),
            Button("IMPORT SOURCE SET", () => PickFile("Game source sets|*.json", LoadGameSources))]);
        Row(body, "", buttons, 82);
        Row(body, "", validated, 42);
        Row(body, "", Caption("Later archives override earlier ones. Hashes verify file identity; mark the source set verified only after establishing that it belongs to this build. Unverified sets produce inspection output."), 100);
    }

    private void BuildNavigationTab()
    {
        var body = Tab("Pedestrian settings");
        Row(body, "Pedestrian settings", agent, 305);
        Row(body, "", Caption("Sizes are metres; slope is degrees. Tune radius, headroom and climb against actual doors and steps."), 54);
        Row(body, "", interior, 34); Row(body, "", islands, 42);
        Row(body, "Resource dependencies", dependencies, 36);
        Row(body, "", Caption("Separate resource names with commas."), 30);
        Row(body, "Advanced polygon flags", flags, 36);
        Row(body, "Excluded archetypes", ignored, 36);
        Row(body, "Collision review", review, 88);
        Row(body, "", Caption("Document replacement collision or why excluded objects do not affect navigation."), 48);
    }

    private void BuildCollisionTab()
    {
        var body = Tab("Extra collision");
        collisions.Columns.Add("Path", "Native collision file"); collisions.Columns[0].Width = 245;
        foreach (string label in new[] { "X", "Y", "Z", "Quat X", "Quat Y", "Quat Z", "Quat W", "Scale X", "Scale Y", "Scale Z" })
            collisions.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = label, Width = 85 });
        Row(body, "World placement", collisions, 290);
        Row(body, "", ListButtons(() => PickFiles("Collision bounds|*.ybn", paths =>
            { foreach (string path in paths) AddCollision(new CollisionInput { Path = path }); }), () =>
            { if (collisions.CurrentRow != null) collisions.Rows.Remove(collisions.CurrentRow); }), 40);
        Row(body, "", Caption("Optional standalone collision, such as pavement approaches or creator-supplied bounds. Enter decoded world position, quaternion orientation and scale. Resource/MLO collision is loaded from the selected maps automatically."), 112);
    }

    private void AddResource(string path, bool includeMaps = true)
    {
        if (!resources.Items.Contains(path)) resources.Items.Add(path);
        var files = includeMaps ? Directory.EnumerateFiles(path, "*.ymap", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToArray() : [];
        foreach (string map in files) if (!maps.Items.Contains(map)) maps.Items.Add(map, files.Length == 1);
        string name = Path.GetFileName(path);
        if (name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            dependencies.Text = string.Join(", ", Names(dependencies.Text).Append(name).Distinct());
        status.Text = $"Added {Path.GetFileName(path)}. Choose its placement, then generate a preview.";
        if (includeMaps) resourceHint.Text = files.Length == 0 ? "This resource has no placements. Add the map resource that uses these assets." :
            files.Length == 1 ? "The map placement is selected. Generate a preview to find dependencies and fit the area." : $"Found {files.Length} placements. Check the ones you want to preview.";
        InputsChanged();
    }

    private async Task<bool> FitMapArea()
    {
        if (!await PrepareGameSources()) return false;
        var value = new BakeSettings { GameBuild = (int)build.Value,
            ResourceRoots = resources.Items.Cast<string>().ToArray(), Ymaps = maps.CheckedItems.Cast<string>().ToArray(), EntitySets = ReadEntitySets() };
        archives.EndEdit();
        var sources = useGame.Checked ? ReadGameSources() : null;
        MapDetails? details = null;
        string[] found = [];
        bool success = await RunOperation(() =>
        {
            Console.WriteLine("Finding map dependencies and fitting the preview area…");
            var game = sources == null ? null : new GameSource(value, new CollisionScene(value), sources);
            found = MapSelection.FindDependencies(value, game, operation!.Token);
            value.ResourceRoots = value.ResourceRoots.Concat(found).ToArray();
            details = MapSelection.Read(value, sources, game);
        }, () =>
        {
            if (details == null) return;
            foreach (string root in found) AddResource(root, false);
            resourceHint.Text = found.Length > 0 ? "Added required resources: " + string.Join(", ", found.Select(Path.GetFileName)) : "Resource names are shown above. Hover over a name to see its full folder path.";
            for (int i = 0; i < 3; i++) { area[i].Value = (decimal)details.Min[i] - 0.5m; area[i + 3].Value = (decimal)details.Max[i] + 0.5m; }
            floorHeight.Value = (decimal)details.Min.Z;
            if (details.CollisionBoundsCount > 0) interior.Checked = true;
            var previous = ReadEntitySets(); entitySets.Items.Clear();
            foreach (var choice in details.Sets)
                entitySets.Items.Add(choice, previous.TryGetValue(choice.Placement, out var names) && names.Contains(choice.Name));
            settings.EntitySets = details.Sets.Select(s => s.Placement).Distinct().ToDictionary(key => key, _ => Array.Empty<string>());
            SetIssues(details.Unresolved);
            preview.Clear();
            summary.ForeColor = Color.LightSkyBlue;
            var size = details.Max - details.Min + new SharpDX.Vector3(1);
            areaHint.Text = $"Preview area: {size.X:N1} × {size.Y:N1} m. Adjust it under Advanced settings → Area & layout.";
            summary.Text = $"MAP AREA READY — {size.X:N1} × {size.Y:N1} × {size.Z:N1} m\n" +
                $"{details.CollisionBoundsCount} interior collision bounds · {details.MetadataBoundsCount} object bounds · {details.Unresolved.Length} notices\n" +
                "Generate a preview to check floors, entrances and remaining collision issues.";
            status.Text = "Interior area fitted to collision where available. Review approaches and active entity sets before generating.";
        });
        return success && details != null;
    }

    private async Task ReadMapDetails() => await FitMapArea();
    internal Task ReadMapDetailsForTest() => ReadMapDetails();

    private Dictionary<string, string[]> ReadEntitySets()
    {
        var result = settings.EntitySets.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var group in entitySets.Items.Cast<EntitySetChoice>().GroupBy(c => c.Placement))
            result[group.Key] = group.Where(choice => entitySets.CheckedItems.Contains(choice)).Select(c => c.Name).ToArray();
        return result;
    }

    internal BakeSettings ReadSettings(bool requireBaseline = true)
    {
        collisions.EndEdit(); archives.EndEdit();
        if (requireBaseline && compose.Checked && string.IsNullOrWhiteSpace(baseline.Text))
            throw new InvalidDataException("Open Export and use PREPARE ORIGINAL NAVIGATION, or turn off inclusion of surrounding navigation for a standalone preview.");
        var result = new BakeSettings
        {
            GameBuild = (int)build.Value, ResourceRoots = resources.Items.Cast<string>().ToArray(),
            AutoDetectGame = useGame.Checked, AutoFitArea = autoFit.Checked,
            Ymaps = maps.CheckedItems.Cast<string>().ToArray(), EntitySets = ReadEntitySets(),
            Min = area.Take(3).Select(n => (float)n.Value).ToArray(), Max = area.Skip(3).Select(n => (float)n.Value).ToArray(),
            OutputDirectory = Path.GetFullPath(output.Text), BaselineDirectory = compose.Checked && baseline.Text.Length > 0 ? Path.GetFullPath(baseline.Text) : "",
            ConflictScanRoots = conflicts.Lines.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.GetFullPath(p.Trim())).ToArray(),
            Agent = JsonSerializer.Deserialize<AgentSettings>(JsonSerializer.Serialize(agent.SelectedObject, BakeSettings.Json), BakeSettings.Json)!,
            Interior = interior.Checked, AllowIsolatedComponents = islands.Checked, Dependencies = Names(dependencies.Text),
            IgnoreArchetypes = Names(ignored.Text), CollisionReview = review.Text,
            PolygonFlags = Names(flags.Text).Select(s => byte.Parse(s, CultureInfo.InvariantCulture)).ToArray(),
            Collision = collisions.Rows.Cast<DataGridViewRow>().Select(row =>
            {
                float[] Read(int start, int count) => Enumerable.Range(start, count).Select(i => float.Parse(Convert.ToString(row.Cells[i].Value, CultureInfo.InvariantCulture) ?? "", CultureInfo.InvariantCulture)).ToArray();
                return new CollisionInput { Path = Path.GetFullPath(Convert.ToString(row.Cells[0].Value) ?? ""),
                    Position = Read(1, 3), Orientation = Read(4, 4), Scale = Read(8, 3) };
            }).ToArray()
        };
        result.Validate();
        foreach (var collision in result.Collision) collision.Transform();
        return result;
    }

    private GameSourceManifest ReadGameSources() => new()
    {
        GameBuild = (int)archiveBuild.Value, GameDirectory = Path.GetFullPath(gameFolder.Text), Source = sourceNotes.Text,
        ValidatedForBuild = validated.Checked, AutoDiscoverDlc = automaticDlc.Checked, DiscoveryIssues = discoveryIssues,
        Archives = archiveItems.Select(a => new ArchiveInput { Path = Path.GetFullPath(a.Path), LogicalPath = a.LogicalPath, Sha256 = a.Sha256.Trim() }).ToArray()
    };

    internal void ApplySettings(BakeSettings value)
    {
        applyingSettings = true;
        try
        {
            settings = value;
            resources.Items.Clear(); resources.Items.AddRange(value.ResourceRoots);
            maps.Items.Clear(); foreach (string path in value.Ymaps) maps.Items.Add(path, true);
            entitySets.Items.Clear(); foreach (var pair in value.EntitySets) foreach (string name in pair.Value) entitySets.Items.Add(new EntitySetChoice(pair.Key, name), true);
            build.Value = value.GameBuild; for (int i = 0; i < 3; i++) { area[i].Value = (decimal)value.Min[i]; area[i + 3].Value = (decimal)value.Max[i]; }
            output.Text = value.OutputDirectory; baseline.Text = value.BaselineDirectory; compose.Checked = value.BaselineDirectory.Length != 0;
            conflicts.Lines = value.ConflictScanRoots; dependencies.Text = string.Join(", ", value.Dependencies);
            ignored.Text = string.Join(", ", value.IgnoreArchetypes); review.Text = value.CollisionReview;
            flags.Text = string.Join(", ", value.PolygonFlags); agent.SelectedObject = value.Agent;
            interior.Checked = value.Interior; islands.Checked = value.AllowIsolatedComponents;
            autoFit.Checked = value.AutoFitArea;
            collisions.Rows.Clear(); foreach (var input in value.Collision) AddCollision(input);
            archiveItems.Clear(); gameFolder.Clear(); sourceNotes.Clear(); validated.Checked = false; archiveBuild.Value = value.GameBuild;
            automaticDlc.Checked = false; preparedGameKey = ""; discoveryIssues = [];
            useGame.Checked = value.GameSourceFile.Length > 0;
            if (useGame.Checked) LoadGameSources(value.GameSourceFile);
            else if (value.AutoDetectGame) DetectGame();
            areaHint.Text = autoFit.Checked ? "The preview area will be fitted to your selected map." : "Using the project's saved area. Enable automatic fitting or edit it under Advanced settings.";
        }
        finally { applyingSettings = false; InputsChanged(); RefreshGameStatus(); }
    }

    private void AddCollision(CollisionInput input) => collisions.Rows.Add(new object[] { input.Path }.Concat(
        input.Position.Concat(input.Orientation).Concat(input.Scale).Select(v => (object)v.ToString("R", CultureInfo.InvariantCulture))).ToArray());

    internal void LoadProject(string path)
    {
        ApplySettings(BakeSettings.Load(path)); projectPath = path;
        preview.Clear(); SetIssues([]); log.Clear(); latestOutput = null;
        Text = "BLRP NavMesh — " + Path.GetFileNameWithoutExtension(path);
        summary.ForeColor = Color.LightSkyBlue;
        summary.Text = "Project loaded. Review the area, inputs and navigation settings, then generate a preview.";
        if (File.Exists(Path.Combine(settings.OutputDirectory, "report.json"))) ShowReport(settings.OutputDirectory);
    }

    private void LoadGameSources(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var value = document.Deserialize<GameSourceManifest>(BakeSettings.Json) ?? throw new InvalidDataException("Empty game source set.");
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        value.GameDirectory = Path.GetFullPath(value.GameDirectory, folder);
        foreach (var archive in value.Archives) archive.Path = Path.GetFullPath(archive.Path, folder);
        // Upgrade only the previous release's untouched automatic base selection.
        bool legacy = !document.RootElement.EnumerateObject().Any(p => p.Name.Equals("autoDiscoverDlc", StringComparison.OrdinalIgnoreCase));
        value.AutoDiscoverDlc |= legacy && !value.ValidatedForBuild && value.Source.StartsWith("Base archives from the installed") &&
            value.Archives.All(a => a.Sha256.Length == 0) && GameSource.IsLegacyDirectory(value.GameDirectory) &&
            value.Archives.Select(a => (a.Path, a.LogicalPath)).SequenceEqual(
                GameSource.BaseGameSelection(value.GameDirectory, value.GameBuild).Archives.Select(a => (a.Path, a.LogicalPath)));
        ApplyGameSources(value);
    }

    private void ApplyGameSources(GameSourceManifest value)
    {
        gameFolder.Text = value.GameDirectory; sourceNotes.Text = value.Source;
        archiveBuild.Value = value.GameBuild; validated.Checked = value.ValidatedForBuild;
        archiveItems.Clear();
        foreach (var archive in value.Archives)
            archiveItems.Add(new ArchiveInput { Path = archive.Path, LogicalPath = archive.LogicalPath, Sha256 = archive.Sha256 });
        automaticDlc.Checked = value.AutoDiscoverDlc; discoveryIssues = value.DiscoveryIssues; preparedGameKey = "";
        useGame.Checked = true;
        RefreshGameStatus();
    }

    private async Task<bool> PrepareGameSources()
    {
        if (!useGame.Checked || !automaticDlc.Checked || validated.Checked) return true;
        string folder = gameFolder.Text; int target = (int)build.Value;
        string key = folder + "|" + target;
        if (preparedGameKey == key) return true;
        GameSourceManifest? sources = null;
        return await RunOperation(() => sources = GameArchiveDiscovery.Discover(folder, target, operation!.Token), () =>
        {
            ApplyGameSources(sources!); preparedGameKey = key;
            status.Text = $"Found {archiveItems.Count} game archives for previewing build {target}.";
        });
    }

    private void DetectGame()
    {
        string? folder = GameSource.FindLegacyDirectory(preferences.GtaFolder);
        if (folder != null) SelectBaseGame(folder);
    }

    private void ChooseGameFolder()
    {
        using var picker = new FolderBrowserDialog { Description = "Choose GTA V Legacy (this choice is remembered)", UseDescriptionForTitle = true,
            InitialDirectory = gameFolder.Text };
        if (picker.ShowDialog(this) == DialogResult.OK) SelectBaseGame(picker.SelectedPath);
    }

    private void SelectBaseGame(string folder)
    {
        var source = GameSource.BaseGameSelection(folder, (int)build.Value);
        ApplyGameSources(source);
        preferences.GtaFolder = source.GameDirectory;
        RefreshGameStatus(); InputsChanged();
    }

    private void RefreshGameStatus()
    {
        gameStatus.Text = useGame.Checked && archiveItems.Count > 0 ? $"GTA V Legacy ready for previews\n{gameFolder.Text}" :
            "GTA files are not selected.\nUse GTA SETTINGS to locate the game, or preview custom collision only.";
        gameStatus.ForeColor = useGame.Checked && archiveItems.Count > 0 ? Color.LightGreen : Color.LightSkyBlue;
        tips.SetToolTip(gameStatus, gameFolder.Text);
    }

    private void SaveSnapshot(BakeSettings value, string path)
    {
        // Build both documents before writing so invalid archive fields do not leave half a project.
        var source = useGame.Checked ? ReadGameSources() : null;
        if (source != null) value.GameSourceFile = Path.ChangeExtension(path, ".game-sources.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (source != null) File.WriteAllText(value.GameSourceFile, JsonSerializer.Serialize(source, BakeSettings.Json));
        File.WriteAllText(path, JsonSerializer.Serialize(value, BakeSettings.Json));
    }

    private void SaveProject()
    {
        var value = ReadSettings();
        using var dialog = new SaveFileDialog { Filter = "NavMesh project|*.json", FileName = projectPath == null ? "navmesh-project.json" : Path.GetFileName(projectPath) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SaveSnapshot(value, dialog.FileName); projectPath = dialog.FileName;
        Text = "BLRP NavMesh — " + Path.GetFileNameWithoutExtension(projectPath); status.Text = "Project saved.";
    }

    private async Task BakeClicked(bool diagnostic)
    {
        BakeSettings value;
        try
        {
            if (!await PrepareGameSources()) return;
            if (diagnostic && autoFit.Checked && maps.CheckedItems.Count > 0 && !await FitMapArea()) return;
            value = ReadSettings();
            if (!diagnostic && value.BaselineDirectory.Length == 0) throw new InvalidDataException("Select original navigation and enable inclusion of the surrounding navigation before building a resource.");
            string run = Path.Combine(value.OutputDirectory, "bake-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..4]);
            value.OutputDirectory = run; value.Validate(); SaveSnapshot(value, run + ".json");
        }
        catch (Exception e) { ShowError(e); return; }
        latestOutput = value.OutputDirectory; preview.Clear(); SetIssues([]); log.Clear();
        summary.Text = diagnostic ? "Generating inspection output…" : "Checking inputs and building a resource…";
        await RunOperation(() => Program.Bake(value, diagnostic, operation!.Token), () => { });
        if (File.Exists(Path.Combine(value.OutputDirectory, "report.json"))) ShowReport(value.OutputDirectory, true);
    }

    private async Task CaptureBaseline()
    {
        try
        {
            if (!useGame.Checked) throw new InvalidDataException("Configure and enable game archives first.");
            if (!await PrepareGameSources()) return;
            var value = ReadSettings(requireBaseline: false);
            string destination = Path.Combine(value.OutputDirectory, "original-navigation-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..4]);
            string snapshot = Path.Combine(value.OutputDirectory, "capture-" + Guid.NewGuid().ToString("N") + ".json");
            SaveSnapshot(value, snapshot);
            await RunOperation(() =>
            {
                var source = new GameSource(value, new CollisionScene(value));
                operation!.Token.ThrowIfCancellationRequested(); source.CaptureBaseline(value, destination);
            }, () => { baseline.Text = destination; compose.Checked = true; status.Text = "Original navigation saved. Generate a new preview to check its connections."; });
        }
        catch (Exception e) { ShowError(e); }
    }

    private async Task HashArchives()
    {
        archives.EndEdit(); var items = archiveItems.ToArray(); var hashes = new List<string>();
        await RunOperation(() =>
        {
            foreach (var item in items)
            { operation!.Token.ThrowIfCancellationRequested(); Console.WriteLine("Hashing " + item.Path); hashes.Add(CollisionScene.Hash(item.Path)); }
        }, () => { for (int i = 0; i < items.Length; i++) items[i].Sha256 = hashes[i]; archiveItems.ResetBindings(); status.Text = "Hashes calculated. Build verification remains a separate review."; });
    }

    private void MoveArchive(int direction)
    {
        archives.EndEdit();
        if (archives.CurrentRow?.DataBoundItem is not ArchiveInput item) return;
        int index = archiveItems.IndexOf(item), next = index + direction;
        if (next < 0 || next >= archiveItems.Count) return;
        archiveItems.RemoveAt(index); archiveItems.Insert(next, item); archives.CurrentCell = archives.Rows[next].Cells[0];
    }

    private async Task<bool> RunOperation(Action work, Action complete)
    {
        if (operation != null) return false;
        operation = new CancellationTokenSource();
        previewReadyForExport = false;
        editor.Enabled = toolbar.Enabled = generate.Enabled = package.Enabled = false;
        cancel.Enabled = progress.Visible = true; status.Text = "Working…";
        RefreshActions();
        var writer = new ProgressWriter(new Progress<string>(text =>
        {
            if (IsDisposed) return;
            if (log.TextLength > 120000) log.Text = log.Text[^80000..];
            log.AppendText(text + Environment.NewLine);
            if (operation != null && !operation.IsCancellationRequested) status.Text = text;
        }));
        var previous = Console.Out; var previousError = Console.Error;
        bool success = false;
        try
        {
            Console.SetOut(writer); Console.SetError(writer);
            await Task.Run(work); operation.Token.ThrowIfCancellationRequested(); complete(); success = true;
        }
        catch (Exception e) { if (operation.IsCancellationRequested) status.Text = "Cancelled. Any completed diagnostics are retained."; else ShowError(e); }
        finally
        {
            Console.SetOut(previous); Console.SetError(previousError);
            operation.Dispose(); operation = null;
            editor.Enabled = toolbar.Enabled = true;
            cancel.Enabled = progress.Visible = false;
            RefreshActions();
            if (closeAfterOperation) BeginInvoke(Close);
        }
        return success;
    }

    internal void ShowReport(string folder, bool currentRun = false)
    {
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "report.json")));
        var root = report.RootElement; latestOutput = folder;
        var messages = root.GetProperty("issues").EnumerateArray().Select(i => i.GetString() ?? "").ToList();
        string result = root.GetProperty("status").GetString() ?? "unknown";
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String) messages.Insert(0, error.GetString()!);
        SetIssues(messages);
        bool incomplete = result == "diagnostic-only" && issueMessages.Length > 0;
        string label = result switch { "diagnostic-only" when incomplete => "PARTIAL PREVIEW — resource build blocked", "diagnostic-only" => "PREVIEW READY — inspection only", "awaiting-fivem-validation" => "RESOURCE BUILT — awaiting FiveM testing", "cancelled" => "CANCELLED", _ => "BUILD STOPPED" };
        summary.Text = $"{label}\n" + (issueMessages.Length > 0 ? $"{issues.Items.Count} categories need attention. Select one below for the next step." : "Review the walkable surfaces, then open Export for the next step.") +
            $"\nServer build {root.GetProperty("gameBuild").GetInt32()} · FiveM movement has not been tested.";
        summary.ForeColor = result == "failed" || incomplete ? Color.Salmon : Color.LightSkyBlue;
        previewReadyForExport = currentRun && issueMessages.Length == 0 && result is "diagnostic-only" or "awaiting-fivem-validation";
        exportStatus.Text = issueMessages.Length > 0 ? "Export is blocked by the checks under Needs attention.\n\n" + string.Join("\n", issues.Items.Cast<IssueGroup>().Select(g => "• " + g.Title)) :
            "Preview checks passed. Prepare the original navigation below and preview again before building. Movement still needs testing in FiveM.";
        if (!currentRun) exportStatus.Text = "You are viewing an earlier result. Generate a preview of the current project before exporting.";
        status.Text = "Preview saved. OPEN RESULTS shows its files and full report.";
        LoadPreview(collisionView.Checked); RefreshActions();
    }

    private void LoadPreview(bool collision)
    {
        if (latestOutput == null) return;
        string path = Path.Combine(latestOutput, collision ? "collision.obj" : "generated-navmesh.obj");
        if (File.Exists(path)) preview.LoadObj(path); else preview.Clear();
    }

    private void ShowError(Exception error)
    {
        string message = error.GetBaseException().Message;
        status.Text = message; SetIssues(new[] { message }.Concat(issueMessages)); log.AppendText(message + Environment.NewLine);
        previewReadyForExport = false; exportStatus.Text = "Resolve the checks under Needs attention, then generate a new preview."; RefreshActions();
    }
    private void ShowHelp()
    {
        if (help == null || help.IsDisposed)
        {
            help = new HelpForm();
            help.Show(this);
        }
        else help.Activate();
    }
    private void TryUi(Action action) { try { action(); } catch (Exception e) { ShowError(e); } }
    internal Task GenerateForTest() => BakeClicked(true);
    internal string? LatestOutput => latestOutput;
    internal int PreviewPolygonCount => preview.PolygonCount;
    internal int IssueCount => issueMessages.Length;
    internal int IssueGroupCount => issues.Items.Count;
    internal bool ExportEnabled => package.Enabled;
    internal bool GameDetected => useGame.Checked && archiveItems.Count > 0;
    internal bool AdvancedVisible => advanced.Checked;
    internal bool AutomaticArea => autoFit.Checked;
    internal bool PreviewEnabled => generate.Enabled;
    internal void AddResourceForTest(string path) => AddResource(path);
    internal void SelectMapsForTest(string[] selected)
    {
        for (int i = 0; i < maps.Items.Count; i++) maps.SetItemChecked(i, selected.Contains((string)maps.Items[i], StringComparer.OrdinalIgnoreCase));
        RefreshActions();
    }
    internal void SetAdvancedForTest(bool value) => advanced.Checked = value;
    internal void SaveForTest(string path) => SaveSnapshot(ReadSettings(), path);
    internal void CaptureTabsForTest(string path)
    {
        for (int i = 0; i < editor.TabCount; i++)
        {
            editor.SelectedIndex = i; PerformLayout();
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            string outputPath = i == 0 ? path : Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "-tab" + i + ".png");
            bitmap.Save(outputPath);
        }
    }
    private static string[] Names(string value) => value.Split([',', ';', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private void SetIssues(IEnumerable<string> messages)
    {
        issueMessages = messages.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToArray();
        issues.Items.Clear(); issues.Items.AddRange(IssueGroup.From(issueMessages));
        issueDetails.Text = issueMessages.Length == 0 ? "No issues reported. Preview results still need visual review and testing in FiveM." : "Select a category for the next step.";
        if (issues.Items.Count > 0) issues.SelectedIndex = 0;
    }

    private void SetAdvanced(bool visible)
    {
        foreach (var page in advancedPages)
        {
            if (visible && !editor.TabPages.Contains(page)) editor.TabPages.Insert(editor.TabPages.IndexOf(exportPage), page);
            else if (!visible) editor.TabPages.Remove(page);
        }
        if (!visible) editor.SelectedIndex = 0;
    }

    private void InputsChanged()
    {
        if (applyingSettings || operation != null) return;
        previewReadyForExport = false;
        exportStatus.Text = "Generate a preview of these settings first. Any missing inputs will be explained under Needs attention.";
        RefreshActions();
    }

    private void RefreshActions()
    {
        generate.Enabled = operation == null && (maps.CheckedItems.Count > 0 || collisions.Rows.Count > 0);
        package.Enabled = operation == null && previewReadyForExport && compose.Checked && !string.IsNullOrWhiteSpace(baseline.Text);
        foreach (var button in new[] { generate, package, cancel }) button.BackColor = button.Enabled ? BlrpTheme.Accent : Color.FromArgb(112, 120, 136);
    }

    private void ConfigureInputEvents()
    {
        void Watch(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is TextBoxBase text) text.TextChanged += (_, _) => InputsChanged();
                if (control is NumericUpDown number) number.ValueChanged += (_, _) => InputsChanged();
                if (control is CheckBox check) check.CheckedChanged += (_, _) => InputsChanged();
                Watch(control);
            }
        }
        Watch(editor);
        foreach (var number in area) number.ValueChanged += (_, _) =>
        { if (!applyingSettings && operation == null) { autoFit.Checked = false; areaHint.Text = "Using your custom preview area."; } };
        build.ValueChanged += (_, _) =>
        {
            if (applyingSettings) return;
            // A target-build edit never certifies the selected game files.
            validated.Checked = false;
            if (automaticDlc.Checked || sourceNotes.Text.StartsWith("Base archives from the installed")) archiveBuild.Value = build.Value;
        };
        autoFit.CheckedChanged += (_, _) => { if (!applyingSettings) areaHint.Text = autoFit.Checked ? "The preview area will be fitted to your selected map." : "Using the saved area. Edit coordinates under Advanced settings → Area & layout."; };
        useGame.CheckedChanged += (_, _) => RefreshGameStatus();
        void ArchivesEdited()
        {
            if (!applyingSettings && operation == null) { automaticDlc.Checked = false; discoveryIssues = []; }
            InputsChanged(); RefreshGameStatus();
        }
        automaticDlc.CheckedChanged += (_, _) => preparedGameKey = "";
        archiveItems.ListChanged += (_, _) => ArchivesEdited();
        archives.CellValueChanged += (_, _) => ArchivesEdited(); collisions.CellValueChanged += (_, _) => InputsChanged();
        collisions.RowsAdded += (_, _) => InputsChanged(); collisions.RowsRemoved += (_, _) => InputsChanged();
        agent.PropertyValueChanged += (_, _) => InputsChanged();
        foreach (var list in new[] { maps, entitySets }) list.ItemCheck += (_, _) =>
        { InputsChanged(); if (IsHandleCreated) BeginInvoke((Action)RefreshActions); };
        foreach (var list in new ListBox[] { resources, maps })
        {
            list.FormattingEnabled = true;
            list.Format += (_, e) => { if (e.ListItem is string path) e.Value = Path.GetFileName(Path.TrimEndingDirectorySeparator(path)); };
            list.MouseMove += (_, e) =>
            {
                int index = list.IndexFromPoint(e.Location);
                tips.SetToolTip(list, index >= 0 ? (string)list.Items[index] : "");
            };
        }
    }
    private static TableLayoutPanel Table() => new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 0 };
    private static Label Caption(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, UseMnemonic = false };
    private static NumericUpDown Number(decimal value, decimal min, decimal max, int places = 6) => new() { Minimum = min, Maximum = max, Value = value, DecimalPlaces = places, Dock = DockStyle.Fill };
    private static DataGridView Grid() => new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None };
    private Button Button(string text, Action click)
    {
        var button = new Button { Text = text, UseMnemonic = false, AutoSize = true, Height = 32, MinimumSize = new Size(70, 32), Padding = new Padding(5, 0, 5, 0) };
        button.Click += (_, _) => TryUi(click); return button;
    }
    private Button AsyncButton(string text, Func<Task> click)
    {
        var button = Button(text, () => { });
        button.Click += async (_, _) => { try { await click(); } catch (Exception e) { if (!IsDisposed) ShowError(e); } };
        return button;
    }
    private TableLayoutPanel Tab(string title)
    {
        var tab = new TabPage(title) { BackColor = BlrpTheme.Background, AutoScroll = true };
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tab.Controls.Add(table); editor.TabPages.Add(tab); return table;
    }
    private static void Row(TableLayoutPanel table, string label, Control value, int height)
    {
        int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        table.Controls.Add(Caption(label), 0, row); table.Controls.Add(value, 1, row);
        value.Dock = DockStyle.Fill;
    }
    private static void FullRow(TableLayoutPanel table, Control value, int height)
    {
        int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        table.Controls.Add(value, 0, row); table.SetColumnSpan(value, 2); value.Dock = DockStyle.Fill;
    }
    private Control FolderField(TextBox field)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        table.Controls.Add(field); var browse = Button("BROWSE", () => PickFolder(path => field.Text = path)); browse.Dock = DockStyle.Fill; browse.AutoSize = false; table.Controls.Add(browse); return table;
    }
    private Control ListButtons(Action add, Action remove, string addLabel = "ADD")
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        panel.Controls.AddRange([Button(addLabel, add), Button("REMOVE", remove)]); return panel;
    }
    private Control ListWithActions(Control list, Action add, Action remove, string addLabel = "ADD")
    {
        var table = Table(); table.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); table.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        table.Controls.Add(list, 0, 0); table.Controls.Add(ListButtons(add, remove, addLabel), 0, 1); return table;
    }
    private void PickFolder(Action<string> action)
    {
        using var dialog = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = "Choose a folder" };
        if (dialog.ShowDialog(this) == DialogResult.OK) action(dialog.SelectedPath);
    }
    private void PickFile(string filter, Action<string> action) => PickFiles(filter, paths => action(paths[0]), false);
    private void PickFiles(string filter, Action<string[]> action, bool multiple = true)
    {
        using var dialog = new OpenFileDialog { Filter = filter, Multiselect = multiple };
        if (dialog.ShowDialog(this) == DialogResult.OK) action(dialog.FileNames);
    }
    private static void OpenFolder(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    private sealed class ProgressWriter(IProgress<string> progress) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        private readonly StringBuilder pending = new();
        public override void Write(char value)
        {
            if (value == '\n') { progress.Report(pending.ToString().TrimEnd('\r')); pending.Clear(); }
            else pending.Append(value);
        }
        public override void WriteLine(string? value) => progress.Report(value ?? "");
    }
}
