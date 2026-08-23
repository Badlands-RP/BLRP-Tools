using System.Diagnostics;

namespace Badlands.LiveryTool;

internal sealed class SignBatchBuilderForm : Form
{
    private readonly SignBatchWorkflow workflow = new();
    private readonly TextBox sourceFolderTextBox = new();
    private readonly TextBox outputFolderTextBox = new();
    private readonly TextBox sourcePrefixTextBox = new() { Text = "sign" };
    private readonly TextBox outputPrefixTextBox = new() { Text = "sign_livery_" };
    private readonly NumericUpDown startNumberInput = new() { Minimum = 0, Maximum = 99999, Value = 1 };
    private readonly CheckBox customTemplateCheckBox = new() { Text = "Use custom template XML", AutoSize = true };
    private readonly TextBox templatePathTextBox = new();
    private readonly TextBox templateTokenTextBox = new() { Text = "template" };
    private readonly Button templateBrowseButton = new() { Text = "Browse...", AutoSize = true };
    private readonly Button previewButton = new() { Text = "Preview and Validate", AutoSize = true };
    private readonly Button buildButton = new() { Text = "Build Ready-to-Use YFTs", AutoSize = true, Enabled = false };
    private readonly Button openOutputButton = new() { Text = "Open Output", AutoSize = true };
    private readonly Button closeButton = new() { Text = "Close", AutoSize = true };
    private readonly DataGridView grid = new();
    private readonly Label statusLabel = new() { AutoSize = true };
    private IReadOnlyList<SignBatchItem> plan = [];
    private bool isWorking;

