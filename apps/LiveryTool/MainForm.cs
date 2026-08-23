namespace Badlands.LiveryTool;

internal enum MetadataBlockKind
{
    Vehicles,
    CarVariations,
    CarCols,
}

internal sealed class MainForm : Form
{
    private readonly LiveryImageConverter imageConverter = new();
    private readonly LiveryScanner liveryScanner = new();
    private readonly LiveryWorkflow liveryWorkflow = new();
    private readonly VehicleMetadataFinder vehicleMetadataFinder = new();
    private readonly ToolTip helpTip = new()
    {
        AutoPopDelay = 20000,
        InitialDelay = 400,
        ReshowDelay = 150,
        ShowAlways = true,
    };

    private readonly TextBox repoTextBox = new();
    private readonly TextBox inputImageTextBox = new();
    private readonly TextBox outputDdsTextBox = new();
    private readonly TextBox vehicleDataFolderTextBox = new();
    private readonly TextBox templateYftTextBox = new();
    private readonly TextBox modkitMasterListTextBox = new();
    private readonly CheckBox updateModkitMasterListCheckBox = new();
    private readonly TextBox modkitEntryTextBox = new();
    private readonly TextBox liveryPrefixTextBox = new();
    private readonly NumericUpDown liveryNumberInput = new();
    private readonly TextBox vehicleModelTextBox = new();
    private readonly NumericUpDown liverySlotInput = new();
    private readonly TextBox modShopLabelTextBox = new();
    private readonly TextBox displayNameTextBox = new();
    private readonly CheckBox lockLiveryCheckBox = new();
    private readonly TextBox permissionTextBox = new();
    private readonly TextBox gtaFolderTextBox = new();
    private readonly TextBox metadataVehicleTextBox = new();
    private readonly ComboBox metadataSourceComboBox = new();
    private readonly TextBox metadataResultsTextBox = new();
    private readonly TextBox metadataVehiclesTargetTextBox = new();
    private readonly TextBox metadataCarVariationsTargetTextBox = new();
    private readonly TextBox metadataCarColsTargetTextBox = new();
    private readonly CheckBox createBackupsCheckBox = new();
    private readonly CheckBox blacklistCheckBox = new();
    private readonly NumericUpDown blacklistSlotInput = new();
    private readonly TextBox blacklistCommentTextBox = new();
    private readonly PictureBox previewBox = new();
    private readonly DataGridView slotsGrid = new();
    private readonly TextBox logTextBox = new();
    private readonly Button convertButton = new();
    private readonly Button signBatchButton = new();
    private readonly Button scanButton = new();
    private readonly Button findMetadataButton = new();
    private readonly Button copyVehiclesMetadataButton = new();
    private readonly Button copyCarVariationsMetadataButton = new();
    private readonly Button copyCarColsMetadataButton = new();
    private readonly Button insertVehiclesMetadataButton = new();
    private readonly Button insertCarVariationsMetadataButton = new();
    private readonly Button insertCarColsMetadataButton = new();
    private readonly Button newLiveryButton = new();
    private readonly Button applyButton = new();
    private VehicleMetadataSearchResult? metadataSearchResult;

