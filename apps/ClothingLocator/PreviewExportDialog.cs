using System.Text.Json;

namespace BLRP.ClothingLocator;

internal sealed record PreviewExportSettings(string Root, string Output, string Blacklists, int GameBuild, int Size, bool IncludeRestricted,
    bool Upload = false, string BucketName = "", string PublicUrl = "");

internal sealed class PreviewExportDialog : Form
{
    private readonly TextBox _output = new() { Dock = DockStyle.Fill };
    private readonly TextBox _blacklists = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _build = new() { Minimum = 1604, Maximum = 9999, Value = 3095, Dock = DockStyle.Fill };
    private readonly ComboBox _size = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly CheckBox _restricted = new() { Text = "Include restricted previews for permitted players", Checked = true, AutoSize = true };
    private readonly CheckBox _upload = new() { Text = "Upload new / changed previews to Backblaze after exporting", AutoSize = true };
    private readonly TextBox _bucket = new() { Dock = DockStyle.Fill, Text = "blrp-cdn" };
    private readonly TextBox _keyId = new() { Dock = DockStyle.Fill, Text = Environment.GetEnvironmentVariable("BLRP_B2_KEY_ID") ?? "" };
    private readonly TextBox _applicationKey = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true,
        Text = Environment.GetEnvironmentVariable("BLRP_B2_APPLICATION_KEY") ?? "" };
    private readonly TextBox _publicUrl = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly Button _start = new() { Text = "EXPORT NEW / CHANGED", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Close", AutoSize = true };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { Text = "Rockstar + custom clothing · both genders · every registered texture", Dock = DockStyle.Fill, AutoSize = true };
    private readonly string _root;
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BLRP", "ClothingUtility", "preview-export.json");
    private CancellationTokenSource? _cancellation;

    public PreviewExportDialog(string root)
    {
        _root = Path.GetFullPath(root);
        Text = "Batch clothing previews";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 680);
        MinimumSize = new Size(720, 710);
        BackColor = Color.FromArgb(20, 20, 40);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9);
        string parent = Path.GetDirectoryName(_root)!;
        string store = Path.Combine(parent, "BadlandsRP", "resources", "blrp_clothingstore", "nui");
        _output.Text = Directory.Exists(store) ? Path.Combine(store, "previews") : Path.Combine(parent, "BLRP-Clothing-Previews");
        _blacklists.Text = Path.Combine(_root, "blrp_clothingstore", "blacklists");
        _size.Items.AddRange(new object[] { 128, 256, 384, 512 });
        _size.SelectedItem = 256;
        LoadSettings();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 3, RowCount = 15 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        foreach (int height in new[] { 24, 34, 24, 34, 26, 34, 38, 30, 60, 24, 34, 64, 30, 24, 42 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.Controls.Add(new Label { Text = "Output folder", AutoSize = true }, 0, 0);
        layout.Controls.Add(_output, 0, 1); layout.SetColumnSpan(_output, 2);
        layout.Controls.Add(BrowseButton(_output), 2, 1);
        layout.Controls.Add(new Label { Text = "Production blacklist folder", AutoSize = true }, 0, 2);
        layout.Controls.Add(_blacklists, 0, 3); layout.SetColumnSpan(_blacklists, 2);
        layout.Controls.Add(BrowseButton(_blacklists), 2, 3);
        layout.Controls.Add(new Label { Text = "Target server build (must match clothing IDs and blacklists)", AutoSize = true }, 0, 4);
        layout.Controls.Add(new Label { Text = "Image size", AutoSize = true }, 1, 4);
        layout.Controls.Add(_build, 0, 5);
        layout.Controls.Add(_size, 1, 5);
        layout.Controls.Add(new Label { Text = "WebP · alpha", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 2, 5);
        layout.Controls.Add(_restricted, 0, 6); layout.SetColumnSpan(_restricted, 3);
        layout.Controls.Add(_upload, 0, 7); layout.SetColumnSpan(_upload, 3);
        var account = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        account.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        account.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        account.Controls.Add(new Label { Text = "Backblaze bucket", AutoSize = true }, 0, 0);
        account.Controls.Add(new Label { Text = "Application key ID", AutoSize = true }, 1, 0);
        account.Controls.Add(_bucket, 0, 1);
        account.Controls.Add(_keyId, 1, 1);
        layout.Controls.Add(account, 0, 8); layout.SetColumnSpan(account, 3);
        layout.Controls.Add(new Label { Text = "Application key (not saved; can also use BLRP_B2_APPLICATION_KEY)", AutoSize = true }, 0, 9);
        layout.SetColumnSpan(layout.GetControlFromPosition(0, 9)!, 3);
        layout.Controls.Add(_applicationKey, 0, 10); layout.SetColumnSpan(_applicationKey, 3);
        layout.Controls.Add(_status, 0, 11); layout.SetColumnSpan(_status, 3);
        layout.Controls.Add(_publicUrl, 0, 12); layout.SetColumnSpan(_publicUrl, 3);
        layout.Controls.Add(_progress, 0, 13); layout.SetColumnSpan(_progress, 3);
        layout.Controls.Add(_start, 0, 14);
        layout.Controls.Add(_cancel, 2, 14);
        Controls.Add(layout);
        _start.Click += async (_, _) => await ExportAsync();
        _upload.CheckedChanged += (_, _) => _start.Text = _upload.Checked ? "UPDATE + UPLOAD" : "EXPORT NEW / CHANGED";
        _start.Text = _upload.Checked ? "UPDATE + UPLOAD" : "EXPORT NEW / CHANGED";
        _cancel.Click += (_, _) => { if (_cancellation != null) _cancellation.Cancel(); else Close(); };
        FormClosing += (_, e) =>
        {
            if (_cancellation == null) return;
            e.Cancel = true;
            _cancellation.Cancel();
        };
    }

    private void LoadSettings()
    {
        if (!File.Exists(SettingsPath)) return;
        try
        {
            PreviewExportSettings? settings = JsonSerializer.Deserialize<PreviewExportSettings>(File.ReadAllText(SettingsPath));
            if (settings == null || !string.Equals(settings.Root, _root, StringComparison.OrdinalIgnoreCase)) return;
            _output.Text = settings.Output;
            _blacklists.Text = settings.Blacklists;
            if (settings.GameBuild >= _build.Minimum && settings.GameBuild <= _build.Maximum) _build.Value = settings.GameBuild;
            if (_size.Items.Contains(settings.Size)) _size.SelectedItem = settings.Size;
            _restricted.Checked = settings.IncludeRestricted;
            _upload.Checked = settings.Upload;
            if (!string.IsNullOrWhiteSpace(settings.BucketName)) _bucket.Text = settings.BucketName;
            _publicUrl.Text = settings.PublicUrl;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _status.Text = "Could not load saved export settings. Check the folders and target build before exporting.";
        }
    }

    private Button BrowseButton(TextBox target)
    {
        var button = new Button { Text = "Browse...", Dock = DockStyle.Fill };
        button.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog { SelectedPath = target.Text, UseDescriptionForTitle = true, Description = "Select folder" };
            if (picker.ShowDialog(this) == DialogResult.OK) target.Text = picker.SelectedPath;
        };
        return button;
    }

    private async Task ExportAsync()
    {
        string output = _output.Text.Trim(), blacklistDirectory = _blacklists.Text.Trim();
        int build = (int)_build.Value, size = (int)_size.SelectedItem!;
        bool publicOnly = !_restricted.Checked;
        bool upload = _upload.Checked;
        string bucket = _bucket.Text.Trim(), keyId = _keyId.Text.Trim(), applicationKey = _applicationKey.Text.Trim();
        if (upload && (bucket == "" || keyId == "" || applicationKey == ""))
        {
            _status.Text = "Enter the bucket name, application key ID and application key before updating.";
            return;
        }
        _cancellation = new CancellationTokenSource();
        CancellationToken token = _cancellation.Token;
        _start.Enabled = false;
        _output.Enabled = _blacklists.Enabled = _build.Enabled = _size.Enabled = _restricted.Enabled = false;
        _upload.Enabled = _bucket.Enabled = _keyId.Enabled = _applicationKey.Enabled = false;
        _cancel.Text = "Cancel";
        _progress.Style = ProgressBarStyle.Marquee;
        _status.Text = "Indexing GTA archives and clothing metadata...";
        var progress = new Progress<ClothingPreviewProgress>(value =>
        {
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Maximum = Math.Max(1, value.Total);
            _progress.Value = Math.Min(value.Completed, _progress.Maximum);
            _status.Text = $"{value.Completed:N0}/{value.Total:N0} · {value.Written:N0} rendered · {value.Unchanged:N0} unchanged · {value.Restricted:N0} restricted · {value.Failed:N0} errors";
        });
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var settings = new PreviewExportSettings(_root, output, blacklistDirectory, build, size, !publicOnly, upload, bucket, _publicUrl.Text);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings));
            ClothingPreviewResult result = await ClothingPreviewExporter.RunAsync(_root, blacklistDirectory, output, build, size, publicOnly, progress, token);
            _status.Text = $"Complete · {result.Written:N0} rendered · {result.Unchanged:N0} unchanged · {result.Restricted:N0} excluded · {result.Failed:N0} errors.\n{result.ManifestPath}";
            if (upload)
            {
                _status.Text = "Checking existing Backblaze previews...";
                var uploadProgress = new Progress<PreviewUploadProgress>(value =>
                {
                    _progress.Maximum = Math.Max(1, value.Total);
                    _progress.Value = Math.Min(value.Completed, _progress.Maximum);
                    _status.Text = $"Upload {value.Completed:N0}/{value.Total:N0} · {value.Uploaded:N0} uploaded · {value.Unchanged:N0} unchanged";
                });
                PreviewUploadResult uploaded = await BackblazePreviewUpload.RunAsync(output, bucket, keyId, applicationKey, uploadProgress, token);
                _publicUrl.Text = uploaded.PublicUrl;
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings with { PublicUrl = uploaded.PublicUrl }));
                _status.Text = $"Complete · {uploaded.Uploaded:N0} uploaded · {uploaded.Unchanged:N0} unchanged · {result.Failed:N0} source texture errors.\nAdd setr clothing_preview_url \"URL below\" to the server config.";
            }
        }
        catch (OperationCanceledException) { _status.Text = "Cancelled. Completed previews are cached; run again to resume."; }
        catch (Exception exception) { _status.Text = exception.Message; }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            _start.Enabled = true;
            _output.Enabled = _blacklists.Enabled = _build.Enabled = _size.Enabled = _restricted.Enabled = true;
            _upload.Enabled = _bucket.Enabled = _keyId.Enabled = _applicationKey.Enabled = true;
            _cancel.Text = "Close";
            _progress.Style = ProgressBarStyle.Continuous;
        }
    }
}
