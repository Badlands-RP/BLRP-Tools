using System.Drawing.Drawing2D;

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

    private ClothingCatalog? _catalog;
    private BaseGameCatalog? _baseGameCatalog;
    private BaseGameClothingEntry? _selectedBaseEntry;

    public MainForm()
    {
        Text = "BLRP Clothing Locator";
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
        layout.Controls.Add(CreateLabel("CLOTHING LOCATOR", 18, TextPrimary, FontStyle.Bold), 1, 0);
        layout.Controls.Add(CreateLabel("BADLANDSRP  /  COLLECTION INDEX & DUPLICATE FINDER", 8, TextMuted, FontStyle.Bold), 1, 1);
        return layout;
    }

    private Control BuildRootCard()
    {
        var card = new BlrpCard(CardTop, CardBottom, Accent) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(16, 12, 16, 12) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 3, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = CreateLabel("EUP SOURCE DIRECTORY", 8, AccentLight, FontStyle.Bold);
        layout.Controls.Add(label, 0, 0);
        layout.SetColumnSpan(label, 3);
        _rootPath.Dock = DockStyle.Fill;
        layout.Controls.Add(_rootPath, 0, 1);
        layout.Controls.Add(CreateButton("BROWSE", BrowseRoot), 1, 1);
        layout.Controls.Add(CreateButton("SCAN", async (_, _) => await ScanAsync(), true), 2, 1);
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
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 3, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(CreateLabel("RESULTS", 10, TextPrimary, FontStyle.Bold), 0, 0);
        _extractBaseFiles.Margin = new Padding(4, 1, 4, 5);
        layout.Controls.Add(_extractBaseFiles, 1, 0);
        _resultCount.Dock = DockStyle.Fill;
        _resultCount.TextAlign = ContentAlignment.MiddleRight;
        layout.Controls.Add(_resultCount, 2, 0);

        ConfigureGrid();
        layout.Controls.Add(_results, 0, 1);
        layout.SetColumnSpan(_results, 3);
        card.Controls.Add(layout);
        return card;
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
        _results.Columns.Add("File", "FILE");
        _results.Columns[0].Width = 76;
        _results.Columns[1].Width = 76;
        _results.Columns[2].Width = 150;
        _results.Columns[3].Width = 98;
        _results.Columns[4].Width = 56;
        _results.Columns[5].Width = 76;
        _results.Columns[6].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
    }

    private void WireEvents()
    {
        _gender.SelectedIndexChanged += (_, _) => SelectionChanged();
        _component.SelectedIndexChanged += (_, _) => SelectionChanged();
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
                relativePath);
            _results.Rows[rowIndex].Cells[6].ToolTipText = entry.FilePath;
        }

        _resultCount.Text = $"{_results.Rows.Count} RESULT{(_results.Rows.Count == 1 ? string.Empty : "S")}";
    }

    private void ShowBaseResult(BaseGameClothingEntry entry)
    {
        _selectedBaseEntry = entry;
        _extractBaseFiles.Enabled = true;
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
            fileSummary);
        _results.Rows[rowIndex].Cells[6].ToolTipText = string.Join(
            Environment.NewLine,
            new[] { entry.ModelArchivePath }.Concat(entry.TextureArchivePaths));
        _resultCount.Text = "1 RESULT";
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
        return form._customStart.ReadOnly &&
               form._customStart.Text == "178" &&
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
        MessageBox.Show(this, message, "BLRP Clothing Locator", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
