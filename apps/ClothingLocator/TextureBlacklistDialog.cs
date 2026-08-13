namespace BLRP.ClothingLocator;

internal sealed record TextureBlacklistAssignment(string TexturePath, int TextureIndex, string? Business);

internal sealed class TextureBlacklistDialog : Form
{
    private const string PublicChoice = "(PUBLIC / NO BLACKLIST)";
    private readonly IReadOnlyList<string> _texturePaths;
    private readonly int _startingTextureIndex;
    private readonly string? _drawableRestriction;
    private readonly DataGridView _grid = new();
    private readonly ComboBox _applyAll = new();

    public TextureBlacklistDialog(
        IReadOnlyList<string> texturePaths,
        int startingTextureIndex,
        IReadOnlyList<string> businesses,
        string? drawableRestriction)
    {
        _texturePaths = texturePaths;
        _startingTextureIndex = startingTextureIndex;
        _drawableRestriction = drawableRestriction;

        Text = "Assign texture blacklists";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(800, Math.Min(700, 260 + texturePaths.Count * 34));
        BackColor = Color.FromArgb(12, 12, 28);
        ForeColor = Color.White;
        Font = new Font("Cascadia Mono", 9F);
        Padding = new Padding(20);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.Controls.Add(Label("ASSIGN EACH NEW TEXTURE", 15F, Color.White, FontStyle.Bold), 0, 0);
        layout.Controls.Add(Label(
            drawableRestriction == null
                ? "Each texture defaults to public. Choose a business only where needed."
                : $"Whole drawable is already restricted to {drawableRestriction}. Per-texture choices are disabled.",
            9F,
            drawableRestriction == null ? Color.FromArgb(180, 200, 215) : Color.FromArgb(255, 180, 50),
            FontStyle.Bold), 0, 1);
        layout.Controls.Add(BuildApplyAll(businesses), 0, 2);
        layout.Controls.Add(BuildGrid(businesses), 0, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        Button import = Button($"IMPORT {texturePaths.Count}", true);
        Button cancel = Button("CANCEL", false);
        import.Click += (_, _) => DialogResult = DialogResult.OK;
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(import);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 4);
        Controls.Add(layout);
        AcceptButton = import;
        CancelButton = cancel;
    }

    public IReadOnlyList<TextureBlacklistAssignment> Assignments => _texturePaths
        .Select((path, index) => new TextureBlacklistAssignment(
            path,
            _startingTextureIndex + index,
            _drawableRestriction == null && _grid.Rows[index].Cells[2].Value is string choice && choice != PublicChoice
                ? choice
                : null))
        .ToArray();

    private Control BuildApplyAll(IReadOnlyList<string> businesses)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        panel.Controls.Add(Label("APPLY TO ALL", 9F, Color.FromArgb(135, 206, 235), FontStyle.Bold), 0, 0);
        ConfigureCombo(_applyAll, businesses);
        _applyAll.Dock = DockStyle.Fill;
        _applyAll.Enabled = _drawableRestriction == null;
        panel.Controls.Add(_applyAll, 1, 0);
        Button apply = Button("APPLY", false);
        apply.DialogResult = DialogResult.None;
        apply.Enabled = _drawableRestriction == null;
        apply.Click += (_, _) => ApplyToAll();
        panel.Controls.Add(apply, 2, 0);
        return panel;
    }

    private Control BuildGrid(IReadOnlyList<string> businesses)
    {
        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = Color.FromArgb(14, 14, 30);
        _grid.BorderStyle = BorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(40, 40, 80),
            ForeColor = Color.FromArgb(135, 206, 235),
            SelectionBackColor = Color.FromArgb(40, 40, 80),
            Font = new Font("Cascadia Mono", 8F, FontStyle.Bold)
        };
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(20, 20, 40),
            ForeColor = Color.White,
            SelectionBackColor = Color.FromArgb(65, 105, 180),
            SelectionForeColor = Color.White
        };
        _grid.RowHeadersVisible = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.MultiSelect = false;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.RowTemplate.Height = 30;

        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "TEXTURE #", Width = 92, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "SOURCE FILE", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        var choices = new DataGridViewComboBoxColumn
        {
            HeaderText = "BLACKLIST TO",
            Width = 260,
            FlatStyle = FlatStyle.Flat,
            ReadOnly = _drawableRestriction != null
        };
        choices.Items.Add(PublicChoice);
        foreach (string business in businesses)
        {
            choices.Items.Add(business);
        }
        string restrictedChoice = $"(DRAWABLE: {_drawableRestriction})";
        if (_drawableRestriction != null)
        {
            choices.Items.Add(restrictedChoice);
        }
        _grid.Columns.Add(choices);

        for (int index = 0; index < _texturePaths.Count; index++)
        {
            _grid.Rows.Add(
                _startingTextureIndex + index,
                Path.GetFileName(_texturePaths[index]),
                _drawableRestriction == null ? PublicChoice : restrictedChoice);
        }
        return _grid;
    }

    private void ApplyToAll()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            row.Cells[2].Value = _applyAll.SelectedItem;
        }
    }

    private static void ConfigureCombo(ComboBox combo, IReadOnlyList<string> businesses)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.BackColor = Color.FromArgb(40, 40, 80);
        combo.ForeColor = Color.White;
        combo.FlatStyle = FlatStyle.Flat;
        combo.Items.Add(PublicChoice);
        combo.Items.AddRange(businesses.Cast<object>().ToArray());
        combo.SelectedIndex = 0;
    }

    private static Label Label(string text, float size, Color color, FontStyle style) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = color,
        Font = new Font("Cascadia Mono", size, style),
        AutoEllipsis = true
    };

    private static Button Button(string text, bool primary) => new()
    {
        Text = text,
        Width = 124,
        Height = 34,
        BackColor = primary ? Color.FromArgb(100, 149, 237) : Color.FromArgb(40, 40, 80),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Font = new Font("Cascadia Mono", 9F, FontStyle.Bold),
        DialogResult = primary ? DialogResult.None : DialogResult.Cancel
    };

    internal static bool SelfTest()
    {
        using var dialog = new TextureBlacklistDialog(["a.ytd", "b.ytd"], 6, ["Aces and Eights"], null);
        dialog._applyAll.SelectedItem = "Aces and Eights";
        dialog.ApplyToAll();
        IReadOnlyList<TextureBlacklistAssignment> assigned = dialog.Assignments;
        using var restricted = new TextureBlacklistDialog(["c.ytd"], 8, ["Aces and Eights"], "Bean Machine");
        return assigned.Count == 2 &&
               assigned[0] is { TextureIndex: 6, Business: "Aces and Eights" } &&
               assigned[1] is { TextureIndex: 7, Business: "Aces and Eights" } &&
               restricted.Assignments[0] is { TextureIndex: 8, Business: null } &&
               restricted._grid.Columns[2].ReadOnly;
    }
}
