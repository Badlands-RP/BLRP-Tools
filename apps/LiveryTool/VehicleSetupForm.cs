namespace Badlands.LiveryTool;

internal sealed class VehicleSetupForm : Form
{
    private readonly VehicleCatalog initial;
    private readonly CatalogVehicle selected;
    private readonly ComboBox carcols = new() { Width = 760, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox variations = new() { Width = 760, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox master = new() { Width = 650, PlaceholderText = "Automatic: modkit master list in this repository" };
    private readonly TextBox gta = new() { Width = 620, PlaceholderText = "GTA V Legacy folder" };
    private readonly TextBox dlc = new() { Width = 190, PlaceholderText = "DLC matching the server build" };
    private readonly CheckBox dedicated = new() { AutoSize = true, Checked = true, Text = "Use a dedicated custom modkit (keep an existing registered kit)" };
    private readonly TextBox review = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 44 };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Bottom, Height = 4, Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly Button inspect = new LiveryButton { Text = "Review setup", Primary = true };
    private readonly Button apply = new LiveryButton { Text = "Apply setup", Enabled = false };
    private readonly Button cancel = new LiveryButton { Text = "Cancel", Visible = false };
    private readonly FlowLayoutPanel options = new() { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };
    private CancellationTokenSource? operation;
    private VehicleSetupResult? pending;
    public bool Applied { get; private set; }
    public string GtaFolder => gta.Text;
    public string DlcLevel => dlc.Text.Trim();

    public VehicleSetupForm(VehicleCatalog catalog, CatalogVehicle vehicle, string gtaFolder, string dlcLevel, string masterList)
    {
        initial = catalog; selected = vehicle;
        Text = "Set up vehicle — " + vehicle.Model;
        Size = new Size(1040, 850); MinimumSize = new Size(900, 730); StartPosition = FormStartPosition.CenterParent;
        options.Controls.Add(new Label { Text = "Set up " + vehicle.Model + " for custom liveries", AutoSize = true, Font = new Font("Segoe UI", 17, FontStyle.Bold) });
        options.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(930, 0), Text = "One review for carcols, carvariations and the modkit. Identical duplicates can be removed safely; differing definitions are reported. Stock liveries remain in GTA and are used directly as templates." });
        options.Controls.Add(dedicated);
        var gameRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        gta.Text = gtaFolder; dlc.Text = dlcLevel;
        var browse = new LiveryButton { Text = "GTA folder…" };
        browse.Click += (_, _) => { using var picker = new FolderBrowserDialog { Description = "Choose GTA V Legacy" }; if (picker.ShowDialog(this) == DialogResult.OK) gta.Text = picker.SelectedPath; };
        gameRow.Controls.AddRange([gta, browse, dlc]); options.Controls.Add(gameRow);
        var showLocations = new CheckBox { AutoSize = true, Text = "Show file locations / change automatic choices" };
        options.Controls.Add(showLocations);
        var locations = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false };
        locations.Controls.AddRange([new Label { Text = "Existing carcols file", AutoSize = true }, carcols,
            new Label { Text = "Existing carvariations file", AutoSize = true }, variations,
            new Label { Text = "Modkit master list", AutoSize = true }, master]);
        var chooseMaster = new LiveryButton { Text = "Choose master list…" };
        chooseMaster.Click += (_, _) => { using var picker = new OpenFileDialog { Filter = "Text files|*.txt|All files|*.*", InitialDirectory = catalog.Root }; if (picker.ShowDialog(this) == DialogResult.OK) master.Text = picker.FileName; };
        locations.Controls.Add(chooseMaster); options.Controls.Add(locations);
        showLocations.CheckedChanged += (_, _) => locations.Visible = showLocations.Checked;
        var targets = VehicleSetupWorkflow.Targets(catalog, vehicle);
        // Unbound choices keep an unresolved destination blank; a CurrencyManager
        // would silently select its first item when the dialog is shown.
        carcols.Items.AddRange(targets.Carcols); variations.Items.AddRange(targets.Variations);
        string? linkedCols = vehicle.Kits.Select(kit => kit.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 ? vehicle.Kits.First().Path : null;
        string? linkedVars = vehicle.MetadataPaths.FirstOrDefault(path => targets.Variations.Contains(path));
        carcols.SelectedItem = VehicleSetupWorkflow.Suggest(targets.Carcols, linkedCols, linkedVars) ?? VehicleSetupWorkflow.SuggestRegisteredCarcols(catalog, targets.Carcols, masterList);
        variations.SelectedItem = VehicleSetupWorkflow.Suggest(targets.Variations, linkedVars, carcols.SelectedItem as string);
        if (carcols.SelectedItem == null) carcols.SelectedItem = VehicleSetupWorkflow.Suggest(targets.Carcols, sibling: variations.SelectedItem as string);
        foreach (var combo in new[] { carcols, variations })
        {
            combo.FormattingEnabled = true;
            combo.Format += (_, e) => { if (e.ListItem is string path) e.Value = Path.GetRelativePath(catalog.Root, path); };
            combo.SelectedIndexChanged += (_, _) => InvalidateReview();
        }
        master.Text = masterList;
        if (master.Text.Length == 0 && ModkitConversion.FindMasterLists(catalog) is [string found]) master.Text = Path.GetRelativePath(catalog.Root, found);
        if (carcols.SelectedItem == null || variations.SelectedItem == null) showLocations.Checked = true;
        foreach (var field in new[] { gta, dlc, master }) field.TextChanged += (_, _) => InvalidateReview();
        dedicated.CheckedChanged += (_, _) => InvalidateReview();
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 57, Padding = new Padding(12), WrapContents = false };
        footer.Controls.AddRange([inspect, apply, cancel]);
        inspect.Click += async (_, _) => await ReviewSetup();
        apply.Click += async (_, _) => await ApplySetup();
        cancel.Click += (_, _) => operation?.Cancel();
        Controls.Add(review); Controls.Add(options); Controls.Add(footer); Controls.Add(status); Controls.Add(progress);
        review.Text = "Choose Review setup to inspect the vehicle and proposed changes. No files are changed until you choose Apply setup.";
        FormClosing += (_, e) => { if (operation != null) { operation.Cancel(); e.Cancel = true; } };
        FormClosed += (_, _) => pending?.Dispose();
        LiveryTheme.Apply(this);
    }

    private void InvalidateReview() { pending?.Dispose(); pending = null; apply.Enabled = false; }

    private async Task ReviewSetup()
    {
        if (operation != null) return;
        InvalidateReview();
        if (carcols.SelectedItem is not string cols || variations.SelectedItem is not string vars) { status.Text = "Choose the existing metadata files under Show file locations."; return; }
        string folder = gta.Text, level = dlc.Text.Trim(), registry = master.Text.Trim(); bool convert = dedicated.Checked;
        await Run(async token =>
        {
            var catalog = await Task.Run(() => VehicleCatalog.Scan(initial.Root, token), token);
            CatalogVehicle vehicle = catalog.Vehicles.Single(item => item.Key == selected.Key);
            VehicleMetadataSourceResult? source = null;
            if (vehicle.Kits.Length == 0 || vehicle.Problems.Contains("Missing carvariations"))
                source = await VehiclePreviewAssets.ReadGame(folder, level, token, new Progress<string>(message => status.Text = message), game => VehicleMetadataFinder.FindActive(game, vehicle.Model, token));
            pending = await Task.Run(() => VehicleSetupWorkflow.Prepare(catalog, vehicle, cols, vars, registry, source, convert, token), token);
            review.Text = pending.Review;
            apply.Enabled = pending.Plan.StagedFiles.Count > 0;
            status.Text = apply.Enabled ? "Review these changes, then Apply setup. Your queued artwork is preserved." : "Metadata is ready. Close this window and preview or review your livery; stock templates load from GTA automatically.";
        });
    }

    private async Task ApplySetup()
    {
        if (pending == null || operation != null) return;
        await Run(async token => { await Task.Run(() => new LiveryBatchWorkflow().Install(pending.Plan, token), token); Applied = true; });
        if (Applied) { DialogResult = DialogResult.OK; Close(); }
    }

    private async Task Run(Func<CancellationToken, Task> action)
    {
        operation = new(); options.Enabled = inspect.Enabled = apply.Enabled = false; cancel.Visible = progress.Visible = true;
        try { await action(operation.Token); }
        catch (OperationCanceledException) { InvalidateReview(); status.Text = "Setup cancelled. No changes retained."; }
        catch (Exception ex) { InvalidateReview(); status.Text = ex.Message; review.Text = "Setup needs attention:\r\n\r\n" + ex.Message; }
        finally { operation.Dispose(); operation = null; options.Enabled = inspect.Enabled = true; cancel.Visible = progress.Visible = false; }
    }
}