    public MainForm()
    {
        Text = "BLRP Livery Tool";
        string iconPath = Path.Combine(AppContext.BaseDirectory, "BLRP.ico");
        if (File.Exists(iconPath)) Icon = new Icon(iconPath);
        MinimumSize = new Size(1100, 650);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        BlrpTheme.Apply(this);
        ConfigureHelp();
        LoadSettings();
        outputDdsTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "livery.dxt5.dds");
        FormClosing += (_, _) => SaveSettings();
    }

    private void BuildLayout()
    {
        var viewport = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
        };
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(12),
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 330));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));

        root.Controls.Add(BuildBrandHeader(), 0, 0);
        root.Controls.Add(BuildRepoRow(), 0, 1);
        root.Controls.Add(BuildScanPanel(), 0, 2);
        root.Controls.Add(BuildMetadataPanel(), 0, 3);
        root.Controls.Add(BuildConversionPanel(), 0, 4);
        root.Controls.Add(BuildWorkflowPanel(), 0, 5);
        root.Controls.Add(BuildLogPanel(), 0, 6);

        viewport.Controls.Add(root);
        Controls.Add(viewport);
        Shown += (_, _) =>
        {
            var resetScroll = new System.Windows.Forms.Timer { Interval = 50 };
            resetScroll.Tick += (_, _) =>
            {
                resetScroll.Stop();
                ActiveControl = null;
                viewport.AutoScrollPosition = Point.Empty;
                resetScroll.Dispose();
            };
            resetScroll.Start();
        };
    }

    private static Control BuildBrandHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        var logo = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom,
            Image = Image.FromFile(Path.Combine(AppContext.BaseDirectory, "BLRP_Logo.png")), Margin = new Padding(0, 0, 12, 4) };
        header.Controls.Add(logo, 0, 0);
        header.SetRowSpan(logo, 2);
        header.Controls.Add(new Label { Text = "LIVERY TOOL", Dock = DockStyle.Fill, Font = new Font("Cascadia Mono", 18F, FontStyle.Bold), ForeColor = BlrpTheme.AccentLight }, 1, 0);
        header.Controls.Add(new Label { Text = "BADLANDSRP  /  SCAN  /  CONVERT  /  INSTALL", Dock = DockStyle.Fill, Font = new Font("Cascadia Mono", 8F, FontStyle.Bold), ForeColor = Color.White }, 1, 1);
        return header;
    }

    private Control BuildRepoRow()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Text = "Step 1: Repo / Shared Files",
        };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            AutoSize = true,
            Padding = new Padding(10),
        };

        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Repo",
            Padding = new Padding(0, 7, 8, 0),
        }, 0, 0);

        repoTextBox.Dock = DockStyle.Fill;
        panel.Controls.Add(repoTextBox, 1, 0);

        var browseButton = new Button
        {
            AutoSize = true,
            Text = "Browse...",
        };
        browseButton.Click += (_, _) => BrowseRepo();
        panel.Controls.Add(browseButton, 2, 0);

        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Modkit list",
            Padding = new Padding(0, 7, 8, 0),
        }, 0, 1);

        modkitMasterListTextBox.Dock = DockStyle.Fill;
        panel.Controls.Add(modkitMasterListTextBox, 1, 1);

        var browseModkitButton = new Button
        {
            AutoSize = true,
            Text = "Browse...",
        };
        browseModkitButton.Click += (_, _) => BrowseModkitMasterList();
        panel.Controls.Add(browseModkitButton, 2, 1);

        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "GTA V",
            Padding = new Padding(0, 7, 8, 0),
        }, 0, 2);

        gtaFolderTextBox.Dock = DockStyle.Fill;
        panel.Controls.Add(gtaFolderTextBox, 1, 2);

        var browseGtaButton = new Button
        {
            AutoSize = true,
            Text = "Browse...",
        };
        browseGtaButton.Click += (_, _) => BrowseGtaFolder();
        panel.Controls.Add(browseGtaButton, 2, 2);

        group.Controls.Add(panel);
        return group;
    }

    private Control BuildConversionPanel()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Step 3: Select Image / Template",
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(10),
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4,
        };

        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddFileRow(fields, 0, "Input", inputImageTextBox, BrowseInputImage);
        AddFileRow(fields, 1, "Output", outputDdsTextBox, BrowseOutputDds);

        convertButton.Text = "Convert to DXT5 DDS";
        convertButton.AutoSize = true;
        convertButton.Click += (_, _) => ConvertImage();
        fields.Controls.Add(convertButton, 1, 2);

        signBatchButton.Text = "Open Sign Batch Builder...";
        signBatchButton.AutoSize = true;
        signBatchButton.Click += (_, _) =>
        {
            using var dialog = new SignBatchBuilderForm();
            dialog.ShowDialog(this);
        };
        fields.Controls.Add(signBatchButton, 2, 2);

        var note = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = "Output is DDS FourCC DXT5 / BC3 with generated mipmaps.",
            Padding = new Padding(0, 12, 0, 0),
        };
        fields.SetColumnSpan(note, 3);
        fields.Controls.Add(note, 0, 3);

        previewBox.Dock = DockStyle.Fill;
        previewBox.BorderStyle = BorderStyle.FixedSingle;
        previewBox.SizeMode = PictureBoxSizeMode.Zoom;
        previewBox.BackColor = Color.FromArgb(32, 32, 32);

        layout.Controls.Add(fields, 0, 0);
        layout.Controls.Add(previewBox, 1, 0);
        group.Controls.Add(layout);

        return group;
    }

    private Control BuildWorkflowPanel()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Text = "Step 4: Livery Details / Apply",
        };

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 6,
            Padding = new Padding(10),
        };

        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));

        AddFolderRow(fields, 0, "Vehicle data", vehicleDataFolderTextBox, BrowseVehicleDataFolder);
        AddFileRow(fields, 1, "Template YFT", templateYftTextBox, BrowseTemplateYft);

        AddTextCell(fields, 2, 0, "Prefix", liveryPrefixTextBox);
        ConfigureNumberInput(liveryNumberInput, maximum: 999);
        AddNumberCell(fields, 2, 2, "YFT #", liveryNumberInput);
        AddTextCell(fields, 2, 4, "Model/hash", vehicleModelTextBox);

        ConfigureNumberInput(liverySlotInput, maximum: 999);
        AddNumberCell(fields, 3, 0, "Lua slot", liverySlotInput);
        AddTextCell(fields, 3, 2, "Label", modShopLabelTextBox);
        AddTextCell(fields, 3, 4, "Name", displayNameTextBox);

        lockLiveryCheckBox.Text = "Lock livery";
        lockLiveryCheckBox.AutoSize = true;
        lockLiveryCheckBox.CheckedChanged += (_, _) => TogglePermissionInputs();
        fields.Controls.Add(lockLiveryCheckBox, 0, 4);
        fields.SetColumnSpan(lockLiveryCheckBox, 2);
        AddTextCell(fields, 4, 2, "Permission", permissionTextBox);

        blacklistCheckBox.Text = "Blacklist old slot";
        blacklistCheckBox.AutoSize = true;
        blacklistCheckBox.CheckedChanged += (_, _) => ToggleBlacklistInputs();
        fields.Controls.Add(blacklistCheckBox, 0, 5);
        fields.SetColumnSpan(blacklistCheckBox, 2);

        ConfigureNumberInput(blacklistSlotInput, maximum: 999);
        AddNumberCell(fields, 5, 2, "Old slot", blacklistSlotInput);

        AddTextCell(fields, 5, 4, "Old note", blacklistCommentTextBox);

        createBackupsCheckBox.Text = "Create .bak files";
        createBackupsCheckBox.AutoSize = true;
        fields.Controls.Add(createBackupsCheckBox, 0, 6);
        fields.SetColumnSpan(createBackupsCheckBox, 2);

        updateModkitMasterListCheckBox.Text = "Update modkit master list";
        updateModkitMasterListCheckBox.AutoSize = true;
        updateModkitMasterListCheckBox.CheckedChanged += (_, _) => ToggleModkitInputs();
        fields.Controls.Add(updateModkitMasterListCheckBox, 2, 6);
        fields.SetColumnSpan(updateModkitMasterListCheckBox, 2);

        AddTextCell(fields, 6, 4, "Modkit entry", modkitEntryTextBox);

        newLiveryButton.Text = "New Livery";
        newLiveryButton.AutoSize = true;
        newLiveryButton.Click += (_, _) => ClearLiveryFields();
        fields.Controls.Add(newLiveryButton, 4, 8);

        applyButton.Text = "Apply Livery";
        applyButton.AutoSize = true;
        applyButton.Click += (_, _) => ApplyLivery();
        fields.Controls.Add(applyButton, 5, 8);

        group.Controls.Add(fields);
        ToggleBlacklistInputs();
        ToggleModkitInputs();
        TogglePermissionInputs();
        return group;
    }

    private Control BuildScanPanel()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Step 2: Scan Existing Liveries",
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            Padding = new Padding(10),
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        scanButton.Text = "Scan Liveries";
        scanButton.AutoSize = true;
        scanButton.Click += (_, _) => ScanLiveries();
        layout.Controls.Add(scanButton, 0, 0);

        slotsGrid.Dock = DockStyle.Fill;
        slotsGrid.AllowUserToAddRows = false;
        slotsGrid.AllowUserToDeleteRows = false;
        slotsGrid.ReadOnly = true;
        slotsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        slotsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        slotsGrid.RowHeadersVisible = false;
        slotsGrid.CellDoubleClick += (_, _) => UseSelectedSlotGroup();
        layout.Controls.Add(slotsGrid, 0, 1);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildMetadataPanel()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Step 2b: Source GTA Metadata",
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 6,
            Padding = new Padding(10),
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Model",
            Padding = new Padding(0, 7, 8, 0),
        }, 0, 0);

        metadataVehicleTextBox.Dock = DockStyle.Fill;
        layout.Controls.Add(metadataVehicleTextBox, 1, 0);
        layout.SetColumnSpan(metadataVehicleTextBox, 3);

        findMetadataButton.Text = "Find Metadata";
        findMetadataButton.AutoSize = true;
        findMetadataButton.Click += (_, _) => FindBaseMetadata();
        layout.Controls.Add(findMetadataButton, 4, 0);

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Source",
            Padding = new Padding(0, 7, 8, 0),
        }, 0, 1);

        metadataSourceComboBox.Dock = DockStyle.Fill;
        metadataSourceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        metadataSourceComboBox.SelectedIndexChanged += (_, _) => UpdateSelectedMetadataSource();
        layout.Controls.Add(metadataSourceComboBox, 1, 1);
        layout.SetColumnSpan(metadataSourceComboBox, 3);

        copyVehiclesMetadataButton.Text = "Copy vehicles";
        copyVehiclesMetadataButton.AutoSize = true;
        copyVehiclesMetadataButton.Click += (_, _) => CopySelectedMetadataBlocks(MetadataBlockKind.Vehicles);
        layout.Controls.Add(copyVehiclesMetadataButton, 4, 1);

        copyCarVariationsMetadataButton.Text = "Copy carvar";
        copyCarVariationsMetadataButton.AutoSize = true;
        copyCarVariationsMetadataButton.Click += (_, _) => CopySelectedMetadataBlocks(MetadataBlockKind.CarVariations);
        layout.Controls.Add(copyCarVariationsMetadataButton, 5, 1);

        copyCarColsMetadataButton.Text = "Copy carcols";
        copyCarColsMetadataButton.AutoSize = true;
        copyCarColsMetadataButton.Click += (_, _) => CopySelectedMetadataBlocks(MetadataBlockKind.CarCols);
        layout.Controls.Add(copyCarColsMetadataButton, 6, 1);

        AddMetadataTargetRow(layout, 2, "vehicles target", metadataVehiclesTargetTextBox, BrowseMetadataVehiclesTarget, insertVehiclesMetadataButton, "Insert vehicles", InsertVehiclesMetadata);
        AddMetadataTargetRow(layout, 3, "carvar target", metadataCarVariationsTargetTextBox, BrowseMetadataCarVariationsTarget, insertCarVariationsMetadataButton, "Insert carvar", InsertCarVariationsMetadata);
        AddMetadataTargetRow(layout, 4, "carcols target", metadataCarColsTargetTextBox, BrowseMetadataCarColsTarget, insertCarColsMetadataButton, "Insert carcols", InsertCarColsMetadata);

        metadataResultsTextBox.Dock = DockStyle.Fill;
        metadataResultsTextBox.Multiline = true;
        metadataResultsTextBox.ReadOnly = true;
        metadataResultsTextBox.ScrollBars = ScrollBars.Both;
        metadataResultsTextBox.WordWrap = false;
        layout.Controls.Add(metadataResultsTextBox, 0, 5);
        layout.SetColumnSpan(metadataResultsTextBox, 7);

        group.Controls.Add(layout);
        return group;
    }

    private static void AddMetadataTargetRow(
        TableLayoutPanel layout,
        int row,
        string label,
        TextBox textBox,
        Action browse,
        Button insertButton,
        string insertText,
        Action insert)
    {
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = label,
            Padding = new Padding(0, 7, 8, 0),
        }, 0, row);

        textBox.Dock = DockStyle.Fill;
        layout.Controls.Add(textBox, 1, row);
        layout.SetColumnSpan(textBox, 3);

        var browseButton = new Button
        {
            AutoSize = true,
            Text = "Browse...",
        };
        browseButton.Click += (_, _) => browse();
        layout.Controls.Add(browseButton, 4, row);

        insertButton.Text = insertText;
        insertButton.AutoSize = true;
        insertButton.Click += (_, _) => insert();
        layout.Controls.Add(insertButton, 5, row);
        layout.SetColumnSpan(insertButton, 2);
    }

    private Control BuildLogPanel()
    {
        logTextBox.Dock = DockStyle.Fill;
        logTextBox.Multiline = true;
        logTextBox.ReadOnly = true;
        logTextBox.ScrollBars = ScrollBars.Vertical;
        return logTextBox;
    }

    private static void AddFileRow(TableLayoutPanel panel, int row, string label, TextBox textBox, Action browse)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = label,
            Padding = new Padding(0, 7, 8, 0),
        }, 0, row);

        textBox.Dock = DockStyle.Fill;
        panel.Controls.Add(textBox, 1, row);

        var button = new Button
        {
            AutoSize = true,
            Text = "Browse...",
        };
        button.Click += (_, _) => browse();
        panel.Controls.Add(button, 2, row);
    }

    private static void AddFolderRow(TableLayoutPanel panel, int row, string label, TextBox textBox, Action browse)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = label,
            Padding = new Padding(0, 7, 8, 0),
        }, 0, row);

        textBox.Dock = DockStyle.Fill;
        panel.Controls.Add(textBox, 1, row);
        panel.SetColumnSpan(textBox, 4);

        var button = new Button
        {
            AutoSize = true,
            Text = "Browse...",
        };
        button.Click += (_, _) => browse();
        panel.Controls.Add(button, 5, row);
    }

    private static void AddTextCell(TableLayoutPanel panel, int row, int column, string label, TextBox textBox)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = label,
            Padding = new Padding(0, 7, 8, 0),
        }, column, row);

        textBox.Dock = DockStyle.Fill;
        panel.Controls.Add(textBox, column + 1, row);
    }

    private static void AddNumberCell(TableLayoutPanel panel, int row, int column, string label, NumericUpDown input)
    {
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = label,
            Padding = new Padding(12, 7, 8, 0),
        }, column, row);

        input.Dock = DockStyle.Fill;
        panel.Controls.Add(input, column + 1, row);
    }

    private static void ConfigureNumberInput(NumericUpDown input, int maximum)
    {
        input.Minimum = 0;
        input.Maximum = maximum;
        input.Width = 90;
    }

    private void BrowseRepo()
    {
        var previousRepoRoot = repoTextBox.Text;
        using var dialog = new FolderBrowserDialog
        {
            InitialDirectory = Directory.Exists(repoTextBox.Text) ? repoTextBox.Text : Paths.DefaultRepoRoot,
            Description = "Select the BadlandsRP repo folder",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            repoTextBox.Text = dialog.SelectedPath;
            if (string.IsNullOrWhiteSpace(modkitMasterListTextBox.Text) ||
                IsPathUnder(modkitMasterListTextBox.Text, previousRepoRoot))
            {
                modkitMasterListTextBox.Text = Paths.GetDefaultModkitMasterListPath(repoTextBox.Text);
            }

            UpdatePathHints();
        }
    }

    private void BrowseModkitMasterList()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select modkit master list",
            Filter = "Text files|*.txt|All files|*.*",
            FileName = Path.GetFileName(modkitMasterListTextBox.Text),
            InitialDirectory = Directory.Exists(Path.GetDirectoryName(modkitMasterListTextBox.Text))
                ? Path.GetDirectoryName(modkitMasterListTextBox.Text)
                : Path.Combine(repoTextBox.Text, "resources", "addons"),
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            modkitMasterListTextBox.Text = dialog.FileName;
        }
    }

    private void BrowseGtaFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            InitialDirectory = Directory.Exists(gtaFolderTextBox.Text)
                ? gtaFolderTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Description = "Select the Grand Theft Auto V install folder",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            gtaFolderTextBox.Text = dialog.SelectedPath;
        }
    }

    private void BrowseVehicleDataFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            InitialDirectory = Directory.Exists(vehicleDataFolderTextBox.Text)
                ? vehicleDataFolderTextBox.Text
                : Paths.GetDefaultVehicleDataFolder(repoTextBox.Text),
            Description = "Select the folder containing carcols.meta",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            vehicleDataFolderTextBox.Text = dialog.SelectedPath;
        }
    }

    private void BrowseMetadataVehiclesTarget()
    {
        BrowseMetadataTarget("Select target vehicles.meta", metadataVehiclesTargetTextBox, "vehicles.meta");
    }

    private void BrowseMetadataCarVariationsTarget()
    {
        BrowseMetadataTarget("Select target carvariations.meta", metadataCarVariationsTargetTextBox, "carvariations.meta");
    }

    private void BrowseMetadataCarColsTarget()
    {
        BrowseMetadataTarget("Select target carcols.meta", metadataCarColsTargetTextBox, "carcols.meta");
    }

    private void BrowseMetadataTarget(string title, TextBox targetTextBox, string fileName)
    {
        var initialDirectory = Directory.Exists(Path.GetDirectoryName(targetTextBox.Text))
            ? Path.GetDirectoryName(targetTextBox.Text)
            : Directory.Exists(vehicleDataFolderTextBox.Text)
                ? vehicleDataFolderTextBox.Text
                : Paths.GetDefaultVehicleDataFolder(repoTextBox.Text);

        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Meta files|*.meta|All files|*.*",
            FileName = fileName,
            InitialDirectory = initialDirectory,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            targetTextBox.Text = dialog.FileName;
        }
    }

    private void BrowseTemplateYft()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select existing livery YFT to use as a template",
            Filter = "YFT files|*.yft|All files|*.*",
            InitialDirectory = Paths.GetDefaultLiveryStreamFolder(repoTextBox.Text),
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            templateYftTextBox.Text = dialog.FileName;
        }
    }

    private void UseSelectedSlotGroup()
    {
        if (slotsGrid.CurrentRow?.DataBoundItem is not LiverySlotGroup group)
        {
            return;
        }

        liveryPrefixTextBox.Text = group.Prefix;
        liveryNumberInput.Value = Math.Min(group.NextFileNumber, (int)liveryNumberInput.Maximum);
        liverySlotInput.Value = Math.Min(Math.Max(group.NextLuaSlot, 0), (int)liverySlotInput.Maximum);

        var normalizedPrefix = group.Prefix.TrimEnd('_');
        var suggestedLabelPrefix = normalizedPrefix
            .Replace("_livery", "_LIV", StringComparison.OrdinalIgnoreCase)
            .Replace("_liv", "_LIV", StringComparison.OrdinalIgnoreCase)
            .ToUpperInvariant();
        modShopLabelTextBox.Text = $"{suggestedLabelPrefix}{group.NextFileNumber}";
    }

    private void BrowseInputImage()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select livery image",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.tga|All files|*.*",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        inputImageTextBox.Text = dialog.FileName;
        previewBox.Image?.Dispose();
        previewBox.Image = Image.FromFile(dialog.FileName);

        var suggestedName = Path.GetFileNameWithoutExtension(dialog.FileName) + ".dxt5.dds";
        outputDdsTextBox.Text = Path.Combine(Path.GetDirectoryName(dialog.FileName) ?? ".", suggestedName);
    }

    private void BrowseOutputDds()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save DXT5 DDS",
            Filter = "DDS texture|*.dds|All files|*.*",
            FileName = Path.GetFileName(outputDdsTextBox.Text),
            InitialDirectory = Path.GetDirectoryName(outputDdsTextBox.Text),
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            outputDdsTextBox.Text = dialog.FileName;
        }
    }

    private void ConvertImage()
    {
        try
        {
            ToggleWorkState(false);
            var result = imageConverter.ConvertToDxt5Dds(inputImageTextBox.Text, outputDdsTextBox.Text);
            Log($"Converted {result.Width}x{result.Height} image to {result.OutputPath}");
            Log($"DDS FourCC: {result.FourCc}; bytes: {result.OutputBytes:N0}");
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Conversion failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            ToggleWorkState(true);
        }
    }

    private void ApplyLivery()
    {
        try
        {
            ToggleWorkState(false);
            var result = liveryWorkflow.Apply(new LiveryApplyRequest(
                repoTextBox.Text,
                vehicleDataFolderTextBox.Text,
                inputImageTextBox.Text,
                templateYftTextBox.Text,
                liveryPrefixTextBox.Text,
                (int)liveryNumberInput.Value,
                vehicleModelTextBox.Text,
                (int)liverySlotInput.Value,
                modShopLabelTextBox.Text,
                displayNameTextBox.Text,
                lockLiveryCheckBox.Checked ? permissionTextBox.Text : string.Empty,
                blacklistCheckBox.Checked ? (int)blacklistSlotInput.Value : null,
                blacklistCommentTextBox.Text,
                updateModkitMasterListCheckBox.Checked,
                modkitMasterListTextBox.Text,
                modkitEntryTextBox.Text,
                createBackupsCheckBox.Checked));

            foreach (var message in result.Messages)
            {
                Log(message);
            }

            foreach (var path in result.ChangedFiles)
            {
                Log($"Changed: {path}");
            }
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Apply failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            ToggleWorkState(true);
        }
    }

    private void ScanLiveries()
    {
        try
        {
            ToggleWorkState(false);
            var groups = liveryScanner.Scan(repoTextBox.Text);
            slotsGrid.DataSource = groups;
            ConfigureSlotsGridColumns();
            Log($"Scanned {groups.Count} livery groups.");
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Scan failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            ToggleWorkState(true);
        }
    }

    private async void FindBaseMetadata()
    {
        try
        {
            ToggleWorkState(false);
            metadataResultsTextBox.Clear();
            metadataSourceComboBox.DataSource = null;
            metadataSearchResult = null;
            var modelName = string.IsNullOrWhiteSpace(metadataVehicleTextBox.Text)
                ? vehicleModelTextBox.Text
                : metadataVehicleTextBox.Text;

            Log($"Searching base GTA metadata for {modelName.Trim()}...");
            var result = await Task.Run(() => vehicleMetadataFinder.Find(
                gtaFolderTextBox.Text,
                modelName,
                message => BeginInvoke(new Action(() => Log(message)))));

            metadataSearchResult = result;
            metadataSourceComboBox.DataSource = result.Sources.ToArray();
            metadataResultsTextBox.Text = result.Sources.Count > 0
                ? VehicleMetadataFinder.Format(result.Sources[0])
                : VehicleMetadataFinder.Format(result);
            Log($"Metadata search complete: {result.Sources.Count} source(s), {result.Vehicles.Count} vehicle block(s), {result.Variations.Count} variation block(s), {result.Kits.Count} carcols kit block(s).");
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Metadata search failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            ToggleWorkState(true);
        }
    }

    private void UpdateSelectedMetadataSource()
    {
        if (GetSelectedMetadataSource() is not { } source)
        {
            return;
        }

        metadataResultsTextBox.Text = VehicleMetadataFinder.Format(source);
    }

    private void CopySelectedMetadataBlocks(MetadataBlockKind kind)
    {
        var blocks = GetSelectedBlocks(kind);
        if (blocks.Count == 0)
        {
            Log($"No {GetMetadataKindLabel(kind)} block is available for the selected source.");
            return;
        }

        Clipboard.SetText(string.Join($"{Environment.NewLine}{Environment.NewLine}", blocks.Select(block => block.Xml)));
        Log($"Copied {blocks.Count} {GetMetadataKindLabel(kind)} block(s) from {GetSelectedMetadataSource()?.DisplayName}.");
    }

    private void InsertVehiclesMetadata()
    {
        InsertSelectedMetadataBlocks(
            MetadataBlockKind.Vehicles,
            metadataVehiclesTargetTextBox.Text,
            VehicleMetadataFinder.InsertVehiclesBlock);
    }

    private void InsertCarVariationsMetadata()
    {
        InsertSelectedMetadataBlocks(
            MetadataBlockKind.CarVariations,
            metadataCarVariationsTargetTextBox.Text,
            VehicleMetadataFinder.InsertCarVariationsBlock);
    }

    private void InsertCarColsMetadata()
    {
        InsertSelectedMetadataBlocks(
            MetadataBlockKind.CarCols,
            metadataCarColsTargetTextBox.Text,
            VehicleMetadataFinder.InsertCarColsKitBlock);
    }

    private void InsertSelectedMetadataBlocks(
        MetadataBlockKind kind,
        string targetPath,
        Func<string, string, bool, bool> inserter)
    {
        try
        {
            var blocks = GetSelectedBlocks(kind);
            if (blocks.Count == 0)
            {
                Log($"No {GetMetadataKindLabel(kind)} block is available for the selected source.");
                return;
            }

            var inserted = 0;
            var skipped = 0;
            foreach (var block in blocks)
            {
                if (inserter(targetPath, block.Xml, createBackupsCheckBox.Checked))
                {
                    inserted++;
                }
                else
                {
                    skipped++;
                }
            }

            Log($"Inserted {inserted} {GetMetadataKindLabel(kind)} block(s) into {targetPath}. Skipped {skipped} duplicate block(s).");
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Metadata insert failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private VehicleMetadataSourceResult? GetSelectedMetadataSource()
    {
        return metadataSourceComboBox.SelectedItem as VehicleMetadataSourceResult;
    }

    private IReadOnlyList<VehicleMetadataBlock> GetSelectedBlocks(MetadataBlockKind kind)
    {
        if (GetSelectedMetadataSource() is not { } source)
        {
            return Array.Empty<VehicleMetadataBlock>();
        }

        return kind switch
        {
            MetadataBlockKind.Vehicles => source.Vehicles,
            MetadataBlockKind.CarVariations => source.Variations,
            MetadataBlockKind.CarCols => source.Kits,
            _ => Array.Empty<VehicleMetadataBlock>(),
        };
    }

    private static string GetMetadataKindLabel(MetadataBlockKind kind)
    {
        return kind switch
        {
            MetadataBlockKind.Vehicles => "vehicles",
            MetadataBlockKind.CarVariations => "carvariations",
            MetadataBlockKind.CarCols => "carcols",
            _ => "metadata",
        };
    }

    private void ToggleBlacklistInputs()
    {
        blacklistSlotInput.Enabled = blacklistCheckBox.Checked;
        blacklistCommentTextBox.Enabled = blacklistCheckBox.Checked;
    }

    private void ToggleModkitInputs()
    {
        modkitEntryTextBox.Enabled = updateModkitMasterListCheckBox.Checked;
    }

    private void TogglePermissionInputs()
    {
        permissionTextBox.Enabled = lockLiveryCheckBox.Checked;
    }

    private void ClearLiveryFields()
    {
        inputImageTextBox.Clear();
        outputDdsTextBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "livery.dxt5.dds");
        vehicleDataFolderTextBox.Clear();
        templateYftTextBox.Clear();
        liveryPrefixTextBox.Clear();
        liveryNumberInput.Value = 0;
        vehicleModelTextBox.Clear();
        liverySlotInput.Value = 0;
        modShopLabelTextBox.Clear();
        displayNameTextBox.Clear();
        lockLiveryCheckBox.Checked = false;
        permissionTextBox.Clear();
        blacklistCheckBox.Checked = false;
        blacklistSlotInput.Value = 0;
        blacklistCommentTextBox.Clear();
        updateModkitMasterListCheckBox.Checked = false;
        modkitEntryTextBox.Clear();
        slotsGrid.ClearSelection();

        previewBox.Image?.Dispose();
        previewBox.Image = null;

        Log("Cleared livery fields. Repo, modkit list path, backup setting, and scan results were kept.");
    }

    private void ConfigureSlotsGridColumns()
    {
        if (slotsGrid.Columns[nameof(LiverySlotGroup.Prefix)] is { } prefixColumn)
        {
            prefixColumn.HeaderText = "Prefix";
        }

        if (slotsGrid.Columns[nameof(LiverySlotGroup.ExistingFileNumbers)] is { } existingColumn)
        {
            existingColumn.HeaderText = "Existing YFT #s";
        }

        if (slotsGrid.Columns[nameof(LiverySlotGroup.NextFileNumber)] is { } nextFileColumn)
        {
            nextFileColumn.HeaderText = "Next YFT #";
        }

        if (slotsGrid.Columns[nameof(LiverySlotGroup.NextLuaSlot)] is { } nextLuaColumn)
        {
            nextLuaColumn.HeaderText = "Lua slot";
        }

        if (slotsGrid.Columns[nameof(LiverySlotGroup.Count)] is { } countColumn)
        {
            countColumn.HeaderText = "Count";
        }
    }

    private void ToggleWorkState(bool enabled)
    {
        convertButton.Enabled = enabled;
        scanButton.Enabled = enabled;
        findMetadataButton.Enabled = enabled;
        copyVehiclesMetadataButton.Enabled = enabled;
        copyCarVariationsMetadataButton.Enabled = enabled;
        copyCarColsMetadataButton.Enabled = enabled;
        insertVehiclesMetadataButton.Enabled = enabled;
        insertCarVariationsMetadataButton.Enabled = enabled;
        insertCarColsMetadataButton.Enabled = enabled;
        applyButton.Enabled = enabled;
        Cursor = enabled ? Cursors.Default : Cursors.WaitCursor;
    }

    private void Log(string message)
    {
        logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void LoadSettings()
    {
        var settings = ToolSettingsStore.Load();
        repoTextBox.Text = string.IsNullOrWhiteSpace(settings.RepoRoot) ? Paths.DefaultRepoRoot : settings.RepoRoot;
        modkitMasterListTextBox.Text = string.IsNullOrWhiteSpace(settings.ModkitMasterListPath)
            ? Paths.GetDefaultModkitMasterListPath(repoTextBox.Text)
            : settings.ModkitMasterListPath;
        gtaFolderTextBox.Text = settings.GtaFolder;
        createBackupsCheckBox.Checked = settings.CreateBackups;
        UpdatePathHints();
    }

    private void SaveSettings()
    {
        ToolSettingsStore.Save(new ToolSettings(
            repoTextBox.Text.Trim(),
            modkitMasterListTextBox.Text.Trim(),
            createBackupsCheckBox.Checked,
            gtaFolderTextBox.Text.Trim()));
    }

    private void ConfigureHelp()
    {
        SetHelp(repoTextBox, "The root BadlandsRP folder. Browse to the local clone you want this tool to edit.");
        repoTextBox.Leave += (_, _) => UpdatePathHints();

        SetHelp(inputImageTextBox, "The PNG or image file for the new livery artwork. The tool converts this to DXT5 DDS.");
        inputImageTextBox.PlaceholderText = @"Example: D:\Liveries\bisonhf_silent.png";

        SetHelp(outputDdsTextBox, "Optional standalone DDS output path used by the Convert button. Apply Livery also creates a temporary DXT5 DDS automatically.");
        SetHelp(signBatchButton, "Batch-convert sign PNG or DDS files into ready-to-use YFT and DDS pairs using the built-in sign template.");
        outputDdsTextBox.PlaceholderText = @"Example: D:\Liveries\bisonhf_livery_29.dds";
        SetHelp(convertButton, "Only converts the selected image to a DXT5 DDS. It does not edit any vehicle files.");
        SetHelp(previewBox, "Image preview for the selected input artwork.");

        SetHelp(modkitMasterListTextBox, "Path to resources\\addons\\! modkit master list.txt. This is saved with your settings and only edited when Update modkit master list is checked.");

        SetHelp(gtaFolderTextBox, "The local Grand Theft Auto V install folder. Used read-only to search base-game RPF metadata.");
        gtaFolderTextBox.PlaceholderText = @"Example: C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V";

        SetHelp(metadataVehicleTextBox, "Base-game vehicle model to search in vehicles.meta, carvariations, and referenced carcols kits. Leave blank to use Model/hash below.");
        metadataVehicleTextBox.PlaceholderText = "Example: tailgater2";
        SetHelp(metadataSourceComboBox, "Choose which DLC pack or base-game source to use for copying or inserting metadata.");
        SetHelp(findMetadataButton, "Searches the selected GTA V folder read-only and extracts matching vehicle, carvariations, and carcols kit XML blocks.");
        SetHelp(copyVehiclesMetadataButton, "Copies only the selected source's vehicles.meta Item block.");
        SetHelp(copyCarVariationsMetadataButton, "Copies only the selected source's carvariations Item block.");
        SetHelp(copyCarColsMetadataButton, "Copies only the selected source's carcols kit Item block.");
        SetHelp(metadataVehiclesTargetTextBox, "Existing vehicles.meta file to append the selected vehicles Item into.");
        SetHelp(metadataCarVariationsTargetTextBox, "Existing carvariations.meta file to append the selected variationData Item into.");
        SetHelp(metadataCarColsTargetTextBox, "Existing carcols.meta file to append the selected kit Item into.");
        SetHelp(insertVehiclesMetadataButton, "Appends the selected vehicles Item to the target vehicles.meta InitDatas section.");
        SetHelp(insertCarVariationsMetadataButton, "Appends the selected carvariations Item to the target carvariations.meta variationData section.");
        SetHelp(insertCarColsMetadataButton, "Appends the selected carcols kit Item to the target carcols.meta Kits section.");
        SetHelp(metadataResultsTextBox, "Preview of the selected source's blocks only. Use the per-part copy or insert buttons above.");

        SetHelp(vehicleDataFolderTextBox, "Folder containing the carcols.meta to edit. For shared liveries use resources\\addons\\data\\custom_vehicle_liverys. For Gabz cars, choose that vehicle's data folder.");

        SetHelp(templateYftTextBox, "An existing livery .yft to copy as the template. Pick one from the same vehicle when possible, then the tool replaces its embedded texture.");

        SetHelp(liveryPrefixTextBox, "The file name before the number. Example: bisonhf_livery_ creates bisonhf_livery_29.yft when File # is 29.");
        liveryPrefixTextBox.PlaceholderText = "Example: bisonhf_livery_";

        SetHelp(liveryNumberInput, "The number at the end of the streamed YFT file name. Example: 29 for bisonhf_livery_29.yft.");

        SetHelp(vehicleModelTextBox, "The vehicle key used in blrp_custom_liveries.lua. This is the hash-style name inside backticks, like gbbisonhf or tailgater2a.");
        vehicleModelTextBox.PlaceholderText = "Example: gbbisonhf";

        SetHelp(liverySlotInput, "The zero-based Lua slot used by custom_liveries. This is usually one less than the YFT file number. Example: bisonhf_livery_29.yft maps to [28].");

        SetHelp(modShopLabelTextBox, "The label used by carcols.meta and AddTextEntry. Example: BISONHF_LIV29.");
        modShopLabelTextBox.PlaceholderText = "Example: BISONHF_LIV29";

        SetHelp(displayNameTextBox, "The name players see in the mod shop. Example: The Silent.");
        displayNameTextBox.PlaceholderText = "Example: The Silent";

        SetHelp(lockLiveryCheckBox, "Enable this to restrict the livery to a business, gang, or permission suffix.");
        SetHelp(permissionTextBox, "Permission suffix for custom_liveries when Lock livery is enabled. Enter only the suffix, like logs, masks, management, crafting, or warehouse.");
        permissionTextBox.PlaceholderText = "Example: logs, masks, management";

        SetHelp(createBackupsCheckBox, "Off by default. Turn this on if you want timestamped .bak files before carcols.meta or blrp_custom_liveries.lua are edited.");
        SetHelp(updateModkitMasterListCheckBox, "Enable only when adding a new vehicle modkit that needs a line appended to ! modkit master list.txt.");
        SetHelp(modkitEntryTextBox, "Exact modkit line to append if missing. Example: 61570_bl_italigtb2_modkit.");
        modkitEntryTextBox.PlaceholderText = "Example: 61570_bl_italigtb2_modkit";
        SetHelp(blacklistCheckBox, "Enable this when replacing or retiring an existing livery slot. The old slot will be set to blacklisted in custom_liveries.");
        SetHelp(blacklistSlotInput, "The old zero-based Lua slot to mark as blacklisted. Example: 18 for [18] = 'blacklisted'.");
        SetHelp(blacklistCommentTextBox, "Optional comment explaining what the old livery used to be. Example: Former FDF livery.");
        blacklistCommentTextBox.PlaceholderText = "Optional: Old FDF livery";

        SetHelp(newLiveryButton, "Clears the current livery inputs so you can start another livery. Repo, modkit list path, backup setting, and scan results stay in place.");
        SetHelp(applyButton, "Creates/updates the livery: converts the image, writes the YFT from the template, patches carcols.meta, and updates blrp_custom_liveries.lua.");
        SetHelp(scanButton, "Scans existing streamed .yft liveries and groups them by prefix. Double-click a row to fill Prefix, YFT #, Lua slot, and Label.");
        SetHelp(slotsGrid, "Existing streamed livery file groups. Next YFT # is the file suffix; Lua slot is the zero-based custom_liveries index.");
    }

    private void SetHelp(Control control, string help)
    {
        helpTip.SetToolTip(control, help);
        control.Enter += (_, _) => Log(help);
    }

    private void UpdatePathHints()
    {
        var repoRoot = string.IsNullOrWhiteSpace(repoTextBox.Text) ? Paths.DefaultRepoRoot : repoTextBox.Text.Trim();

        repoTextBox.PlaceholderText = Paths.DefaultRepoRoot;
        modkitMasterListTextBox.PlaceholderText = Paths.GetDefaultModkitMasterListPath(repoRoot);
        vehicleDataFolderTextBox.PlaceholderText = Paths.GetDefaultVehicleDataFolder(repoRoot);
        templateYftTextBox.PlaceholderText = Path.Combine(
            Paths.GetDefaultLiveryStreamFolder(repoRoot),
            "bisonhf_livery_28.yft");
        outputDdsTextBox.PlaceholderText = Path.Combine(
            Paths.GetDefaultLiveryStreamFolder(repoRoot),
            "bisonhf_livery_29.dds");
    }

    private static bool IsPathUnder(string candidatePath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootPath))
        {
            return false;
        }

        try
        {
            var candidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return candidate.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                   candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   candidate.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
