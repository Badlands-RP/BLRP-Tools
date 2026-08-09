using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace BLRP.ClothingLocator;

internal sealed class MainForm : Form
{
    private static readonly Color BackgroundTop = Color.FromArgb(12, 12, 28);
    private static readonly Color BackgroundBottom = Color.FromArgb(20, 20, 40);
    private static readonly Color CardTop = Color.FromArgb(20, 20, 40);
    private static readonly Color CardBottom = Color.FromArgb(30, 30, 60);
    private static readonly Color InputBackground = Color.FromArgb(40, 40, 80);
    private static readonly Color Accent = Color.FromArgb(100, 149, 237);
    private static readonly Color AccentLight = Color.FromArgb(135, 206, 235);
    private static readonly Color TextPrimary = Color.White;
    private static readonly Color TextMuted = Color.FromArgb(180, 200, 215, 240);

    private readonly TextBox _rootPath = CreateTextBox();
    private readonly ComboBox _gender = CreateComboBox();
    private readonly ComboBox _component = CreateComboBox();
    private readonly TextBox _customStart = CreateTextBox();
    private readonly NumericUpDown _clothingNumber = CreateNumberInput(0, 10000);
    private readonly TextBox _manualFile = CreateTextBox();
    private readonly DataGridView _results = new();
    private readonly Label _status = CreateLabel("READY", 9, AccentLight, FontStyle.Bold);
    private readonly Label _resultCount = CreateLabel("0 RESULTS", 9, TextMuted, FontStyle.Bold);
    private readonly BlrpProgress _progress = new();
    private readonly Dictionary<(Gender Gender, string Component), int> _offsets = new();
    private readonly Button _extractBaseFiles;
    private readonly Button _openPreview;
    private readonly Button _duplicateIntoCategory;
    private readonly Button _previewOutfit;
    private readonly Button _removeOutfitItem;
    private readonly Button _clearOutfit;
    private readonly ListBox _outfitItems = new();
    private readonly List<ClothingEntry> _outfit = new();

    private ClothingCatalog? _catalog;
    private BaseGameCatalog? _baseGameCatalog;
    private BaseGameClothingEntry? _selectedBaseEntry;

    public MainForm()
    {
        Text = $"BLRP Clothing Utility v{Application.ProductVersion.Split('+')[0]}";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 700);
        ClientSize = new Size(1120, 760);
        BackColor = BackgroundTop;
        ForeColor = TextPrimary;
        Font = PickMonoFont(9F);
        DoubleBuffered = true;

        _extractBaseFiles = CreateButton("EXTRACT MODEL + TEXTURES", ExtractBaseFiles, true);
        _extractBaseFiles.Enabled = false;
        _openPreview = CreateButton("ADD TO OUTFIT", AddSelectedToOutfit, true);
        _openPreview.Enabled = false;
        _duplicateIntoCategory = CreateButton("DUPLICATE INTO CATEGORY", async (_, _) => await DuplicateIntoCategoryAsync(), true);
        _duplicateIntoCategory.Enabled = false;
        _previewOutfit = CreateButton("PREVIEW OUTFIT", PreviewOutfit, true);
        _previewOutfit.Enabled = false;
        _removeOutfitItem = CreateButton("REMOVE", RemoveOutfitItem);
        _removeOutfitItem.Enabled = false;
        _clearOutfit = CreateButton("CLEAR", ClearOutfit);
        _clearOutfit.Enabled = false;

        foreach (ComponentDefinition component in ClothingComponents.All)
        {
            _offsets[(Gender.Male, component.Code)] = component.MaleBaseOffset;
            _offsets[(Gender.Female, component.Code)] = component.FemaleBaseOffset;
        }

        BuildInterface();
        WireEvents();

        _rootPath.Text = Directory.Exists(@"D:\BadlandsRP_EUP") ? @"D:\BadlandsRP_EUP" : string.Empty;
        _gender.Items.AddRange(new object[] { Gender.Male, Gender.Female });
        _component.DataSource = ClothingComponents.All.ToList();
        _component.DisplayMember = nameof(ComponentDefinition.Display);
        _gender.SelectedIndex = 0;
        _component.SelectedIndex = 6;