    public SignBatchBuilderForm()
    {
        Text = "BLRP Sign Batch Builder";
        MinimumSize = new Size(900, 620);
        Size = new Size(1120, 720);
        StartPosition = FormStartPosition.CenterParent;
        string iconPath = Path.Combine(AppContext.BaseDirectory, "BLRP.ico");
        if (File.Exists(iconPath)) Icon = new Icon(iconPath);

        BuildLayout();
        BlrpTheme.Apply(this);
        ToggleCustomTemplate();
        FormClosing += (_, args) => args.Cancel = isWorking;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildInputs(), 0, 1);
        root.Controls.Add(BuildGrid(), 0, 2);
        root.Controls.Add(BuildActions(), 0, 3);
        Controls.Add(root);
    }

    private static Control BuildHeader()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        panel.Controls.Add(new Label
        {
            Text = "SIGN BATCH BUILDER",
            Dock = DockStyle.Fill,
            Font = new Font("Cascadia Mono", 18F, FontStyle.Bold),
            ForeColor = BlrpTheme.AccentLight,
        }, 0, 0);
        panel.Controls.Add(new Label
        {
            Text = "PNG / DDS  /  DXT5  /  COMPILED YFT",
            Dock = DockStyle.Fill,
            Font = new Font("Cascadia Mono", 8F, FontStyle.Bold),
            ForeColor = Color.White,
        }, 0, 1);
        return panel;
    }

    private Control BuildInputs()
    {
        var group = new GroupBox { Dock = DockStyle.Top, AutoSize = true, Text = "Batch Setup" };
        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 6,
            Padding = new Padding(10),
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddFolderRow(fields, 0, "Source folder", sourceFolderTextBox, BrowseSourceFolder);
        AddFolderRow(fields, 1, "Output folder", outputFolderTextBox, BrowseOutputFolder);
        AddTextField(fields, 2, 0, "Source prefix", sourcePrefixTextBox);
        AddTextField(fields, 2, 3, "Output prefix", outputPrefixTextBox);
        fields.Controls.Add(new Label { Text = "Start #", AutoSize = true, Padding = new Padding(10, 7, 8, 0) }, 0, 3);
        fields.Controls.Add(startNumberInput, 1, 3);

        customTemplateCheckBox.CheckedChanged += (_, _) => { ToggleCustomTemplate(); InvalidatePlan(); };
        fields.Controls.Add(customTemplateCheckBox, 3, 3);
        fields.SetColumnSpan(customTemplateCheckBox, 3);

        fields.Controls.Add(new Label { Text = "Template XML", AutoSize = true, Padding = new Padding(0, 7, 8, 0) }, 0, 4);
        templatePathTextBox.Dock = DockStyle.Fill;
        fields.Controls.Add(templatePathTextBox, 1, 4);
        fields.SetColumnSpan(templatePathTextBox, 4);
        templateBrowseButton.Click += (_, _) => BrowseTemplate();
        fields.Controls.Add(templateBrowseButton, 5, 4);

        fields.Controls.Add(new Label { Text = "Template token", AutoSize = true, Padding = new Padding(0, 7, 8, 0) }, 0, 5);
        templateTokenTextBox.Dock = DockStyle.Fill;
        fields.Controls.Add(templateTokenTextBox, 1, 5);

        var note = new Label
        {
            AutoSize = true,
            Text = "The built-in blank is used by default. Inputs must have power-of-two dimensions; PNG and non-DXT5 DDS files are converted to DXT5 with mipmaps.",
            Padding = new Padding(0, 10, 0, 4),
        };
        fields.Controls.Add(note, 0, 6);
        fields.SetColumnSpan(note, 6);

        foreach (Control control in new Control[] { sourceFolderTextBox, outputFolderTextBox, sourcePrefixTextBox, outputPrefixTextBox, templatePathTextBox, templateTokenTextBox })
        {
            control.TextChanged += (_, _) => InvalidatePlan();
        }
        startNumberInput.ValueChanged += (_, _) => InvalidatePlan();

        group.Controls.Add(fields);
        return group;
    }

    private Control BuildGrid()
    {
        var group = new GroupBox { Dock = DockStyle.Fill, Text = "Preview" };
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SignBatchItem.SourceName), HeaderText = "SOURCE", FillWeight = 42 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SignBatchItem.Input), HeaderText = "TYPE", FillWeight = 12 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SignBatchItem.TargetName), HeaderText = "OUTPUT", FillWeight = 30 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SignBatchItem.Status), HeaderText = "STATUS", FillWeight = 36 });
        group.Controls.Add(grid);
        return group;
    }

    private Control BuildActions()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 5, Padding = new Padding(0, 10, 0, 0) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusLabel.Text = "Choose source and output folders, then preview the batch.";
        panel.Controls.Add(statusLabel, 0, 0);
        previewButton.Click += (_, _) => PreviewBatch();
        panel.Controls.Add(previewButton, 1, 0);
        buildButton.Click += async (_, _) => await BuildBatchAsync();
        panel.Controls.Add(buildButton, 2, 0);
        openOutputButton.Click += (_, _) => OpenOutput();
        panel.Controls.Add(openOutputButton, 3, 0);
        closeButton.Click += (_, _) => Close();
        panel.Controls.Add(closeButton, 4, 0);
        return panel;
    }

    private void PreviewBatch()
    {
        try
        {
            plan = workflow.CreatePlan(
                sourceFolderTextBox.Text,
                outputFolderTextBox.Text,
                sourcePrefixTextBox.Text,
                outputPrefixTextBox.Text,
                (int)startNumberInput.Value);
            grid.DataSource = plan.ToArray();
            int blocked = plan.Count(item => !item.CanBuild);
            buildButton.Enabled = blocked == 0;
            statusLabel.Text = blocked == 0
                ? $"READY / {plan.Count} SIGN{(plan.Count == 1 ? string.Empty : "S")}"
                : $"BLOCKED / {blocked} ITEM{(blocked == 1 ? string.Empty : "S")} NEED ATTENTION";
            statusLabel.ForeColor = blocked == 0 ? BlrpTheme.AccentLight : Color.Orange;
        }
        catch (Exception ex)
        {
            InvalidatePlan();
            MessageBox.Show(this, ex.Message, "Sign batch preview", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task BuildBatchAsync()
    {
        try
        {
            ToggleWorking(true);
            SignBatchResult result = await Task.Run(() => workflow.Build(
                plan,
                outputFolderTextBox.Text,
                customTemplateCheckBox.Checked ? templatePathTextBox.Text : null,
                templateTokenTextBox.Text));
            statusLabel.Text = $"COMPLETE / {result.Files.Count / 2} YFTS / {result.Files.Count} FILES";
            statusLabel.ForeColor = BlrpTheme.AccentLight;
            buildButton.Enabled = false;
            MessageBox.Show(this, string.Join(Environment.NewLine, result.Messages), "Sign batch complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            statusLabel.Text = "BUILD FAILED";
            statusLabel.ForeColor = Color.Orange;
            MessageBox.Show(this, ex.Message, "Sign batch failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            ToggleWorking(false);
        }
    }

    private void BrowseSourceFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Select folder containing sign PNG or DDS files" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        sourceFolderTextBox.Text = dialog.SelectedPath;
        if (string.IsNullOrWhiteSpace(outputFolderTextBox.Text))
        {
            outputFolderTextBox.Text = Path.Combine(dialog.SelectedPath, "built");
        }
    }

    private void BrowseOutputFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = "Select a clean output folder" };
        if (dialog.ShowDialog(this) == DialogResult.OK) outputFolderTextBox.Text = dialog.SelectedPath;
    }

    private void BrowseTemplate()
    {
        using var dialog = new OpenFileDialog { Title = "Select sign YFT XML template", Filter = "YFT XML|*.yft.xml;*.xml|All files|*.*" };
        if (dialog.ShowDialog(this) == DialogResult.OK) templatePathTextBox.Text = dialog.FileName;
    }

    private void OpenOutput()
    {
        if (!Directory.Exists(outputFolderTextBox.Text))
        {
            MessageBox.Show(this, "The output folder does not exist yet.", "Open output", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetFullPath(outputFolderTextBox.Text)}\"") { UseShellExecute = true });
    }

    private void ToggleCustomTemplate()
    {
        templatePathTextBox.Enabled = customTemplateCheckBox.Checked;
        templateBrowseButton.Enabled = customTemplateCheckBox.Checked;
        templateTokenTextBox.Enabled = customTemplateCheckBox.Checked;
    }

    private void InvalidatePlan()
    {
        plan = [];
        grid.DataSource = null;
        buildButton.Enabled = false;
        statusLabel.Text = "PREVIEW REQUIRED";
        statusLabel.ForeColor = Color.White;
    }

    private void ToggleWorking(bool working)
    {
        isWorking = working;
        UseWaitCursor = working;
        previewButton.Enabled = !working;
        buildButton.Enabled = !working && plan.Count > 0 && plan.All(item => item.CanBuild);
        openOutputButton.Enabled = !working;
        closeButton.Enabled = !working;
    }

    private static void AddFolderRow(TableLayoutPanel fields, int row, string label, TextBox textBox, Action browse)
    {
        fields.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 7, 8, 0) }, 0, row);
        textBox.Dock = DockStyle.Fill;
        fields.Controls.Add(textBox, 1, row);
        fields.SetColumnSpan(textBox, 4);
        var button = new Button { Text = "Browse...", AutoSize = true };
        button.Click += (_, _) => browse();
        fields.Controls.Add(button, 5, row);
    }

    private static void AddTextField(TableLayoutPanel fields, int row, int column, string label, TextBox textBox)
    {
        fields.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(column == 0 ? 0 : 10, 7, 8, 0) }, column, row);
        textBox.Dock = DockStyle.Fill;
        fields.Controls.Add(textBox, column + 1, row);
        fields.SetColumnSpan(textBox, 2);
    }
}
