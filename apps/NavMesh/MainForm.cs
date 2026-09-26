using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BLRP.NavMesh;

internal sealed class MainForm : Form
{
    private readonly TabControl editor = new() { Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel toolbar = new() { Dock = DockStyle.Fill, WrapContents = false };
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
    private readonly TextBox gameFolder = new() { Dock = DockStyle.Fill };
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
    private readonly Label summary = Caption("Select a resource and its maps, then read the map area.");
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

    public MainForm(string? initialPath = null)
    {
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
        foreach (var tab in editor.TabPages.Cast<TabPage>()) { tab.UseVisualStyleBackColor = false; tab.BackColor = BlrpTheme.Background; }
        foreach (var list in new ListBox[] { resources, maps, entitySets, issues }) { list.BackColor = BlrpTheme.Input; list.ForeColor = Color.White; }
        agent.ViewBackColor = BlrpTheme.Input; agent.ViewForeColor = Color.White;
        agent.HelpBackColor = BlrpTheme.Card; agent.HelpForeColor = Color.White;
        agent.LineColor = BlrpTheme.Card;
        ApplySettings(new BakeSettings { GameBuild = 3095, Min = [0, 0, -1], Max = [10, 10, 4],
            OutputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BLRP", "NavMesh") });
        FormClosing += (_, e) =>
        {
            if (operation == null) return;
            e.Cancel = true; closeAfterOperation = true; operation.Cancel();
            status.Text = "Finishing the current step before closing…";
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
            projectPath = null; latestOutput = null; preview.Clear(); issues.Items.Clear(); log.Clear();
            ApplySettings(new BakeSettings { GameBuild = (int)build.Value, Min = [0, 0, -1], Max = [10, 10, 4], OutputDirectory = output.Text });
            Text = "BLRP NavMesh"; summary.Text = "New project. Select a resource and its maps.";
        }));
        toolbar.Controls.Add(Button("OPEN PROJECT", () => PickFile("Bake projects|*.json", path => LoadProject(path))));
        toolbar.Controls.Add(Button("SAVE PROJECT", SaveProject));
        toolbar.Controls.Add(Button("OPEN RESULTS", () => { if (latestOutput != null) OpenFolder(latestOutput); }));
        toolbar.Controls.Add(Button("OPEN PREVIOUS RESULT", () => PickFile("Bake reports|report.json", path => ShowReport(Path.GetDirectoryName(path)!))));
        page.Controls.Add(toolbar, 0, 1);

        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1300, 700), SplitterDistance = 580,
            Panel1MinSize = 480, Panel2MinSize = 400 };
        split.Panel1.Padding = new Padding(0, 0, 10, 0); split.Panel1.Controls.Add(editor);
        BuildMapTab(); BuildArchiveTab(); BuildNavigationTab(); BuildCollisionTab();
        var result = Table();
        result.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        result.RowStyles.Add(new RowStyle(SizeType.Percent, 63));
        result.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        result.RowStyles.Add(new RowStyle(SizeType.Percent, 37));
        summary.Padding = new Padding(8); result.Controls.Add(summary, 0, 0);
        result.Controls.Add(preview, 0, 1);
        var viewControls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var slice = new CheckBox { Text = "Floor at Z", AutoSize = true, Margin = new Padding(6, 7, 0, 0) };
        var height = Number(10, -100000, 100000, 2); height.Width = 90; height.Dock = DockStyle.None;
        void SetSlice() { preview.FloorHeight = slice.Checked ? (float)height.Value : null; preview.Invalidate(); }
        slice.CheckedChanged += (_, _) => SetSlice(); height.ValueChanged += (_, _) => SetSlice();
        viewControls.Controls.AddRange([Button("FIT VIEW", preview.Fit), slice, height]);
        var collisionView = new CheckBox { Text = "Collision", AutoSize = true, Margin = new Padding(10, 7, 0, 0) };
        collisionView.CheckedChanged += (_, _) => TryUi(() => LoadPreview(collisionView.Checked));
        viewControls.Controls.Add(collisionView); result.Controls.Add(viewControls, 0, 2);
        var details = new TabControl { Dock = DockStyle.Fill };
        var issueTab = new TabPage("Issues"); issueTab.Controls.Add(issues);
        var logTab = new TabPage("Activity"); logTab.Controls.Add(log);
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
        var body = Tab("Map & area");
        Row(body, "Mapping resources", ListWithActions(resources, () => PickFolder(path => AddResource(path)), () =>
        {
            if (resources.SelectedItem is not string root) return;
            resources.Items.Remove(root);
            foreach (string path in maps.Items.Cast<string>().Where(p => Path.GetRelativePath(root, p) is var relative && !relative.StartsWith("..")).ToArray()) maps.Items.Remove(path);
        }), 132);
        Row(body, "Maps to include", ListWithActions(maps, () => PickFiles("Map placements|*.ymap", paths =>
        { foreach (string path in paths) if (!maps.Items.Contains(path)) maps.Items.Add(path, true); }), () =>
        { if (maps.SelectedIndex >= 0) maps.Items.RemoveAt(maps.SelectedIndex); }), 145);
        Row(body, "World area", AsyncButton("READ MAP BOUNDS & ENTITY SETS", ReadMapDetails), 40);
        var bounds = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3 };
        bounds.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        for (int i = 0; i < 3; i++) bounds.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        for (int i = 0; i < 3; i++) { bounds.Controls.Add(Caption(new[] { "X", "Y", "Z" }[i]), i + 1, 0); bounds.Controls.Add(area[i], i + 1, 1); bounds.Controls.Add(area[i + 3], i + 1, 2); }
        bounds.Controls.Add(Caption("Min"), 0, 1); bounds.Controls.Add(Caption("Max"), 0, 2);
        Row(body, "Replacement box (m)", bounds, 96);
        Row(body, "Active entity sets", entitySets, 115);
        Row(body, "", Caption("Unchecked sets are excluded. Match the sets enabled on your server."), 42);
        Row(body, "Game build", build, 34);
        Row(body, "Results folder", FolderField(output), 36);
        Row(body, "", Caption("Each run gets a new folder. Preview output is for inspection; a resource build requires complete inputs."), 58);
        Row(body, "", compose, 32);
        Row(body, "Original navigation", FolderField(baseline), 36);
        Row(body, "", AsyncButton("CAPTURE ORIGINAL NAVIGATION", CaptureBaseline), 40);
        Row(body, "Conflict scan folders", conflicts, 66);
        Row(body, "", Button("ADD SCAN FOLDER", () => PickFolder(path => conflicts.AppendText((conflicts.Text.Length == 0 ? "" : Environment.NewLine) + path))), 36);
    }

    private void BuildArchiveTab()
    {
        var body = Tab("Game archives");
        Row(body, "", useGame, 34);
        Row(body, "GTA Legacy folder", FolderField(gameFolder), 36);
        Row(body, "Quick setup", Button("LOAD BASE GAME", LoadBaseGame), 40);
        Row(body, "", Caption("Finds the installed Legacy game and loads base archives for previews. Add build-matched DLC/update archives as needed."), 62);
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
        var body = Tab("Navigation");
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

    private void AddResource(string path)
    {
        if (!resources.Items.Contains(path)) resources.Items.Add(path);
        var files = Directory.EnumerateFiles(path, "*.ymap", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (string map in files) if (!maps.Items.Contains(map)) maps.Items.Add(map, files.Length == 1);
        string name = Path.GetFileName(path);
        if (name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            dependencies.Text = string.Join(", ", Names(dependencies.Text).Append(name).Distinct());
        status.Text = $"Found {files.Length} maps. Check the placements to include, then read their bounds.";
    }

    private async Task ReadMapDetails()
    {
        var value = new BakeSettings { GameBuild = (int)build.Value,
            ResourceRoots = resources.Items.Cast<string>().ToArray(), Ymaps = maps.CheckedItems.Cast<string>().ToArray() };
        archives.EndEdit();
        var sources = useGame.Checked ? ReadGameSources() : null;
        MapDetails? details = null;
        await RunOperation(() => { details = MapSelection.Read(value, sources); }, () =>
        {
            if (details == null) return;
            for (int i = 0; i < 3; i++) { area[i].Value = (decimal)details.Min[i] - 0.5m; area[i + 3].Value = (decimal)details.Max[i] + 0.5m; }
            var previous = ReadEntitySets(); entitySets.Items.Clear();
            foreach (var choice in details.Sets)
                entitySets.Items.Add(choice, previous.TryGetValue(choice.Placement, out var names) && names.Contains(choice.Name));
            settings.EntitySets = details.Sets.Select(s => s.Placement).Distinct().ToDictionary(key => key, _ => Array.Empty<string>());
            issues.Items.Clear(); issues.Items.AddRange(details.Unresolved);
            preview.Clear();
            summary.ForeColor = Color.LightSkyBlue;
            var size = details.Max - details.Min + new SharpDX.Vector3(1);
            summary.Text = $"MAP AREA READY — {size.X:N1} × {size.Y:N1} × {size.Z:N1} m\n" +
                $"{details.CollisionBoundsCount} interior collision bounds · {details.MetadataBoundsCount} object bounds · {details.Unresolved.Length} notices\n" +
                "Generate a preview to check floors, entrances and remaining collision issues.";
            status.Text = "Interior area fitted to collision where available. Review approaches and active entity sets before generating.";
        });
    }

    internal Task ReadMapDetailsForTest() => ReadMapDetails();

    private Dictionary<string, string[]> ReadEntitySets()
    {
        var result = settings.EntitySets.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var group in entitySets.Items.Cast<EntitySetChoice>().GroupBy(c => c.Placement))
            result[group.Key] = group.Where(choice => entitySets.CheckedItems.Contains(choice)).Select(c => c.Name).ToArray();
        return result;
    }

    internal BakeSettings ReadSettings()
    {
        collisions.EndEdit(); archives.EndEdit();
        if (compose.Checked && string.IsNullOrWhiteSpace(baseline.Text))
            throw new InvalidDataException("Choose the original navigation folder, or turn off inclusion of surrounding navigation for a standalone preview.");
        var result = new BakeSettings
        {
            GameBuild = (int)build.Value, ResourceRoots = resources.Items.Cast<string>().ToArray(),
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
        ValidatedForBuild = validated.Checked,
        Archives = archiveItems.Select(a => new ArchiveInput { Path = Path.GetFullPath(a.Path), LogicalPath = a.LogicalPath, Sha256 = a.Sha256.Trim() }).ToArray()
    };

    internal void ApplySettings(BakeSettings value)
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
        collisions.Rows.Clear(); foreach (var input in value.Collision) AddCollision(input);
        archiveItems.Clear(); gameFolder.Clear(); sourceNotes.Clear(); validated.Checked = false; archiveBuild.Value = value.GameBuild;
        useGame.Checked = value.GameSourceFile.Length > 0;
        if (useGame.Checked) LoadGameSources(value.GameSourceFile);
    }

    private void AddCollision(CollisionInput input) => collisions.Rows.Add(new object[] { input.Path }.Concat(
        input.Position.Concat(input.Orientation).Concat(input.Scale).Select(v => (object)v.ToString("R", CultureInfo.InvariantCulture))).ToArray());

    internal void LoadProject(string path)
    {
        ApplySettings(BakeSettings.Load(path)); projectPath = path;
        Text = "BLRP NavMesh — " + Path.GetFileNameWithoutExtension(path);
        summary.Text = "Project loaded. Review the area, inputs and navigation settings, then generate a preview.";
        if (File.Exists(Path.Combine(settings.OutputDirectory, "report.json"))) ShowReport(settings.OutputDirectory);
    }

    private void LoadGameSources(string path)
    {
        var value = JsonSerializer.Deserialize<GameSourceManifest>(File.ReadAllText(path), BakeSettings.Json) ?? throw new InvalidDataException("Empty game source set.");
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        gameFolder.Text = Path.GetFullPath(value.GameDirectory, folder); sourceNotes.Text = value.Source;
        archiveBuild.Value = value.GameBuild; validated.Checked = value.ValidatedForBuild;
        archiveItems.Clear();
        foreach (var archive in value.Archives)
            archiveItems.Add(new ArchiveInput { Path = Path.GetFullPath(archive.Path, folder), LogicalPath = archive.LogicalPath, Sha256 = archive.Sha256 });
        useGame.Checked = true;
    }

    private void LoadBaseGame()
    {
        if (archiveItems.Count != 0)
            throw new InvalidDataException("Game archives are already selected. Use ADD to extend that source set, or remove its entries before loading a fresh base selection.");
        string folder = gameFolder.Text.Trim();
        if (folder.Length == 0) folder = GameSource.FindLegacyDirectory() ?? "";
        if (folder.Length == 0)
        {
            using var picker = new FolderBrowserDialog { Description = "Choose your GTA V Legacy installation", UseDescriptionForTitle = true };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            folder = picker.SelectedPath;
        }
        var source = GameSource.BaseGameSelection(folder, (int)build.Value);
        gameFolder.Text = source.GameDirectory; archiveBuild.Value = source.GameBuild; sourceNotes.Text = source.Source;
        foreach (var archive in source.Archives) archiveItems.Add(archive);
        useGame.Checked = true; validated.Checked = false;
        status.Text = $"Loaded {source.Archives.Length} base archives for inspection. Read map bounds again; build compatibility still needs review.";
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
            value = ReadSettings();
            if (!diagnostic && value.BaselineDirectory.Length == 0) throw new InvalidDataException("Select original navigation and enable inclusion of the surrounding navigation before building a resource.");
            string run = Path.Combine(value.OutputDirectory, "bake-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..4]);
            value.OutputDirectory = run; value.Validate(); SaveSnapshot(value, run + ".json");
        }
        catch (Exception e) { ShowError(e); return; }
        latestOutput = value.OutputDirectory; preview.Clear(); issues.Items.Clear(); log.Clear();
        summary.Text = diagnostic ? "Generating inspection output…" : "Checking inputs and building a resource…";
        await RunOperation(() => Program.Bake(value, diagnostic, operation!.Token), () => { });
        if (File.Exists(Path.Combine(value.OutputDirectory, "report.json"))) ShowReport(value.OutputDirectory);
    }

    private async Task CaptureBaseline()
    {
        try
        {
            if (!useGame.Checked) throw new InvalidDataException("Configure and enable game archives first.");
            var value = ReadSettings();
            using var dialog = new FolderBrowserDialog { Description = "Choose a new, empty folder for original navigation", UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            string destination = dialog.SelectedPath;
            string snapshot = Path.Combine(value.OutputDirectory, "capture-" + Guid.NewGuid().ToString("N") + ".json");
            SaveSnapshot(value, snapshot);
            await RunOperation(() =>
            {
                var source = new GameSource(value, new CollisionScene(value));
                operation!.Token.ThrowIfCancellationRequested(); source.CaptureBaseline(value, destination);
            }, () => { baseline.Text = destination; compose.Checked = true; status.Text = "Original navigation captured. Its build verification status is retained."; });
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

    private async Task RunOperation(Action work, Action complete)
    {
        if (operation != null) return;
        operation = new CancellationTokenSource();
        editor.Enabled = toolbar.Enabled = generate.Enabled = package.Enabled = false;
        cancel.Enabled = progress.Visible = true; status.Text = "Working…";
        var writer = new ProgressWriter(new Progress<string>(text =>
        {
            if (IsDisposed) return;
            if (log.TextLength > 120000) log.Text = log.Text[^80000..];
            log.AppendText(text + Environment.NewLine);
            if (operation != null && !operation.IsCancellationRequested) status.Text = text;
        }));
        var previous = Console.Out; var previousError = Console.Error;
        try
        {
            Console.SetOut(writer); Console.SetError(writer);
            await Task.Run(work); operation.Token.ThrowIfCancellationRequested(); complete();
        }
        catch (Exception e) { if (operation.IsCancellationRequested) status.Text = "Cancelled. Any completed diagnostics are retained."; else ShowError(e); }
        finally
        {
            Console.SetOut(previous); Console.SetError(previousError);
            operation.Dispose(); operation = null;
            editor.Enabled = toolbar.Enabled = generate.Enabled = package.Enabled = true;
            cancel.Enabled = progress.Visible = false;
            if (closeAfterOperation) BeginInvoke(Close);
        }
    }

    internal void ShowReport(string folder)
    {
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "report.json")));
        var root = report.RootElement; latestOutput = folder; issues.Items.Clear();
        foreach (var issue in root.GetProperty("issues").EnumerateArray()) issues.Items.Add(issue.GetString() ?? "");
        string result = root.GetProperty("status").GetString() ?? "unknown";
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String) issues.Items.Insert(0, error.GetString()!);
        int tiles = root.GetProperty("outputHashes").EnumerateObject().Count();
        bool incomplete = result == "diagnostic-only" && issues.Items.Count > 0;
        string label = result switch { "diagnostic-only" when incomplete => "PARTIAL PREVIEW — resource build blocked", "diagnostic-only" => "PREVIEW READY — inspection only", "awaiting-fivem-validation" => "RESOURCE BUILT — awaiting FiveM testing", "cancelled" => "CANCELLED", _ => "BUILD STOPPED" };
        summary.Text = $"{label}\n{root.GetProperty("collisionTriangleCount").GetInt32():N0} collision triangles · {tiles} native tiles · {issues.Items.Count} issues\nClient build: {root.GetProperty("gameBuild").GetInt32()} · movement in FiveM remains unverified";
        summary.ForeColor = result == "failed" || incomplete ? Color.Salmon : Color.LightSkyBlue;
        status.Text = folder; LoadPreview(false);
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
        status.Text = message; issues.Items.Insert(0, message); log.AppendText(message + Environment.NewLine);
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
    internal int IssueCount => issues.Items.Count;
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
    private static TableLayoutPanel Table() => new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 0 };
    private static Label Caption(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
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
    private Control FolderField(TextBox field)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        table.Controls.Add(field); var browse = Button("BROWSE", () => PickFolder(path => field.Text = path)); browse.Dock = DockStyle.Fill; table.Controls.Add(browse); return table;
    }
    private Control ListButtons(Action add, Action remove)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        panel.Controls.AddRange([Button("ADD", add), Button("REMOVE", remove)]); return panel;
    }
    private Control ListWithActions(Control list, Action add, Action remove)
    {
        var table = Table(); table.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); table.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        table.Controls.Add(list, 0, 0); table.Controls.Add(ListButtons(add, remove), 0, 1); return table;
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