        Shown += async (_, _) =>
        {
            if (Directory.Exists(_rootPath.Text))
            {
                await ScanAsync();
            }
        };
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(ClientRectangle, BackgroundTop, BackgroundBottom, 135F);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    private void BuildInterface()
    {
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(24, 18, 24, 18),
            ColumnCount = 1,
            RowCount = 5
        };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 184));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        page.Controls.Add(BuildHeader(), 0, 0);
        page.Controls.Add(BuildRootCard(), 0, 1);
        page.Controls.Add(BuildSearchCard(), 0, 2);
        page.Controls.Add(BuildResultsCard(), 0, 3);
        page.Controls.Add(BuildStatusBar(), 0, 4);
        Controls.Add(page);
    }

    private Control BuildHeader()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(4, 0, 0, 4)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var mark = CreateLabel("◆", 23, AccentLight, FontStyle.Bold);
        mark.Font = new Font("Segoe UI Symbol", 20F, FontStyle.Bold, GraphicsUnit.Point);
        mark.AutoEllipsis = false;
        mark.TextAlign = ContentAlignment.MiddleCenter;
        layout.Controls.Add(mark, 0, 0);
        layout.SetRowSpan(mark, 2);
        layout.Controls.Add(CreateLabel("CLOTHING UTILITY", 18, TextPrimary, FontStyle.Bold), 1, 0);
        layout.Controls.Add(CreateLabel("BADLANDSRP  /  LOCATE  •  IMPORT  •  DUPLICATE  •  PREVIEW", 8, TextMuted, FontStyle.Bold), 1, 1);
        return layout;
    }

    private Control BuildRootCard()
    {
        var card = new BlrpCard(CardTop, CardBottom, Accent) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(16, 12, 16, 12) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 5, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 164));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = CreateLabel("EUP SOURCE DIRECTORY", 8, AccentLight, FontStyle.Bold);
        layout.Controls.Add(label, 0, 0);
        layout.SetColumnSpan(label, 5);
        _rootPath.Dock = DockStyle.Fill;
        layout.Controls.Add(_rootPath, 0, 1);
        layout.Controls.Add(CreateButton("BROWSE", BrowseRoot), 1, 1);
        layout.Controls.Add(CreateButton("SCAN", async (_, _) => await ScanAsync(), true), 2, 1);
        layout.Controls.Add(CreateButton("IMPORT MODEL...", async (_, _) => await ImportModelAsync(), true), 3, 1);
        layout.Controls.Add(CreateButton("IMPORT TEXTURE...", async (_, _) => await ImportTextureAsync(), true), 4, 1);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildSearchCard()
    {
        var card = new BlrpCard(CardTop, CardBottom, Accent) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(16, 12, 16, 12) };
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 1 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        split.Controls.Add(BuildNumberSearch(), 0, 0);
        split.Controls.Add(BuildFileSearch(), 1, 0);
        card.Controls.Add(split);
        return card;
    }

    private Control BuildNumberSearch()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(0, 0, 16, 0), ColumnCount = 4, RowCount = 4 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = CreateLabel("NUMBER  →  FILE", 10, TextPrimary, FontStyle.Bold);
        panel.Controls.Add(heading, 0, 0);
        panel.SetColumnSpan(heading, 4);
        panel.Controls.Add(CreateLabel("GENDER", 8, TextMuted, FontStyle.Bold), 0, 1);
        panel.Controls.Add(CreateLabel("COMPONENT", 8, TextMuted, FontStyle.Bold), 1, 1);
        panel.Controls.Add(CreateLabel("AUTO START", 8, TextMuted, FontStyle.Bold), 2, 1);
        panel.Controls.Add(CreateLabel("CLOTHING #", 8, TextMuted, FontStyle.Bold), 3, 1);
        _gender.Dock = DockStyle.Fill;
        _component.Dock = DockStyle.Fill;
        _customStart.Dock = DockStyle.Fill;
        _customStart.ReadOnly = true;
        _customStart.TabStop = false;
        _customStart.TextAlign = HorizontalAlignment.Center;
        _clothingNumber.Dock = DockStyle.Fill;
        panel.Controls.Add(_gender, 0, 2);
        panel.Controls.Add(_component, 1, 2);
        panel.Controls.Add(_customStart, 2, 2);
        panel.Controls.Add(_clothingNumber, 3, 2);
        var locate = CreateButton("LOCATE CLOTHING NUMBER", async (_, _) => await LocateNumberAsync(), true);
        locate.Dock = DockStyle.Top;
        locate.Height = 36;
        panel.Controls.Add(locate, 0, 3);
        panel.SetColumnSpan(locate, 4);
        return panel;
    }

    private Control BuildFileSearch()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(16, 0, 0, 0), ColumnCount = 2, RowCount = 4 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = CreateLabel("YDD FILE  →  NUMBER", 10, TextPrimary, FontStyle.Bold);
        panel.Controls.Add(heading, 0, 0);
        panel.SetColumnSpan(heading, 2);
        var hint = CreateLabel("SELECT COMPILED MODEL", 8, TextMuted, FontStyle.Bold);
        panel.Controls.Add(hint, 0, 1);
        panel.SetColumnSpan(hint, 2);
        _manualFile.Dock = DockStyle.Fill;
        _manualFile.ReadOnly = true;
        panel.Controls.Add(_manualFile, 0, 2);
        panel.Controls.Add(CreateButton("YDD...", BrowseYdd), 1, 2);
        var locate = CreateButton("FIND ALL MATCHING NUMBERS", async (_, _) => await LocateDuplicatesAsync(), true);
        locate.Dock = DockStyle.Top;
        locate.Height = 36;
        panel.Controls.Add(locate, 0, 3);
        panel.SetColumnSpan(locate, 2);
        return panel;
    }

    private Control BuildResultsCard()
    {
        var card = new BlrpCard(CardTop, CardBottom, Accent) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(12) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 5, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        layout.Controls.Add(CreateLabel("RESULTS", 10, TextPrimary, FontStyle.Bold), 0, 0);
        _duplicateIntoCategory.Margin = new Padding(4, 1, 4, 5);
        layout.Controls.Add(_duplicateIntoCategory, 1, 0);
        _openPreview.Margin = new Padding(4, 1, 4, 5);
        layout.Controls.Add(_openPreview, 2, 0);
        _extractBaseFiles.Margin = new Padding(4, 1, 4, 5);
        layout.Controls.Add(_extractBaseFiles, 3, 0);
        _resultCount.Dock = DockStyle.Fill;
        _resultCount.TextAlign = ContentAlignment.MiddleRight;
        layout.Controls.Add(_resultCount, 4, 0);

        ConfigureGrid();
        layout.Controls.Add(_results, 0, 1);
        layout.SetColumnSpan(_results, 5);
        Control outfitBar = BuildOutfitBar();
        layout.Controls.Add(outfitBar, 0, 2);
        layout.SetColumnSpan(outfitBar, 5);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildOutfitBar()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 4,
            RowCount = 2,
            Padding = new Padding(0, 6, 0, 0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var heading = CreateLabel("OUTFIT  /  ADD ONE ITEM PER CLOTHING SLOT", 8, AccentLight, FontStyle.Bold);
        layout.Controls.Add(heading, 0, 0);
        layout.SetColumnSpan(heading, 4);

        _outfitItems.Dock = DockStyle.Fill;
        _outfitItems.BackColor = Color.FromArgb(20, 20, 40);
        _outfitItems.ForeColor = TextPrimary;
        _outfitItems.BorderStyle = BorderStyle.FixedSingle;
        _outfitItems.Font = PickMonoFont(8F);
        _outfitItems.IntegralHeight = false;
        _outfitItems.HorizontalScrollbar = true;
        _outfitItems.SelectedIndexChanged += (_, _) => _removeOutfitItem.Enabled = _outfitItems.SelectedIndex >= 0;
        layout.Controls.Add(_outfitItems, 0, 1);
        layout.Controls.Add(_previewOutfit, 1, 1);
        layout.Controls.Add(_removeOutfitItem, 2, 1);
        layout.Controls.Add(_clearOutfit, 3, 1);
        return layout;
    }

    private Control BuildStatusBar()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _progress.Dock = DockStyle.Fill;
        _progress.Margin = new Padding(0, 11, 0, 11);
        layout.Controls.Add(_status, 0, 0);
        layout.Controls.Add(_progress, 1, 0);
        return layout;
    }

    private void ConfigureGrid()
    {
        _results.Dock = DockStyle.Fill;
        _results.BackgroundColor = Color.FromArgb(14, 14, 30);
        _results.BorderStyle = BorderStyle.None;
        _results.EnableHeadersVisualStyles = false;
        _results.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = InputBackground,
            ForeColor = AccentLight,
            Font = PickMonoFont(8F, FontStyle.Bold),
            SelectionBackColor = InputBackground,
            Alignment = DataGridViewContentAlignment.MiddleLeft
        };
        _results.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(20, 20, 40),
            ForeColor = TextPrimary,
            SelectionBackColor = Color.FromArgb(65, 105, 180),
            SelectionForeColor = Color.White,
            Font = PickMonoFont(8.5F)
        };
        _results.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(25, 25, 50);
        _results.GridColor = Color.FromArgb(65, 80, 130);
        _results.RowHeadersVisible = false;
        _results.AllowUserToAddRows = false;
        _results.AllowUserToDeleteRows = false;
        _results.AllowUserToResizeRows = false;
        _results.ReadOnly = true;
        _results.MultiSelect = false;
        _results.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _results.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _results.RowTemplate.Height = 28;

        _results.Columns.Add("Gender", "GENDER");
        _results.Columns.Add("Slot", "SLOT");
        _results.Columns.Add("Component", "COMPONENT");
        _results.Columns.Add("Global", "CLOTHING #");
        _results.Columns.Add("Pack", "PACK");
        _results.Columns.Add("Relative", "RELATIVE");
        _results.Columns.Add("Textures", "TEXTURES");
        _results.Columns.Add("File", "FILE");
        _results.Columns[0].Width = 76;
        _results.Columns[1].Width = 76;
        _results.Columns[2].Width = 150;
        _results.Columns[3].Width = 98;
        _results.Columns[4].Width = 56;
        _results.Columns[5].Width = 76;
        _results.Columns[6].Width = 76;
        _results.Columns[7].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
    }

    private void WireEvents()
    {
        _gender.SelectedIndexChanged += (_, _) => SelectionChanged();
        _component.SelectedIndexChanged += (_, _) => SelectionChanged();
        _results.SelectionChanged += (_, _) => UpdateResultActions();
    }

    private async Task ScanAsync()
    {
        string root = _rootPath.Text.Trim();
        if (root.Length == 0)
        {
            ShowError("Select the BadlandsRP_EUP directory first.");
            return;
        }

        SetBusy(true, "SCANNING COMPILED CLOTHING...");
        try
        {
            _catalog = await ClothingCatalog.LoadAsync(root);
            SetStatus($"READY  /  {_catalog.FileCount:N0} COMPILED YDD FILES INDEXED", false);
        }
        catch (Exception exception)
        {
            _catalog = null;
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ImportModelAsync()
    {
        string root = _rootPath.Text.Trim();
        if (!Directory.Exists(root))
        {
            ShowError("Select a valid BadlandsRP_EUP directory first.");
            return;
        }
        if (SelectedComponent is not { } component)
        {
            ShowError("Select a clothing component first.");
            return;
        }
        if (component.IsProp)
        {
            ShowError("Prop import is not supported yet. Select a component model.");
            return;
        }

        using var modelDialog = new OpenFileDialog
        {
            Title = $"Select the new {SelectedGender.ToString().ToLowerInvariant()} {component.Code.ToUpperInvariant()} model",
            Filter = "Compiled model (*.ydd;*.ydr)|*.ydd;*.ydr",
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(root) ? root : @"D:\"
        };
        if (modelDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        using var textureDialog = new OpenFileDialog
        {
            Title = "Select every YTD texture for this model (race/variant names are detected automatically)",
            Filter = "Texture dictionaries (*.ytd)|*.ytd",
            CheckFileExists = true,
            Multiselect = true,
            InitialDirectory = Path.GetDirectoryName(modelDialog.FileName)
        };
        if (textureDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        string sourceModelName = Path.GetFileNameWithoutExtension(modelDialog.FileName);
        bool hasSkin;
        if (sourceModelName.EndsWith("_r", StringComparison.OrdinalIgnoreCase))
        {
            hasSkin = true;
        }
        else if (sourceModelName.EndsWith("_u", StringComparison.OrdinalIgnoreCase))
        {
            hasSkin = false;
        }
        else
        {
            DialogResult skinChoice = MessageBox.Show(
                this,
                "Does this model expose player skin?\n\nYES = _r model (skin/race textures)\nNO = _u model (universal texture)",
                "Model texture type",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            if (skinChoice == DialogResult.Cancel)
            {
                return;
            }
            hasSkin = skinChoice == DialogResult.Yes;
        }

        try
        {
            IReadOnlyList<ClothingImportPlan> plans = ClothingImporter.CreatePlans(
                root,
                SelectedGender,
                component,
                modelDialog.FileName,
                textureDialog.FileNames,
                hasSkin);
            using var targetDialog = new ImportTargetDialog(plans);
            if (targetDialog.ShowDialog(this) != DialogResult.OK || targetDialog.SelectedPlan is not { } plan)
            {
                return;
            }

            SetBusy(true, "IMPORTING MODEL, TEXTURES AND YMT ENTRY...");
            await Task.Run(() => ClothingImporter.Import(plan));
            _catalog = await ClothingCatalog.LoadAsync(root);
            _clothingNumber.Value = Math.Min(_clothingNumber.Maximum, plan.GlobalIndex);
            ClothingEntry? imported = _catalog.FindByGlobalIndex(
                plan.Gender,
                plan.Component,
                plan.GlobalIndex,
                plan.Component.DefaultOffset(plan.Gender));
            ShowResults(imported == null ? [] : [imported]);
            SetStatus(
                $"IMPORTED CLOTHING #{plan.GlobalIndex} / ADDON {plan.Pack} / {plan.RemainingSlots} YMT SLOTS REMAIN",
                plan.CountAfterImport >= 120);
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ImportTextureAsync()
    {
        string root = _rootPath.Text.Trim();
        if (!Directory.Exists(root))
        {
            ShowError("Select a valid BadlandsRP_EUP directory first.");
            return;
        }

        ClothingEntry? target = _results.SelectedRows.Count == 1
            ? _results.SelectedRows[0].Tag as ClothingEntry
            : null;
        if (target == null)
        {
            if (SelectedComponent is not { IsProp: false } component)
            {
                ShowError("Select a component clothing model first.");
                return;
            }
            if (_catalog == null || !Path.GetFullPath(root).Equals(_catalog.RootPath, StringComparison.OrdinalIgnoreCase))
            {
                _catalog = await ClothingCatalog.LoadAsync(root);
            }
            target = _catalog.FindByGlobalIndex(
                SelectedGender,
                component,
                decimal.ToInt32(_clothingNumber.Value),
                component.DefaultOffset(SelectedGender));
        }
        if (target == null)
        {
            ShowError("The entered clothing number is not a custom EUP model. Locate or select a custom YDD first.");
            return;
        }

        using var textureDialog = new OpenFileDialog
        {
            Title = $"Select the new texture for {Path.GetFileName(target.FilePath)}",
            Filter = "Texture dictionaries (*.ytd)|*.ytd",
            CheckFileExists = true,
            InitialDirectory = Path.GetDirectoryName(target.FilePath)
        };
        if (textureDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            int globalIndex = _catalog?.GetGlobalIndex(
                target,
                target.Component.DefaultOffset(target.Gender)) ?? target.RelativeIndex;
            SetBusy(true, "IMPORTING TEXTURE AND UPDATING YMT...");
            ClothingTextureImportResult result = await Task.Run(() =>
                ClothingImporter.ImportTexture(root, target, textureDialog.FileName));
            _catalog = await ClothingCatalog.LoadAsync(root);
            ClothingEntry? refreshed = _catalog.FindByGlobalIndex(
                target.Gender,
                target.Component,
                globalIndex,
                target.Component.DefaultOffset(target.Gender));
            ShowResults(refreshed == null ? [] : [refreshed]);
            SetStatus(
                $"IMPORTED {Path.GetFileName(result.TexturePath)} / {result.TextureCount} TEXTURES NOW AVAILABLE",
                false);
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DuplicateIntoCategoryAsync()
    {
        if (_catalog == null || _results.SelectedRows.Count != 1 ||
            _results.SelectedRows[0].Tag is not ClothingEntry source)
        {
            ShowError("Select a custom clothing result first.");
            return;
        }

        ComponentDefinition? targetComponent = ChooseDuplicateCategory(source);
        if (targetComponent == null)
        {
            return;
        }

        try
        {
            IReadOnlyList<ClothingImportPlan> plans = ClothingImporter.CreateDuplicatePlans(
                _catalog.RootPath,
                source,
                targetComponent);
            using var targetDialog = new ImportTargetDialog(plans);
            if (targetDialog.ShowDialog(this) != DialogResult.OK || targetDialog.SelectedPlan is not { } plan)
            {
                return;
            }

            SetBusy(true, $"DUPLICATING {source.Component.Code.ToUpperInvariant()} INTO {targetComponent.Code.ToUpperInvariant()}...");
            await Task.Run(() => ClothingImporter.Import(plan));
            _catalog = await ClothingCatalog.LoadAsync(plan.RootPath);
            _gender.SelectedItem = plan.Gender;
            _component.SelectedItem = targetComponent;
            _clothingNumber.Value = Math.Min(_clothingNumber.Maximum, plan.GlobalIndex);
            ClothingEntry? imported = _catalog.FindByGlobalIndex(
                plan.Gender,
                targetComponent,
                plan.GlobalIndex,
                targetComponent.DefaultOffset(plan.Gender));
            ShowResults(imported == null ? [] : [imported]);
            SetStatus(
                $"DUPLICATED INTO {targetComponent.Code.ToUpperInvariant()} #{plan.GlobalIndex} / ADDON {plan.Pack} / {plan.RemainingSlots} YMT SLOTS REMAIN",
                plan.CountAfterImport >= 120);
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private ComponentDefinition? ChooseDuplicateCategory(ClothingEntry source)
    {
        var choices = ClothingComponents.All
            .Where(component => !component.IsProp &&
                !component.Code.Equals(source.Component.Code, StringComparison.OrdinalIgnoreCase))
            .ToList();
        using var dialog = new Form
        {
            Text = "Duplicate into category",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(520, 170),
            BackColor = BackgroundTop,
            ForeColor = TextPrimary,
            Font = Font,
            Padding = new Padding(20)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(CreateLabel(
            $"COPY {source.Component.Code.ToUpperInvariant()} TO:",
            11,
            TextPrimary,
            FontStyle.Bold), 0, 0);
        ComboBox category = CreateComboBox();
        category.Dock = DockStyle.Fill;
        category.DataSource = choices;
        category.DisplayMember = nameof(ComponentDefinition.Display);
        layout.Controls.Add(category, 0, 1);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        Button duplicate = CreateButton("OK", (_, _) => dialog.DialogResult = DialogResult.OK, true);
        Button cancel = CreateButton("CANCEL", (_, _) => dialog.DialogResult = DialogResult.Cancel);
        buttons.Controls.Add(cancel, 1, 0);
        buttons.Controls.Add(duplicate, 2, 0);
        layout.Controls.Add(buttons, 0, 2);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = duplicate;
        dialog.CancelButton = cancel;

        return dialog.ShowDialog(this) == DialogResult.OK
            ? category.SelectedItem as ComponentDefinition
            : null;
    }

    private async Task LocateNumberAsync()
    {
        if (!EnsureCatalog() || SelectedComponent is not { } component)
        {
            return;
        }

        int index = (int)_clothingNumber.Value;
        int baseOffset = _offsets[(SelectedGender, component.Code)];
        if (index < baseOffset)
        {
            if (component.IsProp)
            {
                ShowResults(Array.Empty<ClothingEntry>());
                SetStatus("BASE-GAME PROP EXTRACTION IS NOT YET SUPPORTED", true);
                return;
            }

            SetBusy(true, "INDEXING BASE-GAME/DLC CLOTHING WITH CODEWALKER...");
            try
            {
                _baseGameCatalog ??= await BaseGameCatalog.LoadAsync();
                BaseGameClothingEntry? baseMatch = _baseGameCatalog.Find(SelectedGender, component, index);
                if (baseMatch == null)
                {
                    ShowResults(Array.Empty<ClothingEntry>());
                    SetStatus($"NO BASE-GAME MODEL FOUND FOR CLOTHING NUMBER {index}", true);
                    return;
                }

                ShowBaseResult(baseMatch);
                SetStatus(
                    $"FOUND ROCKSTAR ITEM / FIVEM BUILD {_baseGameCatalog.Installation.GameBuild} / {_baseGameCatalog.Installation.DlcName}",
                    false);
            }
            catch (Exception exception)
            {
                ShowError(exception.Message);
            }
            finally
            {
                SetBusy(false);
            }
            return;
        }

        ClothingEntry? match = _catalog!.FindByGlobalIndex(SelectedGender, component, index, baseOffset);
        if (match is null)
        {
            ShowResults(Array.Empty<ClothingEntry>());
            SetStatus($"NO COMPILED FILE FOUND FOR CLOTHING NUMBER {index}", true);
            return;
        }

        ShowResults(new[] { match });
        SetStatus($"FOUND CLOTHING NUMBER {index}", false);
    }

    private async Task LocateDuplicatesAsync()
    {
        if (!EnsureCatalog())
        {
            return;
        }

        string filePath = _manualFile.Text.Trim();
        if (!File.Exists(filePath))
        {
            ShowError("Select a compiled .ydd file first.");
            return;
        }

        SetBusy(true, "HASHING SAME-SIZE CANDIDATES...");
        try
        {
            IReadOnlyList<ClothingEntry> matches = await _catalog!.FindDuplicatesAsync(filePath);
            ShowResults(matches);
            SetStatus(matches.Count == 0 ? "NO MATCHES FOUND" : $"{matches.Count} MATCH{(matches.Count == 1 ? string.Empty : "ES")} FOUND", matches.Count == 0);
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowResults(IEnumerable<ClothingEntry> entries)
    {
        _selectedBaseEntry = null;
        _extractBaseFiles.Enabled = false;
        _openPreview.Enabled = false;
        _duplicateIntoCategory.Enabled = false;
        _results.Rows.Clear();
        foreach (ClothingEntry entry in entries)
        {
            int baseOffset = _offsets[(entry.Gender, entry.Component.Code)];
            int globalIndex = _catalog!.GetGlobalIndex(entry, baseOffset);
            string slot = entry.Component.IsProp ? $"PROP {entry.Component.Slot}" : entry.Component.Slot.ToString();
            string relativePath = Path.GetRelativePath(_catalog.RootPath, entry.FilePath);
            int rowIndex = _results.Rows.Add(
                entry.Gender.ToString().ToUpperInvariant(),
                slot,
                entry.Component.Code.ToUpperInvariant(),
                globalIndex,
                entry.Pack,
                entry.RelativeIndex,
                entry.TextureCount,
                relativePath);
            _results.Rows[rowIndex].Cells[7].ToolTipText = entry.FilePath;
            _results.Rows[rowIndex].Tag = entry;
        }

        _resultCount.Text = $"{_results.Rows.Count} RESULT{(_results.Rows.Count == 1 ? string.Empty : "S")}";
        UpdateResultActions();
    }

    private void ShowBaseResult(BaseGameClothingEntry entry)
    {
        _selectedBaseEntry = entry;
        _extractBaseFiles.Enabled = true;
        _openPreview.Enabled = false;
        _duplicateIntoCategory.Enabled = false;
        _results.Rows.Clear();
        string slot = entry.Component.IsProp ? $"PROP {entry.Component.Slot}" : entry.Component.Slot.ToString();
        string fileSummary = entry.ModelArchivePath +
            (entry.TextureArchivePaths.Count == 0 ? string.Empty : $"  +  {entry.TextureArchivePaths.Count} YTD");
        int rowIndex = _results.Rows.Add(
            entry.Gender.ToString().ToUpperInvariant(),
            slot,
            entry.Component.Code.ToUpperInvariant(),
            entry.GlobalIndex,
            "R*",
            entry.RelativeIndex,
            entry.TextureArchivePaths.Count,
            fileSummary);
        _results.Rows[rowIndex].Cells[7].ToolTipText = string.Join(
            Environment.NewLine,
            new[] { entry.ModelArchivePath }.Concat(entry.TextureArchivePaths));
        _resultCount.Text = "1 RESULT";
    }

    private void UpdateResultActions()
    {
        ClothingEntry? entry = _results.SelectedRows.Count == 1
            ? _results.SelectedRows[0].Tag as ClothingEntry
            : null;
        _openPreview.Enabled = entry != null && File.Exists(entry.FilePath);
        _duplicateIntoCategory.Enabled = entry is { Component.IsProp: false };
    }

    private void AddSelectedToOutfit(object? sender, EventArgs e)
    {
        if (_results.SelectedRows.Count != 1 || _results.SelectedRows[0].Tag is not ClothingEntry entry || !File.Exists(entry.FilePath))
        {
            ShowError("Select a custom clothing model result first.");
            return;
        }

        if (_outfit.Count > 0 && _outfit[0].Gender != entry.Gender)
        {
            ShowError($"The outfit already contains {_outfit[0].Gender.ToString().ToLowerInvariant()} items. Clear it before adding {entry.Gender.ToString().ToLowerInvariant()} clothing.");
            return;
        }

        bool replaced = AddOrReplaceOutfitItem(_outfit, entry);
        RefreshOutfit();
        SetStatus(
            replaced
                ? $"REPLACED {entry.Component.Code.ToUpperInvariant()} IN OUTFIT"
                : $"ADDED {entry.Component.Code.ToUpperInvariant()} TO OUTFIT",
            false);
    }

    private static bool AddOrReplaceOutfitItem(List<ClothingEntry> outfit, ClothingEntry entry)
    {
        int existingIndex = outfit.FindIndex(item =>
            item.Component.Slot == entry.Component.Slot &&
            item.Component.IsProp == entry.Component.IsProp);
        if (existingIndex < 0)
        {
            outfit.Add(entry);
            return false;
        }

        outfit[existingIndex] = entry;
        return true;
    }

    private void RemoveOutfitItem(object? sender, EventArgs e)
    {
        int index = _outfitItems.SelectedIndex;
        if (index < 0 || index >= _outfit.Count)
        {
            return;
        }

        _outfit.RemoveAt(index);
        RefreshOutfit();
        SetStatus("REMOVED ITEM FROM OUTFIT", false);
    }

    private void ClearOutfit(object? sender, EventArgs e)
    {
        _outfit.Clear();
        RefreshOutfit();
        SetStatus("OUTFIT CLEARED", false);
    }

    private void RefreshOutfit()
    {
        _outfitItems.Items.Clear();
        foreach (ClothingEntry entry in _outfit)
        {
            int globalIndex = _catalog?.GetGlobalIndex(entry, _offsets[(entry.Gender, entry.Component.Code)]) ?? entry.RelativeIndex;
            string slot = entry.Component.IsProp ? $"PROP {entry.Component.Slot}" : $"COMP {entry.Component.Slot}";
            _outfitItems.Items.Add($"{slot,-7}  {entry.Component.Code.ToUpperInvariant(),-8}  #{globalIndex,-4}  {Path.GetFileName(entry.FilePath)}");
        }

        _previewOutfit.Text = $"PREVIEW OUTFIT ({_outfit.Count})";
        _previewOutfit.Enabled = _outfit.Count > 0;
        _clearOutfit.Enabled = _outfit.Count > 0;
        _removeOutfitItem.Enabled = false;
    }

    private void PreviewOutfit(object? sender, EventArgs e)
    {
        string[] modelPaths = _outfit.Select(entry => entry.FilePath).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (modelPaths.Length == 0)
        {
            ShowError("Add at least one custom clothing model to the outfit first.");
            return;
        }

        string gender = _outfit[0].Gender.ToString().ToLowerInvariant();

        string previewExe = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "tools",
            "grzyClothTool-outfit",
            "grzyClothTool.exe"));
        if (!File.Exists(previewExe))
        {
            ShowError("The grzyClothTool preview helper is missing from the tools folder.");
            return;
        }

        if (TryReusePreview(previewExe, modelPaths, gender))
        {
            SetStatus($"PREVIEWING {_outfit.Count} OUTFIT ITEM{(_outfit.Count == 1 ? string.Empty : "S")} IN GRZYCLOTHTOOL", false);
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = previewExe,
            WorkingDirectory = Path.GetDirectoryName(previewExe)!,
            UseShellExecute = true
        };
        foreach (string modelPath in modelPaths)
        {
            startInfo.ArgumentList.Add("--preview");
            startInfo.ArgumentList.Add(modelPath);
        }
        startInfo.ArgumentList.Add("--gender");
        startInfo.ArgumentList.Add(gender);
        Process.Start(startInfo);
        SetStatus($"OUTFIT PREVIEW STARTED WITH {modelPaths.Length} ITEM{(modelPaths.Length == 1 ? string.Empty : "S")} / COMPLETE GRZYCLOTHTOOL SETUP IF PROMPTED", false);
    }

    private static bool TryReusePreview(string previewExe, IReadOnlyList<string> modelPaths, string gender)
    {
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(previewExe)))
        {
            using (process)
            {
                try
                {
                    if (!string.Equals(process.MainModule?.FileName, previewExe, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    using var pipe = new NamedPipeClientStream(
                        ".",
                        $"BLRP-grzyClothTool-outfit-{process.Id}",
                        PipeDirection.Out);
                    pipe.Connect(500);
                    using var writer = new BinaryWriter(pipe, Encoding.UTF8, leaveOpen: true);
                    writer.Write(gender);
                    writer.Write(modelPaths.Count);
                    foreach (string modelPath in modelPaths)
                    {
                        writer.Write(modelPath);
                    }
                    writer.Flush();
                    return true;
                }
                catch
                {
                    // An older or still-starting helper has no outfit pipe; launch the updated helper below.
                }
            }
        }

        return false;
    }

    private void ExtractBaseFiles(object? sender, EventArgs e)
    {
        if (_selectedBaseEntry is not { } entry || _baseGameCatalog == null)
        {
            return;
        }

        string defaultRoot = @"D:\BLRP-Clothing-Exports";
        Directory.CreateDirectory(defaultRoot);
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where to extract the Rockstar YDD/YTD files",
            UseDescriptionForTitle = true,
            SelectedPath = defaultRoot
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        string safeCollection = string.Concat(entry.Collection.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        string outputDirectory = Path.Combine(
            dialog.SelectedPath,
            $"{entry.Gender.ToString().ToLowerInvariant()}_{entry.Component.Code}_{entry.GlobalIndex}_{safeCollection}");
        try
        {
            IReadOnlyList<string> files = _baseGameCatalog.Extract(entry, outputDirectory);
            SetStatus($"EXTRACTED {files.Count} FILE{(files.Count == 1 ? string.Empty : "S")} TO {outputDirectory}", false);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = outputDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private void SelectionChanged()
    {
        if (SelectedComponent is not { } component)
        {
            return;
        }

        _customStart.Text = _offsets[(SelectedGender, component.Code)].ToString();
    }

    private void BrowseRoot(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the BadlandsRP_EUP directory",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_rootPath.Text) ? _rootPath.Text : @"D:\"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _rootPath.Text = dialog.SelectedPath;
        }
    }

    private void BrowseYdd(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select compiled clothing model",
            Filter = "Compiled clothing (*.ydd)|*.ydd",
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(_rootPath.Text) ? _rootPath.Text : @"D:\"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _manualFile.Text = dialog.FileName;
        }
    }

    private bool EnsureCatalog()
    {
        if (_catalog != null)
        {
            return true;
        }

        ShowError("Scan the EUP source directory first.");
        return false;
    }

    private void SetBusy(bool busy, string? text = null)
    {
        SetWaitCursor(this, busy);
        Cursor.Current = busy ? Cursors.WaitCursor : Cursors.Default;
        _progress.Active = busy;
        if (text != null)
        {
            SetStatus(text, false);
        }
    }

    private static void SetWaitCursor(Control control, bool busy)
    {
        control.UseWaitCursor = busy;
        foreach (Control child in control.Controls)
        {
            SetWaitCursor(child, busy);
        }
    }

    internal static bool SelfTest()
    {
        using var form = new MainForm();
        form.SetBusy(true);
        form.SetBusy(false);
        var outfit = new List<ClothingEntry>();
        ComponentDefinition top = ClothingComponents.ByCode["jbib"];
        ComponentDefinition shoes = ClothingComponents.ByCode["feet"];
        var firstTop = new ClothingEntry("first.ydd", 1, Gender.Male, top, 1, 0);
        var replacementTop = new ClothingEntry("replacement.ydd", 1, Gender.Male, top, 1, 1);
        AddOrReplaceOutfitItem(outfit, firstTop);
        bool replaced = AddOrReplaceOutfitItem(outfit, replacementTop);
        AddOrReplaceOutfitItem(outfit, new ClothingEntry("shoes.ydd", 1, Gender.Male, shoes, 1, 0));
        return form._customStart.ReadOnly &&
               form._customStart.Text == "178" &&
               form._duplicateIntoCategory.Text == "DUPLICATE INTO CATEGORY" &&
               form._openPreview.Text == "ADD TO OUTFIT" &&
               replaced &&
               outfit.Count == 2 &&
               outfit[0] == replacementTop &&
               !HasWaitCursor(form);
    }

    private static bool HasWaitCursor(Control control) =>
        control.UseWaitCursor || control.Controls.Cast<Control>().Any(HasWaitCursor);

    private void SetStatus(string text, bool warning)
    {
        _status.Text = text;
        _status.ForeColor = warning ? Color.FromArgb(255, 180, 50) : AccentLight;
    }

    private void ShowError(string message)
    {
        SetStatus(message.ToUpperInvariant(), true);
        MessageBox.Show(this, message, "BLRP Clothing Utility", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private Gender SelectedGender => _gender.SelectedItem is Gender gender ? gender : Gender.Male;
    private ComponentDefinition? SelectedComponent => _component.SelectedItem as ComponentDefinition;

    private static TextBox CreateTextBox()
    {
        return new TextBox
        {
            BackColor = InputBackground,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = PickMonoFont(9F),
            Margin = new Padding(0, 2, 8, 2)
        };
    }

    private static ComboBox CreateComboBox()
    {
        return new ComboBox
        {
            BackColor = InputBackground,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = PickMonoFont(8.5F),
            Margin = new Padding(0, 2, 8, 2)
        };
    }

    private static NumericUpDown CreateNumberInput(int minimum, int maximum)
    {
        return new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            BackColor = InputBackground,
            ForeColor = TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = PickMonoFont(9F, FontStyle.Bold),
            TextAlign = HorizontalAlignment.Center,
            Margin = new Padding(0, 2, 8, 2)
        };
    }

    private static Label CreateLabel(string text, float size, Color color, FontStyle style = FontStyle.Regular)
    {
        return new Label
        {
            Text = text,
            ForeColor = color,
            BackColor = Color.Transparent,
            Font = PickMonoFont(size, style),
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static Button CreateButton(string text, EventHandler click, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            BackColor = primary ? Accent : InputBackground,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = PickMonoFont(8F, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 2, 0, 2),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = primary ? AccentLight : Color.FromArgb(90, 100, 149, 237);
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(80, 130, 220) : Color.FromArgb(55, 55, 100);
        button.Click += click;
        return button;
    }

    private static Font PickMonoFont(float size, FontStyle style = FontStyle.Regular)
    {
        return new Font("Cascadia Mono", size, style, GraphicsUnit.Point);
    }
}

internal sealed class BlrpCard : Panel
{
    private readonly Color _top;
    private readonly Color _bottom;
    private readonly Color _border;

    public BlrpCard(Color top, Color bottom, Color border)
    {
        _top = top;
        _bottom = bottom;
        _border = border;
        BackColor = Color.Transparent;
        DoubleBuffered = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = new(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        using GraphicsPath path = RoundedRectangle(bounds, 12);
        using var brush = new LinearGradientBrush(bounds, _top, _bottom, 135F);
        using var pen = new Pen(Color.FromArgb(155, _border), 2F);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        int diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class BlrpProgress : Control
{
    private readonly System.Windows.Forms.Timer _timer;
    private int _position;
    private bool _active;

    public BlrpProgress()
    {
        DoubleBuffered = true;
        _timer = new System.Windows.Forms.Timer { Interval = 35 };
        _timer.Tick += (_, _) =>
        {
            _position = (_position + 12) % Math.Max(1, Width + 80);
            Invalidate();
        };
    }

    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            if (value) _timer.Start(); else _timer.Stop();
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(Color.FromArgb(80, 60, 60, 100));
        using var fill = new LinearGradientBrush(ClientRectangle, Color.FromArgb(100, 149, 237), Color.FromArgb(135, 206, 235), 0F);
        e.Graphics.FillRectangle(background, ClientRectangle);
        if (_active)
        {
            e.Graphics.FillRectangle(fill, new Rectangle(_position - 80, 0, 80, Height));
        }
    }
}
