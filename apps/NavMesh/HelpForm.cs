namespace BLRP.NavMesh;

internal sealed class HelpForm : Form
{
    private readonly ListBox topics = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
    private readonly RichTextBox content = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
        DetectUrls = false, BackColor = BlrpTheme.Card, ForeColor = Color.White, ScrollBars = RichTextBoxScrollBars.Vertical };
    private readonly Label title = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly List<(string Title, string Body)> pages = [];

    public HelpForm()
    {
        Text = "NavMesh — How to use";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 540);
        ClientSize = new Size(1060, 740);
        ShowInTaskbar = false;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Information;

        using var stream = typeof(HelpForm).Assembly.GetManifestResourceStream("BLRP.NavMesh.UserGuide.md")
            ?? throw new InvalidDataException("The built-in NavMesh guide is missing.");
        using var reader = new StreamReader(stream);
        string? heading = null;
        var lines = new List<string>();
        void AddPage()
        {
            if (heading != null) pages.Add((heading, string.Join(Environment.NewLine, lines).Trim()));
            lines.Clear();
        }
        foreach (string line in reader.ReadToEnd().Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("## ")) { AddPage(); heading = line[3..]; }
            else lines.Add(line);
        }
        AddPage();
        if (pages.Count == 0) throw new InvalidDataException("The built-in NavMesh guide is empty.");

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var headingLabel = new Label { Text = "NAVMESH HELP", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Cascadia Mono", 15, FontStyle.Bold) };
        layout.Controls.Add(headingLabel, 0, 0);
        layout.Controls.Add(title, 1, 0);
        topics.Margin = new Padding(0, 6, 16, 6);
        topics.HorizontalScrollbar = true;
        layout.Controls.Add(topics, 0, 1);
        var article = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = BlrpTheme.Card };
        article.Controls.Add(content); layout.Controls.Add(article, 1, 1);
        var note = new Label { Text = "Help stays open while you work.\nPress Escape to close.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        layout.Controls.Add(note, 0, 2);
        var close = new Button { Text = "CLOSE HELP", Dock = DockStyle.Right, Width = 130, DialogResult = DialogResult.Cancel, Margin = new Padding(3, 8, 3, 3) };
        close.Click += (_, _) => Close(); layout.Controls.Add(close, 1, 2);
        CancelButton = close;
        Controls.Add(layout);
        BlrpTheme.Apply(this);
        topics.BackColor = BlrpTheme.Background; topics.ForeColor = Color.White;
        topics.Font = new Font("Segoe UI", 10.5f); topics.ItemHeight = 32;
        title.Font = new Font("Segoe UI", 15, FontStyle.Bold);
        content.Font = new Font("Segoe UI", 11);
        content.BackColor = article.BackColor = BlrpTheme.Card;
        topics.Items.AddRange(pages.Select(p => p.Title).Cast<object>().ToArray());
        topics.SelectedIndexChanged += (_, _) =>
        {
            if (topics.SelectedIndex < 0) return;
            var page = pages[topics.SelectedIndex];
            title.Text = page.Title; content.Text = page.Body;
            content.Select(0, 0); content.ScrollToCaret();
        };
        topics.SelectedIndex = 0;
    }

    internal int TopicCount => pages.Count;
    internal string CurrentArticle => content.Text;
    internal void SelectTopic(int index) => topics.SelectedIndex = index;
}
