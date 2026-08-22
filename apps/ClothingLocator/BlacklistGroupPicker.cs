namespace BLRP.ClothingLocator;

internal static class BlacklistGroupPicker
{
    public static string? Pick(IWin32Window owner, IEnumerable<string> restrictions, string? current)
    {
        string[] options = restrictions
            .Append(current ?? string.Empty)
            .SelectMany(value => value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] selected = (current ?? string.Empty)
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        using var form = new Form
        {
            Text = "Combine blacklist groups",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(460, 560),
            BackColor = Color.FromArgb(12, 12, 28),
            ForeColor = Color.White,
            Font = new Font("Cascadia Mono", 9F),
            Padding = new Padding(20)
        };
        var list = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            BackColor = Color.FromArgb(20, 20, 40),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        list.Items.AddRange(options);
        for (int index = 0; index < options.Length; index++)
        {
            list.SetItemChecked(index, selected.Contains(options[index], StringComparer.OrdinalIgnoreCase));
        }

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.Controls.Add(new Label
        {
            Text = "SELECT TWO OR MORE GROUPS\nAccess is granted when any selected group matches.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(135, 206, 235),
            Font = new Font("Cascadia Mono", 9F, FontStyle.Bold)
        }, 0, 0);
        layout.Controls.Add(list, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var combine = CreateButton("COMBINE", Color.FromArgb(100, 149, 237));
        var cancel = CreateButton("CANCEL", Color.FromArgb(40, 40, 80));
        string? result = null;
        combine.Click += (_, _) =>
        {
            if (list.CheckedItems.Count < 2)
            {
                MessageBox.Show(form, "Select at least two groups.", "BLRP Clothing Utility", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            result = Combine(list.CheckedItems.Cast<string>());
            form.DialogResult = DialogResult.OK;
        };
        cancel.Click += (_, _) => form.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(combine);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 2);
        form.Controls.Add(layout);
        form.AcceptButton = combine;
        form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK ? result : null;
    }

    internal static string Combine(IEnumerable<string> groups) => string.Join('|', groups
        .Select(group => group.Trim())
        .Where(group => group.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group, StringComparer.OrdinalIgnoreCase));

    internal static bool SelfTest() => Combine(["LSFD", "LEO", "leo"]) == "LEO|LSFD";

    private static Button CreateButton(string text, Color color) => new()
    {
        Text = text,
        Width = 120,
        Height = 34,
        BackColor = color,
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Font = new Font("Cascadia Mono", 9F, FontStyle.Bold)
    };
}
