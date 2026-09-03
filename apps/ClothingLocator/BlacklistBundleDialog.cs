using System.Diagnostics;

namespace BLRP.ClothingLocator;

internal sealed class BlacklistBundleDialog : Form
{
    private static readonly Color Background = Color.FromArgb(16, 16, 34);
    private static readonly Color InputBackground = Color.FromArgb(40, 40, 80);
    private static readonly Color Accent = Color.FromArgb(100, 149, 237);
    private static readonly Color AccentLight = Color.FromArgb(135, 206, 235);
    private static readonly Color TextMuted = Color.FromArgb(200, 210, 225);

    private readonly string _rootPath;
    private readonly ClothingCatalog _catalog;
    private readonly ComboBox _group = new();
    private readonly DataGridView _grid = new();
    private readonly Label _summary = new();
    private readonly Button _previewSelected;
    private readonly Button _extractSelected;
    private readonly Button _zipAll;
    private readonly Button _reimport;
    private BlacklistAssetSearchResult _result = new([], 0, 0);

    public BlacklistBundleDialog(
        string rootPath,
        ClothingCatalog catalog,
        IEnumerable<string> groups,
        string? selectedGroup)
    {
        _rootPath = Path.GetFullPath(rootPath);
        _catalog = catalog;
        Text = "Blacklist clothing export";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(920, 560);
        ClientSize = new Size(1080, 680);
        BackColor = Background;
        ForeColor = Color.White;
        Font = new Font("Cascadia Mono", 9F);

        _previewSelected = CreateButton("PREVIEW SELECTED", (_, _) => PreviewSelected(), true);
        _extractSelected = CreateButton("EXTRACT SELECTED...", async (_, _) => await ExtractSelectedAsync(), true);
        _zipAll = CreateButton("ZIP ALL...", async (_, _) => await ZipAllAsync(), true);
        _reimport = CreateButton("REIMPORT...", async (_, _) => await ReimportAsync());

        BuildInterface();
        ConfigureGrid();

        _group.Items.AddRange(BusinessDirectory.Normalize(groups).Cast<object>().ToArray());
        int selectedIndex = selectedGroup == null ? -1 : _group.FindStringExact(selectedGroup);
        _group.SelectedIndex = selectedIndex >= 0 ? selectedIndex : (_group.Items.Count > 0 ? 0 : -1);
        _group.SelectedIndexChanged += (_, _) => RefreshItems();
        _grid.SelectionChanged += (_, _) => UpdateSelectionActions();
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) PreviewSelected(); };
        RefreshItems();
    }

    public bool Imported { get; private set; }

    private void BuildInterface()
    {
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 5
        };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        page.Controls.Add(CreateLabel("BLACKLIST CLOTHING EXPORT / REIMPORT", 15F, Color.White, FontStyle.Bold), 0, 0);

        var picker = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2
        };
        picker.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        picker.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        picker.Controls.Add(CreateLabel("GROUP / ACCESS RESTRICTION", 8F, AccentLight, FontStyle.Bold), 0, 0);
        _group.Dock = DockStyle.Fill;
        _group.DropDownStyle = ComboBoxStyle.DropDownList;
        _group.FlatStyle = FlatStyle.Flat;
        _group.BackColor = InputBackground;
        _group.ForeColor = Color.White;
        picker.Controls.Add(_group, 0, 1);
        page.Controls.Add(picker, 0, 1);

        _summary.Dock = DockStyle.Fill;
        _summary.ForeColor = TextMuted;
        _summary.TextAlign = ContentAlignment.MiddleLeft;
        page.Controls.Add(_summary, 0, 2);

        _grid.Dock = DockStyle.Fill;
        page.Controls.Add(_grid, 0, 3);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 6,
            RowCount = 1,
            Padding = new Padding(0, 8, 0, 0)
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        actions.Controls.Add(_previewSelected, 1, 0);
        actions.Controls.Add(_reimport, 2, 0);
        actions.Controls.Add(_extractSelected, 3, 0);
        actions.Controls.Add(_zipAll, 4, 0);
        actions.Controls.Add(CreateButton("CLOSE", (_, _) => Close()), 5, 0);
        page.Controls.Add(actions, 0, 4);

        Controls.Add(page);
    }

    private void ConfigureGrid()
    {
        _grid.BackgroundColor = Color.FromArgb(20, 20, 40);
        _grid.BorderStyle = BorderStyle.None;
        _grid.GridColor = Color.FromArgb(55, 70, 115);
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 35, 70);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = AccentLight;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
        _grid.DefaultCellStyle.BackColor = Color.FromArgb(24, 24, 50);
        _grid.DefaultCellStyle.ForeColor = Color.White;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(58, 88, 155);
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;
        _grid.RowHeadersVisible = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _grid.RowTemplate.Height = 28;

        _grid.Columns.Add("Gender", "GENDER");
        _grid.Columns.Add("Component", "COMPONENT");
        _grid.Columns.Add("Number", "CLOTHING #");
        _grid.Columns.Add("Scope", "BLACKLIST SCOPE");
        _grid.Columns.Add("Files", "FILES");
        _grid.Columns.Add("Model", "MODEL");
        _grid.Columns[0].Width = 80;
        _grid.Columns[1].Width = 100;
        _grid.Columns[2].Width = 100;
        _grid.Columns[3].Width = 360;
        _grid.Columns[4].Width = 64;
        _grid.Columns[5].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
    }

    private void RefreshItems()
    {
        _grid.Rows.Clear();
        if (_group.SelectedItem is not string group)
        {
            _result = new([], 0, 0);
            UpdateSummary();
            return;
        }

        try
        {
            _result = BlacklistBundle.Find(_rootPath, _catalog, group);
            foreach (BlacklistAssetItem item in _result.Items)
            {
                int rowIndex = _grid.Rows.Add(
                    item.Entry.Gender.ToString().ToUpperInvariant(),
                    item.Entry.Component.Code.ToUpperInvariant(),
                    item.GlobalIndex,
                    item.Scope,
                    item.Files.Count,
                    Path.GetRelativePath(_rootPath, item.Entry.FilePath));
                _grid.Rows[rowIndex].Tag = item;
                if (item.MissingTextureIndexes.Count > 0)
                {
                    _grid.Rows[rowIndex].Cells[3].ToolTipText =
                        "Missing matching YTD index(es): " + string.Join(", ", item.MissingTextureIndexes.Select(index => $"#{index}"));
                    _grid.Rows[rowIndex].DefaultCellStyle.ForeColor = Color.FromArgb(255, 190, 80);
                }
            }
        }
        catch (Exception exception)
        {
            _result = new([], 0, 0);
            MessageBox.Show(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int fileCount = _result.Items.SelectMany(item => item.Files).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        string unresolved = _result.UnresolvedDrawables == 0
            ? string.Empty
            : $"  /  {_result.UnresolvedDrawables} BASE-GAME OR MISSING DRAWABLE{(_result.UnresolvedDrawables == 1 ? string.Empty : "S")} SKIPPED";
        string missing = _result.MissingTextures == 0
            ? string.Empty
            : $"  /  {_result.MissingTextures} MISSING YTD{(_result.MissingTextures == 1 ? string.Empty : "S")}";
        _summary.Text = $"{_result.Items.Count} MODEL{(_result.Items.Count == 1 ? string.Empty : "S")}  /  {fileCount} FILE{(fileCount == 1 ? string.Empty : "S")}{unresolved}{missing}";
        UpdateSelectionActions();
        _zipAll.Enabled = _result.Items.Count > 0;
    }

    private void PreviewSelected()
    {
        if (_grid.SelectedRows.Count != 1 || _grid.SelectedRows[0].Tag is not BlacklistAssetItem item) return;
        string? texturePath = item.Files.Skip(1).FirstOrDefault() ?? _catalog.FindPreviewTexture(item.Entry);
        using var dialog = new ClothingPreviewDialog(item.Entry.FilePath, texturePath);
        dialog.ShowDialog(this);
    }

    private void UpdateSelectionActions()
    {
        _previewSelected.Enabled = _grid.SelectedRows.Count == 1;
        _extractSelected.Enabled = _grid.SelectedRows.Count > 0;
    }

    private async Task ExtractSelectedAsync()
    {
        BlacklistAssetItem[] selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => row.Tag)
            .OfType<BlacklistAssetItem>()
            .ToArray();
        if (selected.Length == 0) return;

        string defaultRoot = GetDefaultExportRoot();
        Directory.CreateDirectory(defaultRoot);
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where to create the editable clothing export folder",
            UseDescriptionForTitle = true,
            SelectedPath = defaultRoot
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        string output = Path.Combine(
            dialog.SelectedPath,
            $"{SafeName((string)_group.SelectedItem!)}-clothing-{DateTime.Now:yyyyMMdd-HHmmss}");
        bool exported = await RunBusyAsync(() => Task.Run(() => BlacklistBundle.ExportDirectory(
            _rootPath,
            (string)_group.SelectedItem!,
            selected,
            output)));
        if (!exported) return;
        Process.Start(new ProcessStartInfo(output) { UseShellExecute = true });
    }

    private async Task ZipAllAsync()
    {
        if (_result.Items.Count == 0) return;
        string defaultRoot = GetDefaultExportRoot();
        Directory.CreateDirectory(defaultRoot);
        using var dialog = new SaveFileDialog
        {
            Title = "Export all matching clothing",
            Filter = "ZIP archive (*.zip)|*.zip",
            InitialDirectory = defaultRoot,
            FileName = $"{SafeName((string)_group.SelectedItem!)}-clothing-{DateTime.Now:yyyy-MM-dd}.zip",
            AddExtension = true,
            DefaultExt = "zip",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        bool exported = await RunBusyAsync(() => Task.Run(() => BlacklistBundle.ExportZip(
            _rootPath,
            (string)_group.SelectedItem!,
            _result.Items,
            dialog.FileName)));
        if (!exported) return;
        Process.Start(new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = true,
            ArgumentList = { "/select,", dialog.FileName }
        });
    }

    private async Task ReimportAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = $"Select an edited ZIP or its {BlacklistBundle.ManifestFileName}",
            Filter = $"BLRP clothing export (*.zip;{BlacklistBundle.ManifestFileName})|*.zip;{BlacklistBundle.ManifestFileName}|ZIP archive (*.zip)|*.zip|Export manifest (*.json)|*.json",
            CheckFileExists = true,
            InitialDirectory = GetDefaultExportRoot()
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(
                this,
                "This will replace every YDD/YTD listed in the export manifest. Existing files will be backed up first.\n\nContinue?",
                "Reimport edited clothing",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        BlacklistImportResult? result = null;
        await RunBusyAsync(async () => result = await Task.Run(() => BlacklistBundle.Reimport(_rootPath, dialog.FileName)));
        if (result == null) return;
        Imported = true;
        MessageBox.Show(
            this,
            $"Reimported {result.FileCount} clothing file{(result.FileCount == 1 ? string.Empty : "s")}.\n\nBackup: {result.BackupDirectory}",
            "Reimport complete",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private async Task<bool> RunBusyAsync(Func<Task> action)
    {
        SetBusy(true);
        try
        {
            await action();
            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _group.Enabled = !busy;
        _grid.Enabled = !busy;
        _previewSelected.Enabled = !busy && _grid.SelectedRows.Count == 1;
        _extractSelected.Enabled = !busy && _grid.SelectedRows.Count > 0;
        _zipAll.Enabled = !busy && _result.Items.Count > 0;
        _reimport.Enabled = !busy;
    }

    private static string GetDefaultExportRoot()
    {
        string driveRoot = @"D:\BLRP-Clothing-Exports";
        return Directory.Exists(Path.GetPathRoot(driveRoot))
            ? driveRoot
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BLRP-Clothing-Exports");
    }

    private static string SafeName(string value)
    {
        string safe = string.Concat(value.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) || character == '|' ? '-' : character));
        return safe.Trim().Replace(' ', '-').ToLowerInvariant();
    }

    private static Label CreateLabel(string text, float size, Color color, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text,
        ForeColor = color,
        BackColor = Color.Transparent,
        Font = new Font("Cascadia Mono", size, style),
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Button CreateButton(string text, EventHandler click, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            BackColor = primary ? Accent : InputBackground,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Cascadia Mono", 8F, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 0, 0, 0),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = primary ? AccentLight : Color.FromArgb(90, 100, 149, 237);
        button.Click += click;
        return button;
    }
}
