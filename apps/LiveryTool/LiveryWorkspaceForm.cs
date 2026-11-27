using System.ComponentModel;
using System.Xml.Linq;

namespace Badlands.LiveryTool;

internal sealed class ImportRow
{
    public string Image { get; set; } = "";
    public string Vehicle { get; set; } = "";
    public string LiveryName { get; set; } = "";
    public string AccessGroup { get; set; } = "";
    public string Permission { get; set; } = "";
    public bool FitImage { get; set; }
    public string ExportFolder { get; set; } = "";
    [Browsable(false)] public bool AutomaticFolder { get; set; }
    public string Status { get; set; } = "Choose vehicle";
    [Browsable(false)] public VehicleKit? Kit { get; set; }
    [Browsable(false)] public VehicleTemplate? Template { get; set; }
    [Browsable(false)] public string Texture { get; set; } = "";
    [Browsable(false)] public string OutputName { get; set; } = "";
    [Browsable(false)] public string Label { get; set; } = "";
    [Browsable(false)] public bool Completed { get; set; }
    [Browsable(false)] public bool ConvertModkit { get; set; }
    [Browsable(false)] public bool ReplaceExisting { get; set; }
    [Browsable(false)] public string ReplacementKey { get; set; } = "";
}

internal sealed class LiveryWorkspaceForm : Form
{
    private readonly TextBox repository = new() { Dock = DockStyle.Fill };
    private readonly TextBox search = new() { Dock = DockStyle.Top, PlaceholderText = "Search model / spawn name, display name or resource…", Height = 32 };
    private readonly ListBox vehicles = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
    private readonly CheckBox showUnclassified = new() { Dock = DockStyle.Top, Height = 26, Text = "Include models needing metadata" };
    private readonly Label vehicleDetails = new() { Dock = DockStyle.Bottom, Height = 95, AutoEllipsis = true };
    private readonly DataGridView queue = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly BindingList<ImportRow> rows = [];
    private readonly TextBox sharedName = new() { Width = 155, PlaceholderText = "Livery name (optional)" };
    private readonly TextBox sharedAccess = new() { Width = 170, PlaceholderText = "Access group (empty = public)" };
    private readonly TextBox sharedPermission = new() { Width = 140, PlaceholderText = "Optional permission" };
    private readonly CheckBox fit = new() { Text = "Fit image to template (transparent padding)", AutoSize = true };
    private readonly TextBox reviewText = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly TextBox diagnostics = new() { Width = 720, Height = 180, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 36, Text = "Select a repository and scan vehicles." };
    private readonly Button install = new LiveryButton { Text = "Export liveries", Primary = true, Enabled = false };
    private readonly Button cancel = new LiveryButton { Text = "Cancel", Visible = false };
    private readonly ComboBox kits = new() { Width = 430, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox templates = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox textures = new() { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox outputName = new() { Width = 210, PlaceholderText = "Automatic asset name" };
    private readonly TextBox label = new() { Width = 210, PlaceholderText = "Automatic label" };
    private readonly TextBox accessFile = new() { Width = 720, PlaceholderText = "Automatic: discover the livery access / display-name Lua file" };
    private readonly CheckBox convertModkit = new() { AutoSize = true, Text = "Convert to a dedicated modkit and update the master list" };
    private readonly Label conversionTarget = new() { AutoSize = true };
    private readonly TextBox masterList = new() { Width = 720, PlaceholderText = "Automatic: find the modkit master list in the selected repository" };
    private readonly HashSet<string> conversionVehicles = new(StringComparer.OrdinalIgnoreCase);
    private bool updatingConversion;
    private readonly CheckBox replaceExisting = new() { AutoSize = true, Text = "Replace an existing livery (YFT only)" };
    private readonly LiveryPicker replacements = new() { Width = 720, AccessibleName = "Livery to replace" };
    private readonly Label replacementTarget = new() { AutoSize = true, MaximumSize = new Size(720, 0) };
    private readonly Label replacementHint = new() { AutoSize = true, MaximumSize = new Size(720, 0), Margin = new Padding(0, 0, 0, 14) };
    private VehicleCatalog? replacementCatalog;
    private string replacementVehicleKey = "";
    private Task<ExistingLivery[]>? replacementLoad;
    private bool updatingReplacement;
    private int replacementGeneration;
    private ExistingLivery? previewExisting;
    private string previewBaseVehicleKey = "";
    private readonly LiveryTabs tabs = new() { Dock = DockStyle.Fill };
    private readonly VehiclePreviewPanel preview = new() { Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Top, Height = 54, WrapContents = false, Padding = new Padding(8) };
    private readonly Panel emptyQueue = new() { Dock = DockStyle.Fill };
    private readonly Label queueCount = new() { AutoSize = true, Padding = new Padding(10) };
    private readonly ProgressBar progressBar = new() { Dock = DockStyle.Bottom, Height = 3, Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly LiveryBatchWorkflow workflow = new();
    private VehicleCatalog? catalog;
    private LiveryPlan? plan;
    private CancellationTokenSource? operation;
    private CancellationTokenSource? previewOperation;
    private readonly System.Windows.Forms.Timer previewDebounce = new() { Interval = 600 };
    private bool changing;
    private bool busy;
    private bool previewVehicleOnly = true;
    private ImportRow? advancedRow;
    private ToolSettings settings;
    private readonly Dictionary<string, VehicleChoice> remembered;
    private ImportRow[] plannedRows = [];
    private string lastExportFolder = "";

    public LiveryWorkspaceForm(bool persistSettings = true, string? initialRepository = null)
    {
        settings = ToolSettingsStore.Load();
        remembered = settings.VehicleChoices ?? new Dictionary<string, VehicleChoice>();
        Text = "BLRP Livery Studio";
        Size = new Size(1420, 900);
        MinimumSize = new Size(1120, 740);
        StartPosition = FormStartPosition.CenterScreen;
        repository.Text = initialRepository ?? settings.RepoRoot;
        if (string.Equals(repository.Text, settings.RepoRoot, StringComparison.OrdinalIgnoreCase) && settings.ModkitMasterListPath.Length > 0)
        {
            string saved = Path.IsPathRooted(settings.ModkitMasterListPath) ? settings.ModkitMasterListPath : Path.Combine(repository.Text, settings.ModkitMasterListPath);
            if (File.Exists(saved)) masterList.Text = PortablePath(saved);
        }
        sharedAccess.Text = settings.AccessGroup;
        sharedPermission.Text = settings.Permission;
        fit.Checked = settings.FitImage;
        var repoRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 44, ColumnCount = 4, Padding = new Padding(4) };
        repoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        repoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        repoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        repoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        repoRow.Controls.Add(new Label { Text = "Repository", AutoSize = true }, 0, 0);
        repoRow.Controls.Add(repository, 1, 0);
        repoRow.Controls.Add(Button("Browse…", BrowseRepository), 2, 0);
        repoRow.Controls.Add(Button("Scan vehicles", async () => await Scan()), 3, 0);
        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 80, ColumnCount = 2, Padding = new Padding(20, 10, 20, 10) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        header.Controls.Add(new Label { Text = "BLRP  /  Livery Studio", AutoSize = true, Font = new Font("Segoe UI", 19f, FontStyle.Bold) }, 0, 0);
        header.Controls.Add(new Label { Text = "Choose a car. Add your artwork. Make it yours.", AutoSize = true }, 0, 1);
        var project = Button("Change repository", () => { BrowseRepository(); if (catalog == null) _ = Scan(); });
        header.Controls.Add(project, 1, 0); header.SetRowSpan(project, 2);
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1380, 750), SplitterDistance = 300, FixedPanel = FixedPanel.Panel1, Panel1MinSize = 260, SplitterWidth = 10 };
        split.Panel1.Padding = new Padding(16, 8, 8, 8);
        split.Panel2.Padding = new Padding(4, 0, 16, 8);
        split.Panel1.Controls.Add(vehicles);
        split.Panel1.Controls.Add(search);
        split.Panel1.Controls.Add(vehicleDetails);
        var previewCar = Button("Preview selected car", () => { ResetPreviewSelection(); previewVehicleOnly = true; tabs.SelectedIndex = 1; SchedulePreview(); });
        previewCar.Dock = DockStyle.Bottom;
        split.Panel1.Controls.Add(previewCar);
        split.Panel2.Controls.Add(tabs);
        var importTab = new TabPage("Create liveries") { Padding = new Padding(8) };
        var shared = new FlowLayoutPanel { Width = 740, Height = 132, AutoScroll = true };
        shared.Controls.AddRange([sharedName, sharedAccess, sharedPermission, fit,
            Button("Apply settings to selected", () => ApplyShared(false)), Button("Apply settings to all", () => ApplyShared(true)),
            Button("Set vehicle for selected rows", AssignSelectedVehicle)]);
        actions.Controls.AddRange([Button("Add artwork", BrowseImages), Button("Use selected car", AssignSelectedVehicle), Button("Remove", RemoveSelected), queueCount]);
        var queueFooter = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 66, Padding = new Padding(8) };
        var prepare = (LiveryButton)Button("Review export", async () => await Review());
        prepare.Primary = true;
        queueFooter.Controls.Add(prepare);
        queueFooter.Controls.Add(new Label { Text = "YFTs are built automatically. Destinations and metadata are found for you.", AutoSize = true, Padding = new Padding(8, 10, 0, 0) });
        var dropContent = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        dropContent.Controls.Add(new Label { Text = "Drop your artwork here", AutoSize = true, Font = new Font("Segoe UI", 22f, FontStyle.Bold) });
        dropContent.Controls.Add(new Label { Text = "PNG, JPG, TGA or BMP · add one image or a whole batch", AutoSize = true, Padding = new Padding(0, 10, 0, 12) });
        dropContent.Controls.Add(Button("Choose images", BrowseImages));
        emptyQueue.Controls.Add(dropContent);
        emptyQueue.Resize += (_, _) => dropContent.Location = new Point(Math.Max(16, (emptyQueue.Width - dropContent.Width) / 2), Math.Max(20, (emptyQueue.Height - dropContent.Height) / 2));
        importTab.Controls.Add(queue);
        importTab.Controls.Add(emptyQueue);
        importTab.Controls.Add(queueFooter);
        importTab.Controls.Add(actions);
        var previewTab = new TabPage("3D Preview");
        previewTab.Controls.Add(preview);
        var reviewTab = new TabPage("Review export") { Padding = new Padding(12) };
        reviewTab.Controls.Add(reviewText);
        var installRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(8) };
        installRow.Controls.Add(install);
        reviewTab.Controls.Add(installRow);
        var advancedTab = new TabPage("Advanced");
        var advanced = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(20), AutoScroll = true, WrapContents = false };
        repoRow.Dock = DockStyle.None; repoRow.Width = 760;
        advanced.Controls.Add(repoRow);
        advanced.Controls.Add(new Label { Text = "Shared queue settings", AutoSize = true });
        advanced.Controls.Add(shared);
        showUnclassified.Dock = DockStyle.None;
        showUnclassified.Width = 340;
        advanced.Controls.Add(showUnclassified);
        advanced.Controls.Add(replacementTarget);
        advanced.Controls.Add(replaceExisting);
        advanced.Controls.Add(replacements);
        advanced.Controls.Add(replacementHint);
        advanced.Controls.Add(conversionTarget);
        advanced.Controls.Add(convertModkit);
        advanced.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(720, 0), Text = "For a vehicle that needs its first custom modkit. Preserves the original kit and its other users. Review with your livery queue, or convert the selected vehicle without artwork using the button below." });
        advanced.Controls.Add(masterList);
        var modkitActions = new FlowLayoutPanel { Width = 740, Height = 48, WrapContents = false };
        modkitActions.Controls.AddRange([Button("Choose master list…", () => ChooseMasterList()), Button("Review modkit conversion only", async () => await ReviewModkit())]);
        advanced.Controls.Add(modkitActions);
        advanced.Controls.Add(new Label { Text = "Access / display-name Lua override (leave empty for automatic discovery)", AutoSize = true });
        advanced.Controls.Add(accessFile);
        advanced.Controls.Add(Button("Choose livery Lua file…", () => ChooseAccessFile()));
        advanced.Controls.AddRange([new Label { Text = "Overrides for the selected queue row. Only metadata-linked templates are offered.", AutoSize = true },
            new Label { Text = "Modkit", AutoSize = true }, kits, new Label { Text = "Compatible template", AutoSize = true }, templates,
            new Label { Text = "Embedded texture to replace", AutoSize = true }, textures,
            new Label { Text = "Asset filename / label (empty = automatic)", AutoSize = true }, outputName, label,
            Button("Apply overrides to selected row", ApplyOverrides),
            Button("Metadata extraction, texture conversion & sign tools…", OpenUtilities),
            new Label { AutoSize = true, MaximumSize = new Size(650, 0), Text = "Installation supports metadata-linked VMT_LIVERY_MOD assets. Native texture-index liveries, missing modkits, conflicting resource definitions and linked mod assets need additional setup. Existing slots and permissions are preserved. GTA extraction does not change the target server build." }]);
        advancedTab.Controls.Add(advanced);
        advanced.Controls.Add(new Label { Text = "Repository metadata warnings", AutoSize = true });
        advanced.Controls.Add(diagnostics);
        tabs.TabPages.AddRange([importTab, previewTab, reviewTab, advancedTab, HelpPage()]);
        Controls.Add(split);
        Controls.Add(header);
        Controls.Add(status);
        Controls.Add(progressBar);
        var cancelRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 35 };
        cancelRow.Controls.Add(cancel); Controls.Add(cancelRow);
        queue.DataSource = rows;
        foreach (var column in new[] { ("Image", "Image"), ("Vehicle", "Vehicle / resource"), ("LiveryName", "Livery name"), ("AccessGroup", "Access group (public if blank)"), ("Permission", "Permission"), ("Status", "Validation / result") })
        {
            if (column.Item1 == "Vehicle") queue.Columns.Add(new DataGridViewComboBoxColumn { Name = "Vehicle", DataPropertyName = "Vehicle", HeaderText = column.Item2, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton });
            else queue.Columns.Add(new DataGridViewTextBoxColumn { Name = column.Item1, DataPropertyName = column.Item1, HeaderText = column.Item2, ReadOnly = column.Item1 == "Status" });
        }
        queue.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "FitImage", HeaderText = "Fit", FillWeight = 30 });
        queue.Columns.Add(new DataGridViewButtonColumn { Name = "ExportFolder", DataPropertyName = "ExportFolder", HeaderText = "Export folder", FlatStyle = FlatStyle.Flat, FillWeight = 115 });
        queue.Columns["Permission"].Visible = false;
        queue.Columns[^2].Visible = false;
        queue.Columns["Vehicle"].HeaderText = "Vehicle";
        queue.Columns["AccessGroup"].HeaderText = "Access (public if blank)";
        queue.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            if (queue.Columns[e.ColumnIndex].Name == "Image") { e.Value = Path.GetFileName(rows[e.RowIndex].Image); e.FormattingApplied = true; }
            if (queue.Columns[e.ColumnIndex].Name == "ExportFolder") { e.Value = rows[e.RowIndex].ReplaceExisting ? "Replace in place" : rows[e.RowIndex].ExportFolder.Length == 0 ? "Automatic · change…" : new DirectoryInfo(rows[e.RowIndex].ExportFolder).Name; e.FormattingApplied = true; }
        };
        queue.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && queue.Columns[e.ColumnIndex].Name == "ExportFolder") e.ToolTipText = rows[e.RowIndex].ExportFolder;
        };
        queue.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && queue.Columns[e.ColumnIndex].Name == "ExportFolder" && !rows[e.RowIndex].ReplaceExisting) ChooseFolder(rows[e.RowIndex]); };
        rows.ListChanged += (_, _) => { queue.Visible = rows.Count > 0; emptyQueue.Visible = rows.Count == 0; queueCount.Text = rows.Count == 0 ? "" : $"{rows.Count} liveries"; };
        queue.Columns["Image"].FillWeight = 150;
        queue.Columns["Status"].FillWeight = 130;
        queue.DataError += (_, e) => { status.Text = "Choose a vehicle from the scanned catalogue."; e.ThrowException = false; };
        queue.CellValueChanged += (_, e) =>
        {
            if (changing || e.RowIndex < 0) return;
            ImportRow row = rows[e.RowIndex];
            row.Completed = false;
            ResetPreviewSelection();
            previewVehicleOnly = false;
            if (e.ColumnIndex == queue.Columns["Vehicle"].Index) AutoConfigure(row);
            row.Status = "Needs review";
            InvalidatePlan();
            SchedulePreview();
        };
        queue.CurrentCellDirtyStateChanged += (_, _) => { if (queue.IsCurrentCellDirty) queue.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        // SelectionChanged can run before CurrentRow advances, leaving the previous car's overrides
        // on screen. Bind only after the current cell (and therefore the current row) has changed.
        queue.CurrentCellChanged += (_, _) => { if (changing) return; ResetPreviewSelection(); previewVehicleOnly = false; UpdateAdvanced(); SchedulePreview(); };
        search.TextChanged += (_, _) => FilterVehicles();
        showUnclassified.CheckedChanged += (_, _) => FilterVehicles();
        vehicles.SelectedIndexChanged += (_, _) =>
        {
            ResetPreviewSelection();
            previewVehicleOnly = true;
            if (vehicles.SelectedItem is CatalogVehicle vehicle) vehicleDetails.Text = $"{vehicle.Model} — {vehicle.DisplayName}\r\nResource: {vehicle.Resource}\r\n{vehicle.Status}";
            if (Current == null) UpdateAdvanced();
            SchedulePreview();
        };
        vehicles.DoubleClick += (_, _) => { previewVehicleOnly = true; tabs.SelectedIndex = 1; SchedulePreview(); };
        kits.SelectedIndexChanged += (_, _) => LoadTemplates();
        templates.SelectedIndexChanged += (_, _) => LoadTextures();
        repository.TextChanged += (_, _) => { if (changing) return; InvalidatePlan(); catalog = null; masterList.Text = ""; conversionVehicles.Clear(); vehicles.Items.Clear(); };
        cancel.Click += (_, _) => operation?.Cancel();
        install.Click += async (_, _) => await Install();
        tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedTab == previewTab) SchedulePreview(); };
        previewDebounce.Tick += async (_, _) => { previewDebounce.Stop(); await UpdatePreview(); };
        preview.RefreshRequested += SchedulePreview;
        preview.PreviewLiveryRequested += BrowsePreviewLivery;
        preview.VehicleOnlyRequested += () =>
        {
            previewExisting = null;
            previewBaseVehicleKey = preview.DisplayedVehicleKey;
            previewVehicleOnly = true;
            SchedulePreview();
        };
        preview.LiverySelectionChanged += (existing, pending) =>
        {
            previewExisting = existing;
            previewBaseVehicleKey = pending ? "" : preview.DisplayedVehicleKey;
            previewVehicleOnly = !pending;
            SchedulePreview();
        };
        replaceExisting.CheckedChanged += (_, _) =>
        {
            if (updatingReplacement || Current is not ImportRow row || row != advancedRow) return;
            row.ReplaceExisting = replaceExisting.Checked;
            row.Completed = false;
            if (row.ReplaceExisting) { row.ConvertModkit = false; SelectReplacement(); }
            else { row.ReplacementKey = ""; AutoConfigure(row); }
            UpdateAdvanced(); rows.ResetBindings(); InvalidatePlan(); SchedulePreview();
        };
        replacements.SelectionChanged += () =>
        {
            if (updatingReplacement || Current is not ImportRow row || row != advancedRow || !row.ReplaceExisting) return;
            SelectReplacement(); UpdateAdvanced(); rows.ResetBindings(); InvalidatePlan(); SchedulePreview();
        };
        accessFile.TextChanged += (_, _) => InvalidatePlan();
        masterList.TextChanged += (_, _) => InvalidatePlan();
        convertModkit.CheckedChanged += (_, _) =>
        {
            if (updatingConversion) return;
            CatalogVehicle? vehicle = Current is ImportRow row ? Vehicle(row) : vehicles.SelectedItem as CatalogVehicle;
            if (vehicle == null) return;
            if (convertModkit.Checked) conversionVehicles.Add(vehicle.Key); else conversionVehicles.Remove(vehicle.Key);
            foreach (ImportRow queued in rows.Where(item => !item.Completed && !item.ReplaceExisting && item.Vehicle == vehicle.Key)) queued.ConvertModkit = convertModkit.Checked;
            rows.ResetBindings(); InvalidatePlan();
        };
        AllowDrop = true;
        queue.AllowDrop = true;
        foreach (Control control in new Control[] { this, queue, emptyQueue, dropContent })
        {
            control.AllowDrop = true;
            control.DragEnter += (_, e) => { if (!busy && e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
            control.DragDrop += (_, e) => { if (!busy && e.Data?.GetData(DataFormats.FileDrop) is string[] paths) AddImages(paths); };
        }
        FormClosing += (_, e) =>
        {
            if (busy) { operation?.Cancel(); e.Cancel = true; status.Text = "Cancelling safely. Close again after cancellation completes."; return; }
            previewOperation?.Cancel();
            previewDebounce.Dispose();
            plan?.Dispose();
            if (persistSettings) ToolSettingsStore.Save(ToolSettingsStore.Load() with { RepoRoot = repository.Text, ModkitMasterListPath = masterList.Text.Trim(), AccessGroup = sharedAccess.Text, Permission = sharedPermission.Text, FitImage = fit.Checked, CreateBackups = false, VehicleChoices = remembered });
        };
        LiveryTheme.Apply(this);
        header.Controls[0].Font = new Font("Segoe UI", 19f, FontStyle.Bold);
        dropContent.Controls[0].Font = new Font("Segoe UI", 22f, FontStyle.Bold);
        search.Font = new Font("Segoe UI", 11f);
        search.PlaceholderText = "Search vehicles…";
        search.Margin = new Padding(0, 0, 0, 12);
        vehicles.DrawMode = DrawMode.OwnerDrawFixed;
        vehicles.ItemHeight = 48;
        vehicles.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || vehicles.Items[e.Index] is not CatalogVehicle vehicle) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(selected ? LiveryTheme.Input : LiveryTheme.Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
            if (selected) using (var brush = new SolidBrush(LiveryTheme.Accent)) e.Graphics.FillRectangle(brush, e.Bounds.Left, e.Bounds.Top + 6, 3, e.Bounds.Height - 12);
            using var nameFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            using var detailFont = new Font("Segoe UI", 8.5f);
            TextRenderer.DrawText(e.Graphics, vehicle.Model, nameFont, new Rectangle(e.Bounds.Left + 12, e.Bounds.Top + 4, e.Bounds.Width - 20, 22), LiveryTheme.Text, TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, vehicle.DisplayName + " · " + vehicle.Resource, detailFont, new Rectangle(e.Bounds.Left + 12, e.Bounds.Top + 26, e.Bounds.Width - 20, 19), LiveryTheme.Muted, TextFormatFlags.EndEllipsis);
        };
        queue.Visible = false;
        Shown += async (_, _) => { if (Directory.Exists(Path.Combine(repository.Text, "resources"))) await Scan(); };
    }

    private static Button Button(string text, Action action)
    {
        var button = new LiveryButton { Text = text };
        button.Click += (_, _) => action();
        return button;
    }

    private static TabPage HelpPage()
    {
        var page = new TabPage("Help");
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(24) };
        void Section(string title, string text)
        {
            content.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI", 14f, FontStyle.Bold), Margin = new Padding(0, 14, 0, 8) });
            content.Controls.Add(new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 11f), Margin = new Padding(0, 0, 0, 14), MaximumSize = new Size(750, 0) });
        }
        Section("Start with a car", "Search by spawn name, display name or resource. Select a car and choose Preview selected car to view it without artwork. Game files lets you select your local GTA V Legacy installation for shared textures and base-game vehicles.");
        Section("Add artwork and preview", "Drop images into Create liveries, or choose Preview livery… directly in 3D Preview. That adds artwork for the car you are viewing and previews it from temporary staging. Select a queue row to switch liveries; Car only removes the artwork from the preview while keeping your queue. Orbit with the left mouse button, zoom with the wheel and pan with the middle mouse button. Scene settings contains lighting, backgrounds and paint controls.");
        Section("Browse or replace an existing livery", "The Livery dropdown in 3D Preview lists existing slots with names from Lua and YFT filenames. Choose a slot to see its current artwork, Car only to remove it, or Pending artwork to see the selected queue image. To replace artwork, select your queue row, open Advanced, enable Replace an existing livery (YFT only) and choose the target. Review export builds and verifies the replacement before overwriting that exact YFT. Lua names, access rules, slots, modkits and manifests stay unchanged. Game archive liveries can be previewed; replacement requires one unambiguous YFT inside the selected repository.");
        Section("Paint and scene settings", "Open Scene settings in 3D Preview for separate Paint, Lighting and Scene pages. Paint has independent primary and secondary colour pickers, finish menus and reset controls. A layer is disabled when the model does not declare it. Paint choices stay in the preview when switching liveries, and do not change artwork or exported YFTs. Matte, classic, metallic, satin and chrome are studio approximations; verify the final finish in FiveM. Lighting controls the studio lights, and Scene contains backgrounds, screenshot options and body-part visibility.");
        Section("YFT building is automatic", "Enter a livery name and access group (blank means public). Choose Review export. The tool selects a compatible existing VMT_LIVERY_MOD template, converts the image with mipmaps, builds a uniquely named native YFT and reloads it to verify the result. You do not need to convert an image or build a YFT separately. If artwork dimensions differ, enable Fit image under Advanced to preserve its proportions with transparent padding.");
        Section("Review, then export", "The tool finds the existing livery stream folder, the vehicle's exact modkit metadata and the Lua configuration for names and access. When a destination or Lua file cannot be identified uniquely, a selection window asks you to choose it. The folder button in each queue row and the Lua field in Advanced let you override these choices. Review lists every asset, metadata path, slot and access rule before Export liveries writes them together. Existing liveries are preserved. No files are written to your repository during preview or review.");
        Section("When Advanced is needed", "Use Advanced to select a modkit, compatible livery template or embedded texture when there are several valid choices. It also contains shared queue settings and the separate extraction, texture conversion and sign utilities. A vehicle with no compatible VMT_LIVERY_MOD template needs a template made for that vehicle first. Texture-index livery systems and conflicting resource definitions require additional setup; exporting arbitrary artwork cannot create a compatible mesh.");
        Section("A vehicle's first custom modkit", "In Advanced, enable Convert to a dedicated modkit and update the master list for the selected queue vehicle. Review export will create one dedicated kit, preserve the existing mods and slots, update that vehicle's carvariations reference and register the new ID in the master list. The original kit remains intact for other vehicles. Review modkit conversion only works without artwork or a livery template. The master list is found relative to your selected repository; use Choose master list if it lives elsewhere. Creating a modkit does not create a compatible livery mesh. Vehicles without repository modkit metadata still need the correct metadata extracted or supplied first.");
        Section("Escrowed models", "Encrypted FXAP assets cannot be decoded by this offline renderer, even just for viewing. Unencrypted files in an escrowed resource can still be previewed. For an encrypted vehicle, ask its creator for an unencrypted preview model and required textures, or inspect it in your entitled FiveM resource. The tool does not decrypt or bypass escrow protection.");
        Section("Before using it on the server", "This is an offline visual check. Paint is an approximation and some materials or dependencies cannot be reproduced. Test the exported vehicle, livery slots, names and access in FiveM before deployment. A folder outside a running resource's stream directory needs its exported assets copied into the resource first.");
        content.Resize += (_, _) => { foreach (Label item in content.Controls.OfType<Label>()) item.MaximumSize = new Size(Math.Max(250, content.ClientSize.Width - 65), 0); };
        page.Controls.Add(content);
        return page;
    }

    private static void ThemePanels(Control root)
    {
        foreach (Control control in root.Controls)
        {
            if (control is Panel or TabPage or TabControl) control.BackColor = BlrpTheme.Background;
            if (control is ListBox list) { list.BackColor = BlrpTheme.Input; list.ForeColor = Color.White; }
            ThemePanels(control);
        }
    }

    private void BrowseRepository()
    {
        if (busy) return;
        using var dialog = new FolderBrowserDialog { Description = "Select the BadlandsRP repository", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) repository.Text = dialog.SelectedPath;
    }

    private async Task Scan()
    {
        if (busy) return;
        await Run(async token =>
        {
            InvalidatePlan();
            string root = repository.Text;
            var progress = Progress();
            UseCatalog(await Task.Run(() => VehicleCatalog.Scan(root, token, progress), token));
            reviewText.Text = string.Join(Environment.NewLine, catalog!.Warnings);
            diagnostics.Text = reviewText.Text;
            status.Text = $"{catalog.Vehicles.Count(vehicle => vehicle.MetadataPaths.Length > 0):N0} vehicles • {catalog.Vehicles.Count(vehicle => vehicle.MetadataPaths.Length == 0):N0} unclassified models • build {catalog.GameBuild} • {catalog.Warnings.Length} warnings (Advanced)";
        });
    }

    private void UseCatalog(VehicleCatalog next)
    {
        catalog = next;
        changing = true;
        try
        {
            var column = (DataGridViewComboBoxColumn)queue.Columns["Vehicle"];
            column.DataSource = new[] { "" }.Concat(next.Vehicles.Select(vehicle => vehicle.Key)).ToArray();
            foreach (ImportRow row in rows)
            {
                string previousStatus = row.Status;
                if (!row.ReplaceExisting) RebindSelection(row);
                if (row.Completed) row.Status = previousStatus;
            }
            rows.ResetBindings();
        }
        finally { changing = false; }
        FilterVehicles();
        UpdateAdvanced();
        diagnostics.Text = string.Join(Environment.NewLine, next.Warnings);
    }

    private void RebindSelection(ImportRow row)
    {
        CatalogVehicle? vehicle = Vehicle(row);
        VehicleKit? kit = vehicle?.Kits.SingleOrDefault(kit => row.Kit != null &&
            kit.Name == row.Kit.Name && kit.Path.Equals(row.Kit.Path, StringComparison.OrdinalIgnoreCase));
        if (kit == null) { AutoConfigure(row); return; }
        bool changed = !ReferenceEquals(row.Kit, kit);
        row.Kit = kit;
        VehicleTemplate? template = row.Template;
        row.Template = template == null ? null : catalog!.Templates(kit).SingleOrDefault(candidate =>
            candidate.Path.Equals(template.Path, StringComparison.OrdinalIgnoreCase) && XNode.DeepEquals(candidate.Mod, template.Mod));
        if (template != null && row.Template == null) row.Texture = "";
        if (changed && row.AutomaticFolder) { row.ExportFolder = ""; row.AutomaticFolder = false; }
    }

    private void FilterVehicles()
    {
        string? selected = (vehicles.SelectedItem as CatalogVehicle)?.Key;
        vehicles.BeginUpdate();
        vehicles.Items.Clear();
        if (catalog != null) vehicles.Items.AddRange(catalog.Vehicles.Where(vehicle => (showUnclassified.Checked || vehicle.MetadataPaths.Length > 0) &&
            $"{vehicle.Model} {vehicle.DisplayName} {vehicle.Resource}".Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).Cast<object>().ToArray());
        vehicles.EndUpdate();
        if (selected != null) vehicles.SelectedItem = vehicles.Items.Cast<CatalogVehicle>().FirstOrDefault(vehicle => vehicle.Key == selected);
        if (vehicles.SelectedIndex < 0 && vehicles.Items.Count > 0) vehicles.SelectedIndex = 0;
    }

    private void BrowseImages()
    {
        using var dialog = new OpenFileDialog { Multiselect = true, Filter = "Livery images|*.png;*.jpg;*.jpeg;*.tga;*.bmp", Title = "Add liveries to the queue" };
        if (dialog.ShowDialog(this) == DialogResult.OK) AddImages(dialog.FileNames);
    }

    private bool ChooseFolder(ImportRow row)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = $"Export “{row.LiveryName}” — {Vehicle(row)?.Model ?? "vehicle"}",
            UseDescriptionForTitle = true,
            SelectedPath = row.ExportFolder.Length > 0 ? row.ExportFolder : lastExportFolder,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        row.ExportFolder = Path.GetFullPath(dialog.SelectedPath);
        row.AutomaticFolder = false;
        lastExportFolder = row.ExportFolder;
        row.Status = "Ready to review";
        InvalidatePlan();
        rows.ResetBindings();
        return true;
    }

    private bool ChooseAccessFile()
    {
        using var dialog = new OpenFileDialog { Filter = "Livery Lua configuration|*.lua", Title = "Choose the file containing custom_liveries and livery display names",
            InitialDirectory = Directory.Exists(Path.Combine(repository.Text, "resources")) ? Path.Combine(repository.Text, "resources") : repository.Text };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        accessFile.Text = dialog.FileName;
        return true;
    }

    private string PortablePath(string path)
    {
        string full = Path.GetFullPath(path);
        if (repository.Text.Length > 0)
        {
            string root = Path.GetFullPath(repository.Text).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return Path.GetRelativePath(repository.Text, full);
        }
        return full;
    }

    private bool ChooseMasterList()
    {
        using var dialog = new OpenFileDialog { Title = "Select the modkit master list", Filter = "Text files|*.txt|All files|*.*", InitialDirectory = repository.Text };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        masterList.Text = PortablePath(dialog.FileName);
        return true;
    }

    private Task<string> MasterListPath(VehicleCatalog snapshot, CancellationToken token)
    {
        string selected = masterList.Text.Trim();
        if (selected.Length == 0 && ModkitConversion.FindMasterLists(snapshot).Length != 1)
        {
            status.Text = "Select the master list to use for this repository.";
            if (!ChooseMasterList()) throw new OperationCanceledException("Master list selection cancelled.");
            selected = masterList.Text.Trim();
        }
        return Task.Run(() => ModkitConversion.ResolveMasterList(snapshot, selected), token);
    }

    private void BrowsePreviewLivery()
    {
        if (catalog == null || busy) return;
        using var dialog = new OpenFileDialog { Filter = "Livery images|*.png;*.jpg;*.jpeg;*.tga;*.bmp", Title = "Preview artwork on the current car" };
        if (dialog.ShowDialog(this) == DialogResult.OK) PreviewArtwork(dialog.FileName);
    }

    private void PreviewArtwork(string image)
    {
        CatalogVehicle? car = !previewVehicleOnly && Current is ImportRow current ? Vehicle(current) : catalog?.Vehicles.FirstOrDefault(v => v.Key == previewBaseVehicleKey) ?? vehicles.SelectedItem as CatalogVehicle;
        if (catalog == null || car == null) { status.Text = "Select a car before adding preview artwork."; return; }
        var row = new ImportRow { Image = image, Vehicle = car.Key, LiveryName = sharedName.Text.Trim().Length > 0 ? sharedName.Text.Trim() : Path.GetFileNameWithoutExtension(image),
            AccessGroup = sharedAccess.Text.Trim(), Permission = sharedPermission.Text.Trim(), FitImage = fit.Checked };
        AutoConfigure(row);
        rows.Add(row);
        queue.ClearSelection();
        queue.CurrentCell = queue.Rows[rows.Count - 1].Cells["LiveryName"];
        queue.Rows[rows.Count - 1].Selected = true;
        InvalidatePlan();
        previewVehicleOnly = false;
        tabs.SelectedIndex = 1;
        SchedulePreview();
    }

    private void AddImages(IEnumerable<string> paths)
    {
        if (catalog == null) { status.Text = "Scan vehicles before adding images."; return; }
        foreach (string path in paths)
        {
            if (!File.Exists(path) || !new[] { ".png", ".jpg", ".jpeg", ".tga", ".bmp" }.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;
            string filename = Path.GetFileNameWithoutExtension(path);
            CatalogVehicle[] matches = catalog.Vehicles.Where(vehicle => filename.Equals(vehicle.Model, StringComparison.OrdinalIgnoreCase) ||
                filename.StartsWith(vehicle.Model + "_", StringComparison.OrdinalIgnoreCase) || filename.StartsWith(vehicle.Model + "-", StringComparison.OrdinalIgnoreCase)).ToArray();
            CatalogVehicle? vehicle = matches.Length == 1 ? matches[0] : matches.Length == 0 ? vehicles.SelectedItem as CatalogVehicle : null;
            var row = new ImportRow { Image = path, Vehicle = vehicle?.Key ?? "", LiveryName = sharedName.Text.Trim().Length > 0 ? sharedName.Text.Trim() : filename,
                AccessGroup = sharedAccess.Text.Trim(), Permission = sharedPermission.Text.Trim(), FitImage = fit.Checked };
            AutoConfigure(row);
            if (matches.Length > 1) row.Status = "Ambiguous filename — choose vehicle";
            rows.Add(row);
        }
        InvalidatePlan();
        SchedulePreview();
    }

    private ImportRow? Current => queue.CurrentRow?.DataBoundItem as ImportRow;
    private IEnumerable<ImportRow> Selected => queue.SelectedRows.Cast<DataGridViewRow>().Select(row => (ImportRow)row.DataBoundItem);
    private CatalogVehicle? Vehicle(ImportRow row) => catalog?.Vehicles.SingleOrDefault(vehicle => vehicle.Key == row.Vehicle);

    private void AutoConfigure(ImportRow row)
    {
        row.ReplaceExisting = false; row.ReplacementKey = "";
        row.ConvertModkit = conversionVehicles.Contains(row.Vehicle);
        if (row.AutomaticFolder) { row.ExportFolder = ""; row.AutomaticFolder = false; }
        row.Kit = null; row.Template = null; row.Texture = "";
        CatalogVehicle? vehicle = Vehicle(row);
        if (vehicle == null) { row.Status = "Choose vehicle"; return; }
        string key = catalog!.Root + "|" + vehicle.Key;
        if (remembered.TryGetValue(key, out VehicleChoice? choice))
        {
            row.Kit = vehicle.Kits.SingleOrDefault(kit => kit.Path == choice.KitPath && kit.Name == choice.KitName);
            row.Template = row.Kit == null ? null : catalog.Templates(row.Kit).SingleOrDefault(template => template.Path == choice.TemplatePath);
            if (row.Template != null) { row.Texture = choice.Texture; row.Status = "Remembered settings • review required"; return; }
        }
        if (vehicle.Problems.Length > 0 || vehicle.Kits.Length != 1) { row.Status = vehicle.Status; return; }
        row.Kit = vehicle.Kits[0];
        VehicleTemplate[] compatible = catalog!.Templates(row.Kit);
        // Different livery meshes are not interchangeable merely because they share a vehicle.
        if (compatible.Length == 0) { row.Status = "Needs compatible streamed VMT_LIVERY_MOD template"; return; }
        row.Status = "Checking template";
        // Inspecting YFTs runs only on demand, away from the UI thread.
        row.Template = compatible.Length == 1 ? compatible[0] : null;
        row.Status = row.Template == null ? "Choose template in Advanced" : "Ready for review";
    }

    private void ApplyShared(bool all)
    {
        queue.EndEdit();
        foreach (ImportRow row in all ? rows.AsEnumerable() : Selected.ToArray())
        {
            if (sharedName.Text.Trim().Length > 0) row.LiveryName = sharedName.Text.Trim();
            row.AccessGroup = sharedAccess.Text.Trim(); row.Permission = sharedPermission.Text.Trim(); row.FitImage = fit.Checked; row.Status = "Needs review";
        }
        rows.ResetBindings(); InvalidatePlan(); SchedulePreview();
    }

    private void AssignSelectedVehicle()
    {
        if (vehicles.SelectedItem is not CatalogVehicle vehicle) return;
        foreach (ImportRow row in Selected.ToArray()) { row.Vehicle = vehicle.Key; AutoConfigure(row); }
        rows.ResetBindings(); UpdateAdvanced(); InvalidatePlan(); SchedulePreview();
    }

    private void RemoveSelected()
    {
        foreach (ImportRow row in Selected.ToArray()) rows.Remove(row);
        InvalidatePlan(); SchedulePreview();
    }

    private void ResetPreviewSelection() { previewExisting = null; previewBaseVehicleKey = ""; }

    private void SelectReplacement()
    {
        if (Current is not ImportRow row || replacements.SelectedChoice?.Existing is not ExistingLivery selected || selected.VehicleKey != row.Vehicle) return;
        row.ReplacementKey = selected.Key;
        if (!selected.CanReplace) { row.Texture = ""; row.Completed = false; row.Status = "Choose a replaceable repository YFT"; return; }
        row.Kit = selected.Kit;
        row.Template = new VehicleTemplate(selected.Paths.Single(), selected.Mod);
        row.Texture = ""; row.ConvertModkit = false; row.Completed = false;
        row.Status = "Replacement · needs review";
    }

    private async void LoadReplacements(ImportRow? row, CatalogVehicle? vehicle)
    {
        int generation = ++replacementGeneration;
        VehicleCatalog? snapshot = catalog;
        try
        {
            if (snapshot != replacementCatalog || vehicle?.Key != replacementVehicleKey || replacementLoad == null)
            {
                replacementCatalog = snapshot; replacementVehicleKey = vehicle?.Key ?? "";
                replacements.Placeholder = "Loading livery slots…";
                replacements.SetChoices([], null);
                replacementLoad = snapshot == null || vehicle == null ? Task.FromResult(Array.Empty<ExistingLivery>()) : Task.Run(() => ExistingLiveries.Load(snapshot, vehicle));
            }
            var choices = await replacementLoad;
            if (IsDisposed || generation != replacementGeneration || row != Current || catalog != snapshot) return;
            updatingReplacement = true;
            try
            {
                replacements.Placeholder = choices.Length == 0 ? "No livery slots found" : "Choose the livery to replace…";
                replacements.Context = (vehicle?.Model ?? "Vehicle") + " · replace artwork";
                replacements.SetChoices(choices.Select(l => LiveryChoice.From(l, replacing: true)), row?.ReplacementKey);
                ExistingLivery? selected = replacements.SelectedChoice?.Existing;
                replacementHint.Text = selected?.CanReplace == false ? selected.ReplacementUnavailableReason :
                    $"{choices.Count(l => l.CanReplace)} of {choices.Length} slots have a replaceable repository YFT. Game-only and duplicate slots stay visible with their availability. Replacement preserves Lua, slots and access.";
            }
            finally { updatingReplacement = false; }
        }
        catch (Exception ex) { if (!IsDisposed && generation == replacementGeneration) status.Text = ex.Message; }
    }

    private void UpdateAdvanced()
    {
        advancedRow = Current;
        CatalogVehicle? target = advancedRow is ImportRow selected ? Vehicle(selected) : vehicles.SelectedItem as CatalogVehicle;
        replacementTarget.Text = advancedRow != null ? $"Replace artwork for: {target?.Model ?? "choose a vehicle"}  ·  {Path.GetFileName(advancedRow.Image)} (selected queue row)" : "Select a queue row or add artwork to replace a livery.";
        conversionTarget.Text = "Modkit setup for: " + (target?.Model ?? "select a vehicle");
        updatingConversion = true;
        try { convertModkit.Checked = advancedRow?.ConvertModkit ?? (target != null && conversionVehicles.Contains(target.Key)); }
        finally { updatingConversion = false; }
        convertModkit.Enabled = target != null && advancedRow?.ReplaceExisting != true;
        updatingReplacement = true;
        try { replaceExisting.Checked = advancedRow?.ReplaceExisting == true; }
        finally { updatingReplacement = false; }
        replaceExisting.Enabled = advancedRow != null && target != null;
        replacements.Enabled = replaceExisting.Checked;
        kits.Enabled = templates.Enabled = outputName.Enabled = label.Enabled = !replaceExisting.Checked;
        LoadReplacements(advancedRow, target);
        if (advancedRow is not ImportRow row) { kits.DataSource = target?.Kits; templates.DataSource = null; textures.DataSource = null; return; }
        kits.DataSource = Vehicle(row)?.Kits;
        if (row.Kit != null) kits.SelectedItem = row.Kit;
        LoadTemplates();
        if (row.Template != null) templates.SelectedItem = ((VehicleTemplate[]?)templates.DataSource)?.FirstOrDefault(template => template.Path == row.Template.Path);
        outputName.Text = row.OutputName; label.Text = row.Label;
    }

    private void LoadTemplates()
    {
        templates.DataSource = catalog != null && kits.SelectedItem is VehicleKit kit ? catalog.Templates(kit) : null;
        LoadTextures();
    }

    private async void LoadTextures()
    {
        textures.DataSource = null;
        if (templates.SelectedItem is not VehicleTemplate template) return;
        try
        {
            var result = await Task.Run(() => (LiveryAssetBuilder.Textures(template.Path), LiveryAssetBuilder.SuggestTexture(template.Path)));
            if (!Equals(templates.SelectedItem, template) || IsDisposed) return;
            textures.DataSource = result.Item1;
            string selected = Current?.Texture is { Length: > 0 } name ? name : result.Item2 ?? "";
            textures.SelectedItem = result.Item1.SingleOrDefault(texture => texture.Name == selected);
            if (selected.Length == 0) textures.SelectedIndex = -1;
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    private void ApplyOverrides()
    {
        if (Current is not ImportRow row) return;
        if (row.ReplaceExisting)
        {
            row.Texture = (textures.SelectedItem as LiveryTexture)?.Name ?? "";
            row.Completed = false; row.Status = "Replacement · needs review";
            InvalidatePlan(); rows.ResetBindings(); SchedulePreview(); return;
        }
        CatalogVehicle? vehicle = Vehicle(row);
        if (!ReferenceEquals(row, advancedRow) || vehicle == null || kits.SelectedItem is not VehicleKit kit || !vehicle.Kits.Contains(kit))
        {
            UpdateAdvanced();
            status.Text = "The selected row changed. Review its Advanced settings and apply again.";
            return;
        }
        VehicleTemplate? template = templates.SelectedItem as VehicleTemplate;
        if (template != null && !catalog!.Templates(kit).Any(candidate => candidate.Path == template.Path && XNode.DeepEquals(candidate.Mod, template.Mod)))
        {
            UpdateAdvanced();
            status.Text = "Choose a template belonging to the current vehicle's modkit.";
            return;
        }
        row.Kit = kit; row.Template = template;
        row.Texture = (textures.SelectedItem as LiveryTexture)?.Name ?? "";
        if (!row.ReplaceExisting) { row.OutputName = outputName.Text.Trim(); row.Label = label.Text.Trim(); }
        row.Completed = false; row.Status = "Needs review";
        if (!row.ReplaceExisting && row.Kit != null && row.Template != null && catalog != null)
            remembered[catalog.Root + "|" + row.Vehicle] = new VehicleChoice(row.Kit.Path, row.Kit.Name, row.Template.Path, row.Texture);
        rows.ResetBindings(); InvalidatePlan(); SchedulePreview();
    }

    private async Task<LiveryImport> Request(ImportRow row)
    {
        if (!row.ReplaceExisting) RebindSelection(row);
        VehicleCatalog snapshot = catalog ?? throw new InvalidOperationException("Scan vehicles before previewing or reviewing.");
        CatalogVehicle vehicle = Vehicle(row) ?? throw new InvalidDataException("Choose a vehicle for " + Path.GetFileName(row.Image));
        if (row.ReplaceExisting)
        {
            string key = row.ReplacementKey;
            string replacementTexture = row.Texture;
            ExistingLivery[] choices = await Task.Run(() => ExistingLiveries.Load(snapshot, vehicle));
            ExistingLivery selected = choices.SingleOrDefault(l => l.Key == key) ?? throw new InvalidDataException("Choose an existing repository livery to replace in Advanced.");
            if (!selected.CanReplace) throw new InvalidDataException(selected.Model + ".yft: " + selected.ReplacementUnavailableReason);
            string textureName = replacementTexture.Length > 0 ? replacementTexture : await Task.Run(() => LiveryAssetBuilder.SuggestTexture(selected.Paths.Single())) ?? "";
            if (catalog != snapshot || row.Vehicle != vehicle.Key || !row.ReplaceExisting || row.ReplacementKey != key || row.Texture != replacementTexture || !rows.Contains(row))
                throw new OperationCanceledException("Replacement selection changed during preparation.");
            if (textureName.Length == 0) throw new InvalidDataException("Choose the embedded livery texture in Advanced.");
            row.Kit = selected.Kit; row.Template = new VehicleTemplate(selected.Paths.Single(), selected.Mod); row.Texture = textureName;
            return new LiveryImport(vehicle, selected.Kit, row.Template, textureName, row.Image, selected.DisplayName, "", "", row.FitImage, Replacement: selected);
        }
        if (row.Kit == null) throw new InvalidDataException(vehicle.Status + ". Choose a modkit in Advanced.");
        VehicleKit kit = row.Kit;
        VehicleTemplate? selectedTemplate = row.Template;
        VehicleTemplate? template = selectedTemplate;
        string selectedTexture = row.Texture;
        if (template == null)
        {
            // Automatically reuse equivalent templates only when their mesh/shader structure is identical.
            template = await Task.Run(() => TemplateCompatibility.Choose(snapshot.Templates(kit)));
        }
        if (template == null) throw new InvalidDataException("Choose a compatible template in Advanced for " + vehicle.Model);
        string texture = selectedTexture.Length > 0 ? selectedTexture : await Task.Run(() => LiveryAssetBuilder.SuggestTexture(template.Path)) ?? "";
        if (!ReferenceEquals(catalog, snapshot) || row.Vehicle != vehicle.Key || !ReferenceEquals(row.Kit, kit) ||
            !ReferenceEquals(row.Template, selectedTemplate) || row.Texture != selectedTexture || !rows.Contains(row))
            throw new OperationCanceledException("Vehicle selection changed during preview preparation.");
        if (texture.Length == 0) throw new InvalidDataException("Choose the embedded livery texture in Advanced for " + vehicle.Model);
        row.Template = template; row.Texture = texture;
        return new LiveryImport(vehicle, kit, template, texture, row.Image, row.LiveryName.Trim(), row.AccessGroup.Trim(), row.Permission.Trim(), row.FitImage,
            row.OutputName, row.Label, row.ExportFolder, row.ConvertModkit);
    }

    private async Task Review()
    {
        if (catalog == null || busy) return;
        queue.EndEdit();
        ImportRow[] pending = rows.Where(row => !row.Completed).ToArray();
        if (pending.Length == 0) { status.Text = "Add artwork to start a new export."; return; }
        await Run(async token =>
        {
            InvalidatePlan();
            string root = repository.Text;
            var scanProgress = Progress();
            UseCatalog(await Task.Run(() => VehicleCatalog.Scan(root, token, scanProgress), token));
            VehicleCatalog snapshot = catalog!;
            var requests = new List<LiveryImport>();
            foreach (ImportRow row in pending)
            {
                LiveryImport request;
                try { request = await Request(row); row.Status = "Preparing"; }
                catch (Exception ex) { row.Status = ex.Message; rows.ResetBindings(); throw; }
                if (request.Replacement == null && row.ExportFolder.Length == 0)
                {
                    string? folder = LiveryRouting.ExportFolder(snapshot, request.Kit);
                    if (folder != null) { row.ExportFolder = folder; row.AutomaticFolder = true; }
                    else if (!ChooseFolder(row)) throw new OperationCanceledException("Export folder selection cancelled.");
                }
                requests.Add(request.Replacement != null ? request : request with { ExportFolder = row.ExportFolder });
            }
            string lua = accessFile.Text.Trim();
            if (requests.Any(r => r.Replacement == null) && lua.Length == 0)
            {
                string[] matches = await Task.Run(() => LiveryRouting.AccessFiles(snapshot, token), token);
                if (matches.Length == 1) lua = matches[0];
                else
                {
                    status.Text = matches.Length == 0 ? "Select the Lua file containing livery access and names." : "Several livery Lua configurations found. Select the active file.";
                    if (!ChooseAccessFile()) throw new OperationCanceledException("Livery Lua selection cancelled.");
                    lua = accessFile.Text.Trim();
                }
            }
            var progress = Progress();
            string? master = requests.Any(request => request.ConvertModkit) ? await MasterListPath(snapshot, token) : null;
            plan = await Task.Run(() => workflow.Prepare(snapshot, requests, token, progress, lua, master), token);
            plannedRows = pending;
            for (int i = 0; i < pending.Length; i++) pending[i].Status = $"Ready · slot {plan.Items[i].Slot}";
            rows.ResetBindings();
            reviewText.Text = $"{pending.Length} liveries ready to export\r\nYFTs built and verified. Replacements update only their existing YFT; new liveries include names and access.\r\n\r\n" + plan.Review() +
                "\r\n\r\nFor folders outside a running resource's stream directory, copy the exported YFTs into that resource before testing in FiveM.";
            install.Enabled = true;
            install.Text = "Export liveries";
            tabs.SelectedIndex = 2;
            status.Text = "Review the destinations, names and access, then export.";
        });
    }

    private async Task ReviewModkit()
    {
        if (busy || catalog == null) return;
        if (!convertModkit.Checked) { status.Text = "Enable modkit conversion in Advanced first."; return; }
        CatalogVehicle? target = Current is ImportRow row ? Vehicle(row) : vehicles.SelectedItem as CatalogVehicle;
        if (target == null) { status.Text = "Select a vehicle first."; return; }
        string key = target.Key;
        VehicleKit? selected = kits.SelectedItem as VehicleKit;
        await Run(async token =>
        {
            InvalidatePlan();
            string root = repository.Text;
            var progress = Progress();
            UseCatalog(await Task.Run(() => VehicleCatalog.Scan(root, token, progress), token));
            VehicleCatalog snapshot = catalog!;
            CatalogVehicle vehicle = snapshot.Vehicles.Single(item => item.Key == key);
            VehicleKit? kit = vehicle.Kits.SingleOrDefault(item => selected != null && item.Path == selected.Path && item.Name == selected.Name);
            kit ??= vehicle.Kits.Length == 1 ? vehicle.Kits[0] : null;
            if (kit == null) throw new InvalidDataException("Select this vehicle's modkit in Advanced. If it has no repository modkit metadata, extract or supply that metadata first.");
            string master = await MasterListPath(snapshot, token);
            plan = await Task.Run(() => workflow.PrepareModkits(snapshot, [new(vehicle, kit)], master, token), token);
            plannedRows = [];
            reviewText.Text = "Modkit conversion only — no livery assets will be exported.\r\nThe original kit is preserved.\r\n\r\n" + plan.Review();
            install.Enabled = plan.StagedFiles.Count > 0;
            install.Text = "Export modkit";
            tabs.SelectedIndex = 2;
            status.Text = install.Enabled ? "Review the kit ID, vehicle reference and master list, then export." : "This modkit is already registered. No changes are needed.";
        });
    }

    private async Task Install()
    {
        if (plan == null || busy) return;
        await Run(async token =>
        {
            install.Enabled = false;
            var progress = Progress();
            await Task.Run(() => workflow.Install(plan, token, progress), token);
            foreach (ImportRow row in plannedRows) { row.Status = row.ReplaceExisting ? "Replaced · YFT only" : "Exported · metadata updated"; row.Completed = true; }
            rows.ResetBindings();
            string exported = plan.Items.Count > 0 ? $"{plan.Items.Count} {(plan.Items.Count == 1 ? "livery" : "liveries")}" : "";
            if (plan.Modkits.Count > 0) exported += (exported.Length > 0 ? " and " : "") + $"{plan.Modkits.Count} {(plan.Modkits.Count == 1 ? "modkit" : "modkits")}";
            status.Text = $"Exported {exported}. Ready for FiveM testing.";
            plan.Dispose(); plan = null;
        });
    }

    private void InvalidatePlan()
    {
        install.Enabled = false;
        plan?.Dispose(); plan = null;
        plannedRows = [];
        reviewText.Text = "Review your queue to build the YFTs and find destinations automatically.";
        previewOperation?.Cancel();
    }

    private IProgress<string> Progress() => new Progress<string>(text => { if (!IsDisposed) status.Text = text; });

    private async Task Run(Func<CancellationToken, Task> action)
    {
        busy = true;
        operation = new CancellationTokenSource();
        previewOperation?.Cancel();
        tabs.Enabled = false; vehicles.Enabled = false; repository.Parent!.Enabled = false;
        cancel.Enabled = true; cancel.Visible = true; progressBar.Visible = true;
        try { await action(operation.Token); }
        catch (OperationCanceledException) { status.Text = "Cancelled. No batch changes retained."; InvalidatePlan(); }
        catch (Exception ex)
        {
            status.Text = ex.Message;
            InvalidatePlan();
            reviewText.Text = "Operation stopped: " + ex.Message;
            foreach (ImportRow row in rows.Where(row => row.Status == "Preparing")) row.Status = "Not installed: " + ex.Message;
            rows.ResetBindings();
            MessageBox.Show(this, ex.Message, "Livery operation stopped", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            cancel.Enabled = false; cancel.Visible = false; progressBar.Visible = false;
            tabs.Enabled = true; vehicles.Enabled = true; repository.Parent!.Enabled = true;
            operation.Dispose(); operation = null; busy = false;
        }
    }

    private void SchedulePreview()
    {
        previewOperation?.Cancel();
        previewDebounce.Stop();
        if (tabs.SelectedIndex == 1) previewDebounce.Start();
    }

    private async Task UpdatePreview()
    {
        if (catalog == null || busy) return;
        VehicleCatalog snapshot = catalog;
        ImportRow? row = Current;
        CatalogVehicle? selected = snapshot.Vehicles.FirstOrDefault(v => v.Key == previewBaseVehicleKey) ?? vehicles.SelectedItem as CatalogVehicle;
        if ((row == null || previewVehicleOnly) && selected == null) { preview.Clear("Select a car or a queue row to preview."); return; }
        previewOperation?.Cancel();
        var source = new CancellationTokenSource(); previewOperation = source;
        try
        {
            LiveryImport? request = row != null && !previewVehicleOnly ? await Request(row) : null;
            CatalogVehicle vehicle = request?.Vehicle ?? selected!;
            ExistingLivery[] existing = await Task.Run(() => ExistingLiveries.Load(snapshot, vehicle, source.Token), source.Token);
            source.Token.ThrowIfCancellationRequested();
            ExistingLivery? choice = previewExisting == null ? null : existing.SingleOrDefault(l => l.Key == previewExisting.Key)
                ?? throw new InvalidDataException("The selected existing livery changed. Choose it again.");
            preview.SetExistingLiveries(vehicle, existing, row?.Vehicle == vehicle.Key, request != null, choice?.Key);
            await preview.LoadVehicle(snapshot, vehicle, request, source.Token, choice);
            if (!source.IsCancellationRequested && request != null && row != null && !row.Completed && plan == null)
            {
                row.Status = "Preview ready · review to export";
                queue.Invalidate();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!source.IsCancellationRequested) preview.Clear(ex.Message); }
        finally { if (previewOperation == source) previewOperation = null; source.Dispose(); }
    }

    private void OpenUtilities()
    {
        if (busy) return;
        using var utilities = new MainForm();
        utilities.ShowDialog(this);
    }
}

internal static class TemplateCompatibility
{
    public static VehicleTemplate? Choose(VehicleTemplate[] templates)
    {
        if (templates.Length == 0) return null;
        string? signature = null;
        foreach (VehicleTemplate template in templates)
        {
            var yft = new CodeWalker.GameFiles.YftFile();
            yft.Load(NativeAssetReader.Read(template.Path));
            var xml = System.Xml.Linq.XDocument.Parse(CodeWalker.GameFiles.YftXml.GetXml(yft));
            string? texture = LiveryAssetBuilder.SuggestTexture(template.Path);
            if (texture == null) return null;
            // Ignore only asset identities and DDS payload names; retain UVs, geometry, dimensions and shaders.
            // Asset identities and livery texture identities are separate: cara2_liv11, for example,
            // contains a texture called livery11, while later templates name both alike.
            foreach (var name in xml.Descendants("TextureDictionary").Elements("Item").Elements("Name")
                .Concat(xml.Descendants("Parameters").Elements("Item").Where(item => (string?)item.Attribute("type") == "Texture").Elements("Name")))
                if (name.Value == texture) name.Value = "__livery__";
            LiveryAssetIdentity.Rename(xml, Path.GetFileNameWithoutExtension(template.Path), "__asset__");
            foreach (var filename in xml.Descendants("TextureDictionary").Elements("Item").Elements("FileName"))
                if (filename.Value == texture + ".dds") filename.Value = "__livery__.dds";
            string current = xml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
            if (signature != null && signature != current) return null;
            signature = current;
        }
        return templates[0];
    }
}
