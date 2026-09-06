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

        var chosen = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        var search = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "Search blacklist groups...", AccessibleName = "Search blacklist groups" };
        bool filtering = false;
        list.ItemCheck += (_, e) =>
        {
            if (filtering) return;
            string group = (string)list.Items[e.Index];
            if (e.NewValue == CheckState.Checked) chosen.Add(group); else chosen.Remove(group);
        };
        search.TextChanged += (_, _) =>
        {
            filtering = true;
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (string group in Filter(options, search.Text, null))
                    list.Items.Add(group, chosen.Contains(group));
            }
            finally { list.EndUpdate(); filtering = false; }
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.Controls.Add(new Label
        {
            Text = "SELECT TWO OR MORE GROUPS\nAccess is granted when any selected group matches.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(135, 206, 235),
            Font = new Font("Cascadia Mono", 9F, FontStyle.Bold)
        }, 0, 0);
        layout.Controls.Add(search, 0, 1);
        layout.Controls.Add(list, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var combine = CreateButton("COMBINE", Color.FromArgb(100, 149, 237));
        var cancel = CreateButton("CANCEL", Color.FromArgb(40, 40, 80));
        string? result = null;
        combine.Click += (_, _) =>
        {
            if (chosen.Count < 2)
            {
                MessageBox.Show(form, "Select at least two groups.", "BLRP Clothing Utility", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            result = Combine(chosen);
            form.DialogResult = DialogResult.OK;
        };
        cancel.Click += (_, _) => form.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(combine);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 3);
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

    internal static bool SelfTest() => Combine(["LSFD", "LEO", "leo"]) == "LEO|LSFD" &&
        Filter(["Angels of Death", "LEO", "LSFD"], "of de", "LEO").SequenceEqual(["Angels of Death", "LEO"]) &&
        Filter(["LEO", "LSFD"], "missing", null).Length == 0;

    // Keep the active choice visible so searching never silently changes import restrictions.
    internal static string[] Filter(IEnumerable<string> options, string query, string? selected) => options
        .Append(selected ?? string.Empty)
        .Where(value => value.Length > 0 && (value.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) || value == selected))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();

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
