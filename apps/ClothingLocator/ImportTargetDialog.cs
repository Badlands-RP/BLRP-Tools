namespace BLRP.ClothingLocator;

internal sealed class ImportTargetDialog : Form
{
    private readonly ListBox _targets = new();
    private readonly Label _details = new();
    private readonly IReadOnlyList<ClothingImportPlan> _plans;

    public ImportTargetDialog(IReadOnlyList<ClothingImportPlan> plans)
    {
        _plans = plans;
        Text = "Choose clothing addon target";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(720, 330);
        BackColor = Color.FromArgb(12, 12, 28);
        ForeColor = Color.White;
        Font = new Font("Cascadia Mono", 9F);
        Padding = new Padding(20);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        layout.Controls.Add(Label("CHOOSE TARGET ADDON PACK", 15F, Color.White, FontStyle.Bold), 0, 0);
        ClothingImportPlan first = plans[0];
        layout.Controls.Add(Label(
            $"{first.Gender.ToString().ToUpperInvariant()} / {first.Component.Code.ToUpperInvariant()} / " +
            $"{first.TexturePaths.Count} TEXTURE{(first.TexturePaths.Count == 1 ? string.Empty : "S")}",
            9F,
            Color.FromArgb(135, 206, 235),
            FontStyle.Bold), 0, 1);

        _targets.Dock = DockStyle.Fill;
        _targets.BackColor = Color.FromArgb(30, 30, 60);
        _targets.ForeColor = Color.White;
        _targets.BorderStyle = BorderStyle.FixedSingle;
        _targets.IntegralHeight = false;
        _targets.Font = new Font("Cascadia Mono", 10F, FontStyle.Bold);
        foreach (ClothingImportPlan plan in plans)
        {
            _targets.Items.Add(
                $"ADDON {plan.Pack}   NEXT SLOT {plan.PackDrawableIndex:000}   " +
                $"{plan.RemainingSlots,3} FREE AFTER   CLOTHING #{plan.GlobalIndex}");
        }
        _targets.SelectedIndexChanged += (_, _) => UpdateDetails();
        _targets.DoubleClick += (_, _) => Confirm();
        layout.Controls.Add(_targets, 0, 2);

        _details.Dock = DockStyle.Fill;
        _details.TextAlign = ContentAlignment.MiddleLeft;
        _details.ForeColor = Color.FromArgb(180, 200, 215);
        layout.Controls.Add(_details, 0, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var import = Button("IMPORT", true);
        var cancel = Button("CANCEL", false);
        import.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(import);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 4);
        Controls.Add(layout);

        AcceptButton = import;
        CancelButton = cancel;
        _targets.SelectedIndex = 0;
    }

    public ClothingImportPlan? SelectedPlan { get; private set; }

    private void UpdateDetails()
    {
        if (_targets.SelectedIndex < 0) return;
        ClothingImportPlan plan = _plans[_targets.SelectedIndex];
        _details.Text = $"{plan.ModelFileName}  /  YMT {plan.CountAfterImport}/128" +
            (plan.CountAfterImport >= 120 ? "  /  LOW CAPACITY" : string.Empty);
        _details.ForeColor = plan.CountAfterImport >= 120
            ? Color.FromArgb(255, 180, 50)
            : Color.FromArgb(180, 200, 215);
    }

    private void Confirm()
    {
        if (_targets.SelectedIndex < 0) return;
        SelectedPlan = _plans[_targets.SelectedIndex];
        DialogResult = DialogResult.OK;
    }

    private static Label Label(string text, float size, Color color, FontStyle style) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = color,
        Font = new Font("Cascadia Mono", size, style)
    };

    private static Button Button(string text, bool primary) => new()
    {
        Text = text,
        Width = 110,
        Height = 34,
        BackColor = primary ? Color.FromArgb(100, 149, 237) : Color.FromArgb(40, 40, 80),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Font = new Font("Cascadia Mono", 9F, FontStyle.Bold),
        DialogResult = primary ? DialogResult.None : DialogResult.Cancel
    };
}
