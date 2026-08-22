using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;

namespace BLRP.WeaponSkinTool;

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

    private readonly TextBox _root = CreateTextBox();
    private readonly TextBox _dataDirectory = CreateTextBox();
    private readonly TextBox _streamDirectory = CreateTextBox();
    private readonly TextBox _weaponMeta = CreateTextBox();
    private readonly TextBox _modelPrefix = CreateTextBox();
    private readonly TextBox _componentPrefix = CreateTextBox();
    private readonly TextBox _sourceModel = CreateTextBox();
    private readonly TextBox _sourceTexture = CreateTextBox();
    private readonly TextBox _sourcePng = CreateTextBox();
    private readonly TextBox _staffPng = CreateTextBox();
    private readonly TextBox _nextSkin = CreateTextBox(true);
    private readonly TextBox _targetModel = CreateTextBox(true);
    private readonly TextBox _targetComponent = CreateTextBox(true);
    private readonly TextBox _cupRoot = CreateTextBox();
    private readonly TextBox _cupModel = CreateTextBox();
    private readonly TextBox _cupPng = CreateTextBox();
    private readonly TextBox _cupTop = CreateTextBox();
    private readonly TextBox _cupLod = CreateTextBox();
    private readonly TextBox _cupId = CreateTextBox();
    private readonly TextBox _cupYdrTarget = CreateTextBox(true);
    private readonly TextBox _cupIconTarget = CreateTextBox(true);
    private readonly TextBox _inventoryModel = CreateTextBox();
    private readonly ComboBox _inventoryTexture = CreatePathComboBox();
    private readonly TextBox _inventoryReplacement = CreateTextBox();
    private readonly Label _status = CreateLabel("READY", 9, AccentLight, FontStyle.Bold);
    private readonly ModelPreview _preview = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _mode = CreateComboBox();
    private readonly Button _importButton;
    private readonly Button _cupCreateButton;
    private Control _developerCard = null!;
    private Control _staffCard = null!;
    private Control _cupCard = null!;
    private Control _inventoryCard = null!;
    private string? _loadedModel;
    private string? _loadedReplacement;
    private string? _loadedCupTop;
    private string? _loadedCupLod;
    private bool IsStaff => _mode.SelectedIndex == 0;
    private bool IsDeveloper => _mode.SelectedIndex == 1;
    private bool IsCup => _mode.SelectedIndex == 2;
    private bool IsInventory => _mode.SelectedIndex == 3;

    public MainForm()
    {
        Text = $"BLRP Asset Studio v{typeof(MainForm).Assembly.GetName().Version?.ToString(3)}";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1240, 820);
        ClientSize = new Size(1240, 820);
        BackColor = BackgroundTop;
        ForeColor = TextPrimary;
        Font = PickMonoFont(9F);
        DoubleBuffered = true;

        _importButton = CreateButton("ADD WEAPON SKIN", async (_, _) => await ImportAsync(), true);
        _importButton.Enabled = false;
        _cupCreateButton = CreateButton("CREATE CUP YDR + WEBP", async (_, _) => await CreateCupAssetsAsync(), true);
        _staffPng.PlaceholderText = "CHOOSE TICKET PNG OR DDS";
        _sourcePng.PlaceholderText = "NO REPLACEMENT SELECTED - PREVIEW USES SOURCE YTD";
        _cupPng.PlaceholderText = "OPTIONAL FOR PREVIEW / REQUIRED TO CREATE";
        _cupTop.PlaceholderText = "OPTIONAL - KEEPS SOURCE WHEN BLANK";
        _cupLod.PlaceholderText = "OPTIONAL - KEEPS SOURCE WHEN BLANK";
        _inventoryTexture.SelectionChangeCommitted += (_, _) => _ = LoadPreviewAsync();
        BuildInterface();
        SetBatDefaults();
        _mode.Items.AddRange(["STAFF PREVIEW", "DEVELOPER IMPORT", "CUP CREATOR", "INVENTORY PHOTO"]);
        _mode.SelectedIndexChanged += (_, _) => ApplyMode();
        _mode.SelectedIndex = 0;
        Shown += async (_, _) => { if (IsDeveloper && Directory.Exists(_root.Text)) await ScanAsync(); };
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(ClientRectangle, BackgroundTop, BackgroundBottom, 135F);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    private void BuildInterface()
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(24, 18, 24, 18), ColumnCount = 1, RowCount = 3 };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        page.Controls.Add(BuildHeader(), 0, 0);

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 1 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 550));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var leftHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        _staffCard = BuildStaffCard();
        _developerCard = BuildSetupCard();
        _cupCard = BuildCupCard();
        _inventoryCard = BuildInventoryCard();
        leftHost.Controls.Add(_developerCard);
        leftHost.Controls.Add(_staffCard);
        leftHost.Controls.Add(_cupCard);
        leftHost.Controls.Add(_inventoryCard);
        split.Controls.Add(leftHost, 0, 0);
        split.Controls.Add(BuildPreviewCard(), 1, 0);
        page.Controls.Add(split, 0, 1);
        page.Controls.Add(_status, 0, 2);
        Controls.Add(page);
    }

    private Control BuildHeader()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 3, RowCount = 2, Padding = new Padding(4, 0, 0, 4) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var logo = new PictureBox
        {
            Dock = DockStyle.Fill,
            Image = Image.FromFile(Path.Combine(AppContext.BaseDirectory, "BLRP_Logo.png")),
            Margin = new Padding(0, 0, 8, 4),
            SizeMode = PictureBoxSizeMode.Zoom
        };
        layout.Controls.Add(logo, 0, 0);
        layout.SetRowSpan(logo, 2);
        layout.Controls.Add(CreateLabel("ASSET STUDIO", 18, TextPrimary, FontStyle.Bold), 1, 0);
        layout.Controls.Add(CreateLabel("BADLANDSRP  /  PREVIEW  /  BUILD  /  SNAP", 8, TextMuted, FontStyle.Bold), 1, 1);
        layout.Controls.Add(CreateLabel("APP MODE", 7, TextMuted, FontStyle.Bold), 2, 0);
        _mode.Dock = DockStyle.Fill;
        layout.Controls.Add(_mode, 2, 1);
        return layout;
    }

    private Control BuildStaffCard()
    {
        var card = new BlrpCard(CardTop, CardBottom, Accent) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0), Padding = new Padding(18) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 3, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Label heading = CreateLabel("STAFF TICKET PREVIEW", 11, AccentLight, FontStyle.Bold);
        layout.Controls.Add(heading, 0, 0);
        layout.SetColumnSpan(heading, 3);
        Label explanation = CreateLabel("Choose the submitted PNG or DDS and preview it on the built-in BLRP bat. This mode cannot access the repository or change game files.", 9, TextPrimary);
        explanation.AutoEllipsis = false;
        layout.Controls.Add(explanation, 0, 1);
        layout.SetColumnSpan(explanation, 3);
        AddSection(layout, "SUBMISSION", 2);
        AddField(layout, "DIFFUSE TEXTURE (.PNG / .DDS)", _staffPng, 3,
            CreateButton("BROWSE", (_, _) => BrowseAsset(_staffPng, ReplacementFilter)),
            CreateButton("CLEAR", (_, _) => _staffPng.Clear()));
        TextBox template = CreateTextBox(true);
        template.Text = "BLRP BAT  /  BAKED YDR + YTD  /  DXT5 OUTPUT";
        AddField(layout, "PREVIEW TEMPLATE", template, 4);
        Button previewButton = CreateButton("PREVIEW SUBMISSION", async (_, _) => await LoadPreviewAsync(), true);
        previewButton.Dock = DockStyle.Fill;
        previewButton.Margin = new Padding(0, 4, 0, 0);
        layout.Controls.Add(previewButton, 0, 5);
        layout.SetColumnSpan(previewButton, 3);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildSetupCard()
    {
        var card = new BlrpCard(CardTop, CardBottom, Accent) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0), Padding = new Padding(18, 14, 18, 14) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, AutoScroll = true, ColumnCount = 3, RowCount = 15 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        for (int row = 0; row < 14; row++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, row is 0 or 3 or 7 or 11 ? 24 : 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddSection(layout, "BADLANDSRP RESOURCE", 0);
        AddField(layout, "REPOSITORY ROOT", _root, 1, CreateButton("BROWSE", BrowseRoot), CreateButton("SCAN", async (_, _) => await ScanAsync(), true));
        AddField(layout, "DATA DIRECTORY (RELATIVE)", _dataDirectory, 2);
        AddSection(layout, "WEAPON PROFILE", 3);
        AddField(layout, "STREAM DIRECTORY (RELATIVE)", _streamDirectory, 4);
        AddField(layout, "WEAPON META (RELATIVE)", _weaponMeta, 5);
        AddDualField(layout, "MODEL PREFIX", _modelPrefix, "COMPONENT PREFIX", _componentPrefix, 6);
        AddSection(layout, "SOURCE ASSETS", 7);
        AddField(layout, "MODEL (.YDR)", _sourceModel, 8, CreateButton("BROWSE", (_, _) => BrowseAsset(_sourceModel, "YDR model|*.ydr")));
        AddField(layout, "TEXTURES (.YTD)", _sourceTexture, 9, CreateButton("BROWSE", (_, _) => BrowseAsset(_sourceTexture, "YTD textures|*.ytd")));
        AddField(layout, "REPLACEMENT DIFFUSE (.PNG / .DDS, OPTIONAL)", _sourcePng, 10,
            CreateButton("BROWSE", (_, _) => BrowseAsset(_sourcePng, ReplacementFilter)),
            CreateButton("CLEAR", (_, _) => _sourcePng.Clear()));
        AddSection(layout, "NEXT SAFE SKIN", 11);
        AddDualField(layout, "NUMBER", _nextSkin, "COMPONENT", _targetComponent, 12);
        AddField(layout, "TARGET MODEL", _targetModel, 13, _importButton);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildCupCard()
    {
        var card = new BlrpCard(CardTop, CardBottom, Accent) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0), Padding = new Padding(18) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 3, RowCount = 14 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Label heading = CreateLabel("CUP PREVIEW + CREATOR", 11, AccentLight, FontStyle.Bold);
        layout.Controls.Add(heading, 0, 0);
        layout.SetColumnSpan(heading, 3);
        Label explanation = CreateLabel("Load any cup YDR, apply a 2:1 PNG or DDS wrap, pose it in 3D, then create the embedded-texture YDR and transparent inventory WebP.", 8.5F, TextPrimary);
        explanation.AutoEllipsis = false;
        layout.Controls.Add(explanation, 0, 1);
        layout.SetColumnSpan(explanation, 3);
        AddSection(layout, "CUP SOURCE", 2);
        AddField(layout, "TEMPLATE CUP (.YDR)", _cupModel, 3, CreateButton("BROWSE", (_, _) => BrowseAsset(_cupModel, "YDR model|*.ydr")));
        AddField(layout, "CUP WRAP (.PNG / .DDS, 2:1)", _cupPng, 4,
            CreateButton("BROWSE", (_, _) => BrowseAsset(_cupPng, ReplacementFilter)),
            CreateButton("CLEAR", (_, _) => _cupPng.Clear()));
        Button advanced = null!;
        advanced = CreateButton("SHOW OPTIONAL TOP + LOD", (_, _) =>
        {
            bool show = layout.RowStyles[6].Height == 0;
            layout.RowStyles[6].Height = layout.RowStyles[7].Height = show ? 42 : 0;
            advanced.Text = show ? "HIDE OPTIONAL TOP + LOD" : "SHOW OPTIONAL TOP + LOD";
            layout.PerformLayout();
        });
        advanced.Dock = DockStyle.Fill;
        advanced.Margin = new Padding(0, 4, 0, 2);
        layout.Controls.Add(advanced, 0, 5);
        layout.SetColumnSpan(advanced, 3);
        AddField(layout, "LID TEXTURE (coffee_top, OPTIONAL)", _cupTop, 6,
            CreateButton("BROWSE", (_, _) => BrowseAsset(_cupTop, ReplacementFilter)),
            CreateButton("CLEAR", (_, _) => _cupTop.Clear()));
        AddField(layout, "DISTANCE TEXTURE (coffee_lod, OPTIONAL)", _cupLod, 7,
            CreateButton("BROWSE", (_, _) => BrowseAsset(_cupLod, ReplacementFilter)),
            CreateButton("CLEAR", (_, _) => _cupLod.Clear()));
        AddSection(layout, "REPOSITORY OUTPUT", 8);
        AddField(layout, "BADLANDSRP ROOT", _cupRoot, 9, CreateButton("BROWSE", (_, _) => BrowseFolder(_cupRoot)));
        AddField(layout, "CUP ASSET ID", _cupId, 10);
        AddField(layout, "YDR TARGET", _cupYdrTarget, 11);
        AddField(layout, "INVENTORY WEBP TARGET", _cupIconTarget, 12);
        _cupCreateButton.Dock = DockStyle.Top;
        _cupCreateButton.Height = 38;
        _cupCreateButton.Margin = new Padding(0, 8, 0, 0);
        layout.Controls.Add(_cupCreateButton, 0, 13);
        layout.SetColumnSpan(_cupCreateButton, 3);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildInventoryCard()
    {
        var card = new BlrpCard(CardTop, CardBottom, Accent) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0), Padding = new Padding(18) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 3, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Label heading = CreateLabel("INVENTORY ITEM PHOTO", 11, AccentLight, FontStyle.Bold);
        layout.Controls.Add(heading, 0, 0);
        layout.SetColumnSpan(heading, 3);
        Label explanation = CreateLabel("Load any GTA YDR, YDD, or YFT with its optional YTD, pose it in 3D, then save the current view as a transparent 256x256 WebP with drop shadow.", 9, TextPrimary);
        explanation.AutoEllipsis = false;
        layout.Controls.Add(explanation, 0, 1);
        layout.SetColumnSpan(explanation, 3);
        AddSection(layout, "MODEL SOURCE", 2);
        AddField(layout, "MODEL (.YDR / .YDD / .YFT)", _inventoryModel, 3,
            CreateButton("BROWSE", (_, _) => BrowseAsset(_inventoryModel, "GTA model|*.ydr;*.ydd;*.yft|YDR model|*.ydr|YDD dictionary|*.ydd|YFT fragment|*.yft")));
        AddField(layout, "TEXTURES (.YTD, OPTIONAL / DETECTED)", _inventoryTexture, 4,
            CreateButton("CUSTOM YTD", (_, _) => BrowseAsset(_inventoryTexture, "YTD textures|*.ytd")),
            CreateButton("CLEAR", (_, _) => { _inventoryTexture.SelectedIndex = -1; _inventoryTexture.Text = string.Empty; }));
        AddField(layout, "DIFFUSE OVERRIDE (.PNG / .DDS, OPTIONAL)", _inventoryReplacement, 5,
            CreateButton("BROWSE", (_, _) => BrowseAsset(_inventoryReplacement, ReplacementFilter)),
            CreateButton("CLEAR", (_, _) => _inventoryReplacement.Clear()));
        Button preview = CreateButton("LOAD MODEL TO POSE", async (_, _) => await LoadPreviewAsync(), true);
        preview.Dock = DockStyle.Fill;
        preview.Margin = new Padding(0, 8, 0, 0);
        layout.Controls.Add(preview, 0, 6);
        layout.SetColumnSpan(preview, 3);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildPreviewCard()
    {
        var card = new BlrpCard(CardTop, CardBottom, Accent) { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(14) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.Controls.Add(CreateLabel("3D MODEL PREVIEW", 11, AccentLight, FontStyle.Bold), 0, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, BackColor = Color.Transparent, WrapContents = false };
        buttons.Controls.Add(CreateButton("RESET POSE", (_, _) => _preview.ResetView()));
        layout.Controls.Add(buttons, 1, 0);
        layout.Controls.Add(_preview, 0, 1);
        layout.SetColumnSpan(_preview, 2);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 3 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        footer.Controls.Add(CreateLabel("LEFT DRAG ROTATE  /  RIGHT DRAG TILT  /  WHEEL ZOOM", 8, TextMuted, FontStyle.Bold), 0, 0);
        Button saveInventory = CreateButton("SAVE 256 WEBP", (_, _) => SaveInventoryImage(), true);
        saveInventory.Dock = DockStyle.Fill;
        footer.Controls.Add(saveInventory, 1, 0);
        Button loadPreview = CreateButton("LOAD PREVIEW", async (_, _) => await LoadPreviewAsync(), true);
        loadPreview.Dock = DockStyle.Fill;
        footer.Controls.Add(loadPreview, 2, 0);
        layout.Controls.Add(footer, 0, 2);
        layout.SetColumnSpan(footer, 2);
        card.Controls.Add(layout);
        return card;
    }

    private void SetBatDefaults()
    {
        _root.Text = Directory.Exists(@"D:\BadlandsRP") ? @"D:\BadlandsRP" : string.Empty;
        _dataDirectory.Text = @"resources\blrp_weapons\data\bats";
        _streamDirectory.Text = @"resources\blrp_weapons\stream\bats";
        _weaponMeta.Text = @"resources\blrp_weapons\weapons.meta";
        _modelPrefix.Text = "W_ME_Bat_BL";
        _componentPrefix.Text = "COMPONENT_BAT_VARMOD_BL";
        _cupRoot.Text = _root.Text;
        _cupModel.Text = Path.Combine(AppContext.BaseDirectory, "assets", "cup-template", "prop_coffeecup_template.ydr");
        _cupId.Text = "prop_coffeecup_new";
        _cupRoot.TextChanged += (_, _) => UpdateCupTargets();
        _cupId.TextChanged += (_, _) => UpdateCupTargets();
        UpdateCupTargets();
    }

    private WeaponSkinSettings Settings() => new(_root.Text, _dataDirectory.Text, _streamDirectory.Text, _weaponMeta.Text, _modelPrefix.Text, _componentPrefix.Text);

    private string BatTemplate(string extension) => Path.Combine(AppContext.BaseDirectory, "assets", "bat-template", "w_me_bat_bl_template" + extension);

    private void ApplyMode()
    {
        if (_developerCard is null || _staffCard is null || _cupCard is null || _inventoryCard is null) return;
        _developerCard.Visible = IsDeveloper;
        _staffCard.Visible = IsStaff;
        _cupCard.Visible = IsCup;
        _inventoryCard.Visible = IsInventory;
        if (IsInventory) _inventoryCard.BringToFront(); else if (IsCup) _cupCard.BringToFront(); else if (IsDeveloper) _developerCard.BringToFront(); else _staffCard.BringToFront();
        _preview.EmptyMessage = IsInventory
            ? "SELECT A GTA YDR, YDD, OR YFT, THEN LOAD MODEL"
            : IsCup
            ? "SELECT A CUP YDR, THEN LOAD PREVIEW"
            : IsDeveloper
            ? "SELECT A YDR + YTD, THEN LOAD PREVIEW"
            : "SELECT A SUBMISSION PNG OR DDS, THEN LOAD PREVIEW";
        _preview.Invalidate();
        SetStatus(IsInventory
            ? "INVENTORY PHOTO  /  LOAD, POSE, THEN SAVE 256 WEBP"
            : IsCup
            ? "CUP CREATOR  /  LOAD, POSE, THEN CREATE YDR + WEBP"
            : IsDeveloper
            ? "DEVELOPER IMPORT MODE  /  SCAN THE REPOSITORY TO BEGIN"
            : "STAFF PREVIEW MODE  /  CHOOSE A SUBMISSION PNG OR DDS");
    }

    private async Task ScanAsync()
    {
        SetBusy("SCANNING WEAPON METADATA...");
        try
        {
            WeaponSkinPlan plan = await Task.Run(() => WeaponSkinImporter.Analyze(Settings()));
            _nextSkin.Text = plan.Index.ToString();
            _targetModel.Text = Path.GetFileNameWithoutExtension(plan.ModelTarget);
            _targetComponent.Text = plan.ComponentName;
            (string Model, string Texture)? latest = WeaponSkinImporter.FindLatestAssetPair(
                Path.GetDirectoryName(plan.ModelTarget)!, _modelPrefix.Text);
            if (latest is not null)
            {
                _sourceModel.Text = latest.Value.Model;
                _sourceTexture.Text = latest.Value.Texture;
            }
            _importButton.Enabled = true;
            SetStatus(plan.Warning ?? $"READY  /  NEXT SKIN {plan.Index}  /  ATTACH BONE {WeaponBoneExpander.BoneForSkin(plan.Index)}", plan.Warning is not null);
        }
        catch (Exception exception)
        {
            ClearPlan();
            ShowError(exception.Message);
        }
    }

    private async Task LoadPreviewAsync()
    {
        string modelPath = IsInventory ? _inventoryModel.Text : IsCup ? _cupModel.Text : IsDeveloper ? _sourceModel.Text : BatTemplate(".ydr");
        string? texturePath = IsInventory
            ? (string.IsNullOrWhiteSpace(_inventoryTexture.Text) ? null : _inventoryTexture.Text)
            : IsCup ? null : IsDeveloper ? _sourceTexture.Text : BatTemplate(".ytd");
        string? imagePath = IsInventory
            ? (string.IsNullOrWhiteSpace(_inventoryReplacement.Text) ? null : _inventoryReplacement.Text)
            : IsCup
            ? (string.IsNullOrWhiteSpace(_cupPng.Text) ? null : _cupPng.Text)
            : IsDeveloper
            ? (string.IsNullOrWhiteSpace(_sourcePng.Text) ? null : _sourcePng.Text)
            : (string.IsNullOrWhiteSpace(_staffPng.Text) ? null : _staffPng.Text);
        string? topPath = IsCup && !string.IsNullOrWhiteSpace(_cupTop.Text) ? _cupTop.Text : null;
        string? lodPath = IsCup && !string.IsNullOrWhiteSpace(_cupLod.Text) ? _cupLod.Text : null;
        if (!File.Exists(modelPath) || (texturePath is not null && !File.Exists(texturePath)))
        {
            ShowError(IsInventory
                ? "Choose a valid YDR, YDD, or YFT and, when used by the model, its YTD texture dictionary."
                : IsCup
                ? "Choose a cup YDR first."
                : IsDeveloper
                ? "Choose both a YDR model and YTD texture dictionary first."
                : "The built-in bat preview assets are missing. Reinstall the app.");
            return;
        }
        if (IsStaff && imagePath is null)
        {
            ShowError("Choose the submitted PNG or DDS first.");
            return;
        }
        SetStatus("BUILDING 3D PREVIEW...");
        try
        {
            bool diagonalBatPose = IsStaff || Path.GetFileNameWithoutExtension(modelPath).Contains("bat", StringComparison.OrdinalIgnoreCase);
            await _preview.LoadAsync(modelPath, texturePath, imagePath, topPath, lodPath,
                diagonalBatPose ? -MathF.PI / 4f : 0f);
            _loadedModel = Path.GetFullPath(modelPath);
            _loadedReplacement = imagePath is null ? null : Path.GetFullPath(imagePath);
            _loadedCupTop = topPath is null ? null : Path.GetFullPath(topPath);
            _loadedCupLod = lodPath is null ? null : Path.GetFullPath(lodPath);
            SetStatus(imagePath is null && (topPath is not null || lodPath is not null)
                ? "PREVIEW READY  /  OPTIONAL CUP TEXTURES APPLIED  /  DRAG TO ROTATE"
                : imagePath is null
                ? $"PREVIEW READY  /  SOURCE {(texturePath is null ? "EMBEDDED TEXTURES" : "YTD")}  /  DRAG TO ROTATE"
                : Path.GetExtension(imagePath).Equals(".dds", StringComparison.OrdinalIgnoreCase)
                ? "PREVIEW READY  /  DDS APPLIED DIRECTLY  /  DRAG TO ROTATE"
                : "PREVIEW READY  /  PNG APPLIED AS DXT5  /  DRAG TO ROTATE");
        }
        catch (Exception exception) { ShowError("Preview failed: " + exception.Message); }
    }

    private async Task ImportAsync()
    {
        WeaponSkinPlan plan;
        try { plan = WeaponSkinImporter.Analyze(Settings()); }
        catch (Exception exception) { ShowError(exception.Message); return; }
        if (MessageBox.Show(this,
            $"Add skin {plan.Index}?\n\n{Path.GetFileName(plan.ModelTarget)}\n{Path.GetFileName(plan.TextureTarget)}\n{plan.ComponentName}\n\nThe three metadata files will be backed up first.",
            "Confirm weapon skin import", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        SetBusy("ADDING WEAPON SKIN...");
        try
        {
            plan = await Task.Run(() => WeaponSkinImporter.Import(
                Settings(), _sourceModel.Text, _sourceTexture.Text,
                string.IsNullOrWhiteSpace(_sourcePng.Text) ? null : _sourcePng.Text));
            SetStatus($"ADDED SKIN {plan.Index}  /  ASSETS + METADATA + {WeaponBoneExpander.BoneForSkin(plan.Index)} VERIFIED");
            MessageBox.Show(this, $"Weapon skin {plan.Index} was added successfully.\n\n{plan.ModelTarget}", "Import complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await ScanAsync();
        }
        catch (Exception exception) { ShowError("Import failed and was rolled back: " + exception.Message); }
    }

    private async Task CreateCupAssetsAsync()
    {
        string id;
        try { id = CupAssetId(); }
        catch (Exception exception) { ShowError(exception.Message); return; }
        if (!Directory.Exists(_cupRoot.Text) || !File.Exists(_cupModel.Text) || !File.Exists(_cupPng.Text))
        {
            ShowError("Choose a valid repository root, template cup YDR, and 2:1 PNG or DDS wrap first.");
            return;
        }
        if (!Path.GetFullPath(_cupModel.Text).Equals(_loadedModel, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFullPath(_cupPng.Text).Equals(_loadedReplacement, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(OptionalFullPath(_cupTop.Text), _loadedCupTop, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(OptionalFullPath(_cupLod.Text), _loadedCupLod, StringComparison.OrdinalIgnoreCase))
        {
            ShowError("Load and pose the current cup and wrap before creating its YDR and inventory image.");
            return;
        }
        string ydrTarget = Path.Combine(CupStreamFolder()!, id + ".ydr");
        string iconTarget = Path.Combine(InventoryFolder()!, id + ".webp");
        if (File.Exists(ydrTarget) || File.Exists(iconTarget))
        {
            ShowError("The cup YDR or inventory image already exists. Choose a new cup asset ID.");
            return;
        }
        string ydrTemp = ydrTarget + ".asset-studio.tmp";
        string iconTemp = iconTarget + ".asset-studio.tmp";
        bool ydrCreated = false, iconCreated = false;
        SetStatus("CREATING CUP YDR + INVENTORY WEBP...");
        try
        {
            byte[] ydr = await Task.Run(() => WeaponTextureBuilder.BuildEmbedded(
                _cupModel.Text, _cupPng.Text, OptionalFullPath(_cupTop.Text), OptionalFullPath(_cupLod.Text)));
            Directory.CreateDirectory(Path.GetDirectoryName(ydrTarget)!);
            Directory.CreateDirectory(Path.GetDirectoryName(iconTarget)!);
            File.WriteAllBytes(ydrTemp, ydr);
            _preview.SaveInventoryImage(iconTemp);
            File.Move(ydrTemp, ydrTarget);
            ydrCreated = true;
            File.Move(iconTemp, iconTarget);
            iconCreated = true;
            SetStatus($"CREATED {id}.YDR + 256 WEBP  /  ADD THE CUP TO CUPS.LUA");
            MessageBox.Show(this, $"Cup assets created.\n\n{ydrTarget}\n{iconTarget}\n\nThe cups.lua item entry is still manual.", "Cup created", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            File.Delete(ydrTemp);
            File.Delete(iconTemp);
            if (ydrCreated) File.Delete(ydrTarget);
            if (iconCreated) File.Delete(iconTarget);
            ShowError("Cup creation failed and was rolled back: " + exception.Message);
        }
    }

    private void SaveInventoryImage()
    {
        string name;
        try
        {
            name = IsCup ? CupAssetId() : IsInventory
                ? Path.GetFileNameWithoutExtension(_inventoryModel.Text)
                : IsDeveloper
                ? WeaponInventoryName()
                : Path.GetFileNameWithoutExtension(_staffPng.Text);
        }
        catch (Exception exception) { ShowError(exception.Message); return; }
        if (string.IsNullOrWhiteSpace(name)) name = "inventory_item";
        using var dialog = new SaveFileDialog
        {
            Filter = "WebP image|*.webp",
            DefaultExt = "webp",
            AddExtension = true,
            FileName = name + ".webp",
            InitialDirectory = InventoryFolder() ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _preview.SaveInventoryImage(dialog.FileName);
            SetStatus("SAVED 256x256 TRANSPARENT WEBP  /  CURRENT POSE + DROP SHADOW");
        }
        catch (Exception exception) { ShowError("Inventory image failed: " + exception.Message); }
    }

    private void UpdateCupTargets()
    {
        try
        {
            string id = CupAssetId();
            _cupYdrTarget.Text = CupStreamFolder() is string stream ? Path.Combine(stream, id + ".ydr") : string.Empty;
            _cupIconTarget.Text = InventoryFolder() is string inventory ? Path.Combine(inventory, id + ".webp") : string.Empty;
        }
        catch
        {
            _cupYdrTarget.Clear();
            _cupIconTarget.Clear();
        }
    }

    private string CupAssetId()
    {
        string id = _cupId.Text.Trim().ToLowerInvariant();
        if (!id.StartsWith("prop_coffeecup_", StringComparison.Ordinal)) id = "prop_coffeecup_" + id;
        if (!Regex.IsMatch(id, "^prop_coffeecup_[a-z0-9_]+$"))
            throw new InvalidDataException("The cup asset ID may contain only lowercase letters, numbers, and underscores.");
        return id;
    }

    private string WeaponInventoryName()
    {
        string prefix = _modelPrefix.Text.Trim();
        if (int.TryParse(_nextSkin.Text, out int index) && prefix.StartsWith("W_ME_", StringComparison.OrdinalIgnoreCase))
            return "comp_sk_" + prefix[5..].ToLowerInvariant() + "_" + index;
        return string.IsNullOrWhiteSpace(_targetModel.Text) ? Path.GetFileNameWithoutExtension(_sourceModel.Text) : _targetModel.Text;
    }

    private void BrowseRoot(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose the BadlandsRP repository", SelectedPath = Directory.Exists(_root.Text) ? _root.Text : @"D:\" };
        if (dialog.ShowDialog(this) == DialogResult.OK) _root.Text = dialog.SelectedPath;
    }

    private void BrowseFolder(TextBox target)
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose the BadlandsRP repository", SelectedPath = Directory.Exists(target.Text) ? target.Text : @"D:\" };
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
    }

    private void BrowseAsset(Control target, string filter)
    {
        string? initialDirectory = target == _cupModel
            ? CupStreamFolder()
            : target == _sourceModel || target == _sourceTexture
            ? StreamFolder()
            : (File.Exists(target.Text) ? Path.GetDirectoryName(target.Text) : null);
        using var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true, RestoreDirectory = true };
        if (initialDirectory is not null) dialog.InitialDirectory = initialDirectory;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        target.Text = dialog.FileName;
        if (target == _sourceModel)
        {
            string pair = Path.ChangeExtension(dialog.FileName, ".ytd");
            if (File.Exists(pair)) _sourceTexture.Text = pair;
        }
        if (target == _inventoryModel)
        {
            SetDetectedInventoryTextures(dialog.FileName);
        }
        if (target == _inventoryTexture && !_inventoryTexture.Items.Contains(dialog.FileName))
        {
            _inventoryTexture.Items.Add(dialog.FileName);
        }
        if (target == _cupModel || ((target == _cupPng || target == _cupTop || target == _cupLod) && File.Exists(_cupModel.Text)) || target == _staffPng ||
            (target == _sourcePng && File.Exists(_sourceModel.Text) && File.Exists(_sourceTexture.Text)) ||
            ((target == _inventoryTexture || target == _inventoryReplacement) && File.Exists(_inventoryModel.Text)))
            _ = LoadPreviewAsync();
    }

    private void SetDetectedInventoryTextures(string modelPath)
    {
        _inventoryTexture.Items.Clear();
        string exact = Path.ChangeExtension(modelPath, ".ytd");
        IEnumerable<string> matches = File.Exists(exact) ? [exact] : [];
        Regex? clothing = ClothingTextureRegex(modelPath);
        string? folder = Path.GetDirectoryName(modelPath);
        if (clothing is not null && folder is not null)
            matches = matches.Concat(Directory.EnumerateFiles(folder, "*.ytd")
                .Where(path => clothing.IsMatch(Path.GetFileNameWithoutExtension(path))));
        string[] options = matches.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        _inventoryTexture.Items.AddRange(options);
        _inventoryTexture.SelectedIndex = options.Length > 0 ? 0 : -1;
        _inventoryTexture.Text = options.FirstOrDefault() ?? string.Empty;
        if (options.Length > 1) SetStatus($"DETECTED {options.Length} MATCHING YTDS  /  CHOOSE A TEXTURE VARIANT");
    }

    private static Regex? ClothingTextureRegex(string modelPath)
    {
        string stem = Path.GetFileNameWithoutExtension(modelPath);
        string prefix = stem.Contains('^') ? stem[..(stem.LastIndexOf('^') + 1)] : string.Empty;
        string name = stem[(stem.LastIndexOf('^') + 1)..];
        string[] parts = name.Split('_');
        bool prop = parts.Length >= 3 && parts[0].Equals("p", StringComparison.OrdinalIgnoreCase);
        int numberIndex = prop ? 2 : 1;
        if (parts.Length <= numberIndex || !int.TryParse(parts[numberIndex], out _)) return null;
        string component = prop ? parts[0] + "_" + parts[1] : parts[0];
        return new Regex($"^{Regex.Escape(prefix + component)}_diff_{Regex.Escape(parts[numberIndex])}(?:_|$)", RegexOptions.IgnoreCase);
    }

    internal static bool TextureMatchingSelfTest()
    {
        Regex component = ClothingTextureRegex(@"C:\pack\shop.v1^jbib_005_u.ydd")!;
        Regex prop = ClothingTextureRegex(@"C:\pack\p_head_012.ydd")!;
        return component.IsMatch("shop.v1^jbib_diff_005_a_uni") && !component.IsMatch("shopXv1^jbib_diff_005_a_uni") &&
            !component.IsMatch("shop.v1^jbib_diff_006_a_uni") && prop.IsMatch("p_head_diff_012_a");
    }

    private string? StreamFolder()
    {
        try
        {
            string path = Path.GetFullPath(Path.Combine(_root.Text.Trim(), _streamDirectory.Text.Trim()));
            return Directory.Exists(path) ? path : null;
        }
        catch { return null; }
    }

    private string? CupStreamFolder() => RepositoryFolder(_cupRoot.Text,
        @"resources\[custom_props]\props_Addon\stream\_furniture-only\housing_cups");

    private string? InventoryFolder() => RepositoryFolder(IsCup ? _cupRoot.Text : _root.Text,
        @"resources\blrp_inventory\images");

    private static string? RepositoryFolder(string root, string relative)
    {
        try
        {
            if (!Directory.Exists(root)) return null;
            return Path.GetFullPath(Path.Combine(root.Trim(), relative));
        }
        catch { return null; }
    }

    private void ClearPlan()
    {
        _nextSkin.Clear();
        _targetModel.Clear();
        _targetComponent.Clear();
        _importButton.Enabled = false;
    }

    private void SetBusy(string message) { _importButton.Enabled = false; SetStatus(message); }
    private void SetStatus(string message, bool warning = false) { _status.Text = message.ToUpperInvariant(); _status.ForeColor = warning ? Color.FromArgb(255, 180, 50) : AccentLight; }
    private void ShowError(string message) { SetStatus(message, true); MessageBox.Show(this, message, "BLRP Asset Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning); }

    private const string ReplacementFilter = "Texture image|*.png;*.dds|PNG image|*.png|DDS texture|*.dds";

    private static string? OptionalFullPath(string path) => string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);

    private static void AddSection(TableLayoutPanel layout, string text, int row)
    {
        Label label = CreateLabel(text, 9, AccentLight, FontStyle.Bold);
        label.Padding = new Padding(0, 4, 0, 0);
        layout.Controls.Add(label, 0, row);
        layout.SetColumnSpan(label, 3);
    }

    private static void AddField(TableLayoutPanel layout, string label, Control field, int row, params Control[] buttons)
    {
        var holder = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        holder.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
        holder.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        holder.Controls.Add(CreateLabel(label, 7, TextMuted, FontStyle.Bold), 0, 0);
        field.Dock = DockStyle.Fill;
        holder.Controls.Add(field, 0, 1);
        layout.Controls.Add(holder, 0, row);
        int column = 1;
        foreach (Control button in buttons) { button.Dock = DockStyle.Fill; button.Margin = new Padding(5, 15, 0, 1); layout.Controls.Add(button, column++, row); }
        if (buttons.Length == 0) layout.SetColumnSpan(holder, 3);
    }

    private static void AddDualField(TableLayoutPanel layout, string leftLabel, Control left, string rightLabel, Control right, int row)
    {
        var holder = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
        holder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        holder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        holder.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
        holder.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        holder.Controls.Add(CreateLabel(leftLabel, 7, TextMuted, FontStyle.Bold), 0, 0);
        holder.Controls.Add(CreateLabel(rightLabel, 7, TextMuted, FontStyle.Bold), 1, 0);
        left.Dock = DockStyle.Fill; right.Dock = DockStyle.Fill;
        holder.Controls.Add(left, 0, 1); holder.Controls.Add(right, 1, 1);
        layout.Controls.Add(holder, 0, row);
        layout.SetColumnSpan(holder, 3);
    }

    private static TextBox CreateTextBox(bool readOnly = false) => new() { BackColor = InputBackground, ForeColor = readOnly ? AccentLight : TextPrimary, BorderStyle = BorderStyle.FixedSingle, Font = PickMonoFont(8.5F), ReadOnly = readOnly };
    private static ComboBox CreatePathComboBox() => new() { BackColor = InputBackground, ForeColor = TextPrimary, FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDown, DropDownWidth = 720, Font = PickMonoFont(8.5F) };
    private static ComboBox CreateComboBox() => new() { BackColor = InputBackground, ForeColor = TextPrimary, FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList, Font = PickMonoFont(8.5F, FontStyle.Bold) };
    private static Label CreateLabel(string text, float size, Color color, FontStyle style = FontStyle.Regular) => new() { Text = text, ForeColor = color, BackColor = Color.Transparent, Font = PickMonoFont(size, style), Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private static Button CreateButton(string text, EventHandler handler, bool primary = false)
    {
        var button = new Button { Text = text, BackColor = primary ? Accent : InputBackground, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = PickMonoFont(8F, FontStyle.Bold), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        button.FlatAppearance.BorderColor = primary ? AccentLight : Color.FromArgb(90, 100, 149, 237);
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(80, 130, 220) : Color.FromArgb(55, 55, 100);
        button.Click += handler;
        return button;
    }
    private static Font PickMonoFont(float size, FontStyle style = FontStyle.Regular) => new("Cascadia Mono", size, style, GraphicsUnit.Point);
}

internal sealed class BlrpCard(Color top, Color bottom, Color border) : Panel
{
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(ClientRectangle, top, bottom, 120F);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(155, border), 1.5F);
        e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
    }
}
