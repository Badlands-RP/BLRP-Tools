namespace Badlands.LiveryTool;

// Kept separate from the install workflow: archive extraction and standalone asset utilities.
internal sealed class MainForm : Form
{
    private readonly TextBox gta = new() { Width = 420, Text = ToolSettingsStore.Load().GtaFolder };
    private readonly TextBox model = new() { Width = 180, PlaceholderText = "Vehicle spawn name" };
    private readonly ComboBox sources = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox metadata = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 32 };
    private CancellationTokenSource? search;

    public MainForm()
    {
        Text = "Livery Tool — Advanced utilities";
        Size = new Size(1000, 700);
        var tabs = new LiveryTabs { Dock = DockStyle.Fill };
        var metadataTab = new TabPage("GTA metadata extraction");
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 95 };
        void Button(FlowLayoutPanel panel, string text, Action action)
        {
            var button = new LiveryButton { Text = text };
            button.Click += (_, _) => action(); panel.Controls.Add(button);
        }
        actions.Controls.AddRange([gta, model]);
        Button(actions, "GTA folder…", () =>
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog(this) == DialogResult.OK) gta.Text = dialog.SelectedPath;
        });
        Button(actions, "Find metadata", async () => await Find());
        Button(actions, "Cancel search", () => search?.Cancel());
        Button(actions, "Copy source", () => { if (metadata.Text.Length > 0) Clipboard.SetText(metadata.Text); });
        foreach (string kind in new[] { "vehicles.meta", "carvariations.meta", "carcols.meta" }) Button(actions, "Insert " + kind + "…", () => Insert(kind));
        sources.SelectedIndexChanged += (_, _) => metadata.Text = sources.SelectedItem is VehicleMetadataSourceResult source ? VehicleMetadataFinder.Format(source) : "";
        metadataTab.Controls.Add(metadata); metadataTab.Controls.Add(sources); metadataTab.Controls.Add(actions);
        var conversionTab = new TabPage("Standalone texture conversion");
        var conversion = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        conversion.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(800, 0), Text = "Convert an image to BC3 / DXT5 with mipmaps without installing a livery. Queue imports convert automatically. Use Fit image in the queue for proportion-preserving resizing." });
        Button(conversion, "Image → DDS…", async () =>
        {
            using var input = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.tga;*.bmp" };
            if (input.ShowDialog(this) != DialogResult.OK) return;
            using var output = new SaveFileDialog { Filter = "DDS|*.dds", FileName = Path.GetFileNameWithoutExtension(input.FileName) + ".dds" };
            if (output.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                status.Text = "Converting…";
                await Task.Run(() => new LiveryImageConverter().ConvertToDxt5Dds(input.FileName, output.FileName));
                status.Text = "Converted: " + output.FileName;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Conversion stopped"); }
        });
        Button(conversion, "Sign batch tool…", () => { using var form = new SignBatchBuilderForm(); form.ShowDialog(this); });
        conversionTab.Controls.Add(conversion);
        tabs.TabPages.AddRange([metadataTab, conversionTab]);
        Controls.Add(tabs); Controls.Add(status);
        FormClosing += (_, e) =>
        {
            if (search != null) { search.Cancel(); e.Cancel = true; status.Text = "Cancelling archive search; close again when it stops."; return; }
            ToolSettingsStore.Save(ToolSettingsStore.Load() with { GtaFolder = gta.Text, CreateBackups = false });
        };
        LiveryTheme.Apply(this);
    }

    private async Task Find()
    {
        if (search != null) return;
        search = new CancellationTokenSource();
        var progress = new Progress<string>(message => status.Text = message);
        try
        {
            string folder = gta.Text, vehicle = model.Text;
            var result = await Task.Run(() => new VehicleMetadataFinder().Find(folder, vehicle, message =>
            {
                search.Token.ThrowIfCancellationRequested(); ((IProgress<string>)progress).Report(message);
            }, search.Token));
            sources.DataSource = result.Sources.ToArray();
            status.Text = $"Found {result.Sources.Count} sources. Confirm compatibility with the target build before inserting.";
        }
        catch (OperationCanceledException) { status.Text = "Search cancelled."; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Metadata search stopped"); }
        finally { search.Dispose(); search = null; }
    }

    private void Insert(string kind)
    {
        if (sources.SelectedItem is not VehicleMetadataSourceResult source) return;
        var blocks = kind == "vehicles.meta" ? source.Vehicles : kind == "carvariations.meta" ? source.Variations : source.Kits;
        if (blocks.Count != 1) { MessageBox.Show(this, "Select a source containing exactly one matching block. Resolve ambiguous versions before inserting."); return; }
        using var dialog = new OpenFileDialog { Filter = "GTA metadata|*.meta", FileName = kind, InitialDirectory = ToolSettingsStore.Load().RepoRoot };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            bool changed = kind == "vehicles.meta" ? VehicleMetadataFinder.InsertVehiclesBlock(dialog.FileName, blocks[0].Xml, false) :
                kind == "carvariations.meta" ? VehicleMetadataFinder.InsertCarVariationsBlock(dialog.FileName, blocks[0].Xml, false) :
                VehicleMetadataFinder.InsertCarColsKitBlock(dialog.FileName, blocks[0].Xml, false);
            status.Text = changed ? "Metadata inserted. Review with Git and scan vehicles again." : "Identity already exists; no changes made.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Metadata insertion stopped"); }
    }
}
