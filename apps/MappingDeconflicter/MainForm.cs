using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace YmapDeconflicter
{
    public partial class MainForm : Form
    {
        private Dictionary<string, List<string>> currentDuplicates = new();
        private Dictionary<string, bool> checkedItems = new();
        private ListView? resultsListView;
        private TextBox? detailsTextBox;
        private TextBox? pathTextBox;
        private TextBox? searchTextBox;
        private CheckBox? showOnlyUncheckedCheckBox;
        private ListViewColumnSorter? lvwColumnSorter;
        private string[] selectedExtensions = { ".ymap" };

        public MainForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "BLRP Mapping Deconflicter";
            this.Size = new System.Drawing.Size(1000, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new System.Drawing.Font("Segoe UI", 10);
            this.MinimumSize = new System.Drawing.Size(600, 400);
            this.AutoScaleMode = AutoScaleMode.Font;

            // Dark mode colors
            var darkBg = System.Drawing.Color.FromArgb(30, 30, 30);
            var darkFg = System.Drawing.Color.FromArgb(240, 240, 240);
            var darkBorder = System.Drawing.Color.FromArgb(50, 50, 50);
            var accentColor = System.Drawing.Color.FromArgb(0, 120, 215);
            var successColor = System.Drawing.Color.FromArgb(0, 150, 0);

            this.BackColor = darkBg;
            this.ForeColor = darkFg;

            // Title Label
            var titleLabel = new Label
            {
                Text = "MAPPING DECONFLICTER",
                Font = new System.Drawing.Font("Cascadia Mono", 16, System.Drawing.FontStyle.Bold),
                Location = new System.Drawing.Point(82, 20),
                Size = new System.Drawing.Size(400, 40),
                AutoSize = false,
                BackColor = darkBg,
                ForeColor = darkFg
            };

            // Path Selection
            var pathLabel = new Label
            {
                Text = "Target Directory:",
                Location = new System.Drawing.Point(20, 70),
                Size = new System.Drawing.Size(100, 25),
                BackColor = darkBg,
                ForeColor = darkFg
            };

            pathTextBox = new TextBox
            {
                Name = "pathTextBox",
                Location = new System.Drawing.Point(130, 70),
                Size = new System.Drawing.Size(500, 25),
                BackColor = System.Drawing.Color.FromArgb(45, 45, 45),
                ForeColor = darkFg,
                BorderStyle = BorderStyle.FixedSingle
            };

            var browseButton = new Button
            {
                Text = "Browse...",
                Location = new System.Drawing.Point(640, 70),
                Size = new System.Drawing.Size(80, 25),
                BackColor = accentColor,
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold)
            };
            browseButton.FlatAppearance.BorderColor = accentColor;
            browseButton.Click += (s, e) => BrowseFolder(pathTextBox);

            var loadButton = new Button
            {
                Text = "Load CSV/JSON",
                Location = new System.Drawing.Point(730, 70),
                Size = new System.Drawing.Size(90, 25),
                BackColor = accentColor,
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold)
            };
            loadButton.FlatAppearance.BorderColor = accentColor;
            loadButton.Click += (s, e) => LoadFromFile();

            var scanButton = new Button
            {
                Text = "Scan",
                Location = new System.Drawing.Point(20, 110),
                Size = new System.Drawing.Size(70, 35),
                Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold),
                BackColor = accentColor,
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat
            };
            scanButton.FlatAppearance.BorderColor = accentColor;
            scanButton.Click += (s, e) => ScanDirectory(pathTextBox?.Text ?? "");

            // File type selection panel
            var fileTypePanel = new GroupBox
            {
                Text = "File Types",
                Location = new System.Drawing.Point(100, 110),
                Size = new System.Drawing.Size(520, 40),
                BackColor = darkBg,
                ForeColor = darkFg,
                Font = new System.Drawing.Font("Segoe UI", 8)
            };

            string[] fileTypes = { ".ymap", ".ytyp", ".ybn", ".ydr", ".ytd", ".yft" };
            int xPos = 10;
            foreach (var ext in fileTypes)
            {
                var cb = new CheckBox
                {
                    Text = ext,
                    Location = new System.Drawing.Point(xPos, 16),
                    Size = new System.Drawing.Size(70, 18),
                    BackColor = darkBg,
                    ForeColor = darkFg,
                    Checked = ext == ".ymap",
                    Font = new System.Drawing.Font("Segoe UI", 8)
                };
                cb.CheckedChanged += (s, e) => UpdateSelectedExtensions();
                cb.Tag = ext;
                fileTypePanel.Controls.Add(cb);
                xPos += 75;
            }

            // Search box
            searchTextBox = new TextBox
            {
                Name = "searchTextBox",
                Location = new System.Drawing.Point(630, 110),
                Size = new System.Drawing.Size(150, 25),
                BackColor = System.Drawing.Color.FromArgb(45, 45, 45),
                ForeColor = darkFg,
                BorderStyle = BorderStyle.FixedSingle,
                PlaceholderText = "Search..."
            };
            searchTextBox.TextChanged += (s, e) => FilterResults();

            // Show only unchecked checkbox
            showOnlyUncheckedCheckBox = new CheckBox
            {
                Text = "Unchecked only",
                Location = new System.Drawing.Point(630, 140),
                Size = new System.Drawing.Size(120, 20),
                BackColor = darkBg,
                ForeColor = darkFg,
                Checked = false,
                Font = new System.Drawing.Font("Segoe UI", 8)
            };
            showOnlyUncheckedCheckBox.CheckedChanged += (s, e) => FilterResults();

            // Results Display
            var resultsLabel = new Label
            {
                Text = "Results:",
                Location = new System.Drawing.Point(20, 140),
                Size = new System.Drawing.Size(100, 25),
                Font = new System.Drawing.Font("Segoe UI", 11, System.Drawing.FontStyle.Bold),
                BackColor = darkBg,
                ForeColor = darkFg
            };

            resultsListView = new ListView
            {
                Name = "resultsListView",
                Location = new System.Drawing.Point(20, 170),
                Size = new System.Drawing.Size(820, 310),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = System.Drawing.Color.FromArgb(45, 45, 45),
                ForeColor = darkFg,
                BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                AllowColumnReorder = false
            };
            resultsListView.Columns.Add("✓", 30);
            resultsListView.Columns.Add("Type", 50);
            resultsListView.Columns.Add("Filename", 200);
            resultsListView.Columns.Add("Count", 60);
            resultsListView.Columns.Add("Paths", 480);

            lvwColumnSorter = new ListViewColumnSorter();
            resultsListView.ListViewItemSorter = lvwColumnSorter;
            resultsListView.ColumnClick += (s, e) => SortByColumn(e.Column);
            resultsListView.SelectedIndexChanged += (s, e) => UpdateDetails(resultsListView, detailsTextBox);

            // Details Display
            var detailsLabel = new Label
            {
                Text = "File Paths:",
                Location = new System.Drawing.Point(20, 525),
                Size = new System.Drawing.Size(100, 25),
                Font = new System.Drawing.Font("Segoe UI", 11, System.Drawing.FontStyle.Bold),
                BackColor = darkBg,
                ForeColor = darkFg,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            detailsTextBox = new TextBox
            {
                Name = "detailsTextBox",
                Location = new System.Drawing.Point(20, 555),
                Size = new System.Drawing.Size(820, 200),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = System.Drawing.Color.FromArgb(45, 45, 45),
                ForeColor = darkFg,
                BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            // Export Button
            var exportButton = new Button
            {
                Text = "Export All",
                Location = new System.Drawing.Point(20, 490),
                Size = new System.Drawing.Size(100, 25),
                Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold),
                BackColor = successColor,
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            exportButton.FlatAppearance.BorderColor = successColor;
            exportButton.Click += (s, e) => ExportToCSV(false);

            // Export Unchecked Button
            var exportUncheckedButton = new Button
            {
                Text = "Export Unchecked",
                Location = new System.Drawing.Point(130, 490),
                Size = new System.Drawing.Size(120, 25),
                Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold),
                BackColor = System.Drawing.Color.FromArgb(200, 100, 0),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            exportUncheckedButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(200, 100, 0);
            exportUncheckedButton.Click += (s, e) => ExportToCSV(true);

            // Open Target Button
            var openTargetButton = new Button
            {
                Text = "Open File (1st)",
                Location = new System.Drawing.Point(260, 490),
                Size = new System.Drawing.Size(100, 25),
                Font = new System.Drawing.Font("Segoe UI", 9),
                BackColor = accentColor,
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            openTargetButton.FlatAppearance.BorderColor = accentColor;
            openTargetButton.Click += (s, e) => OpenTarget();

            // Open Target Folder Button
            var openFolderButton = new Button
            {
                Text = "Open Folder (1st)",
                Location = new System.Drawing.Point(370, 490),
                Size = new System.Drawing.Size(120, 25),
                Font = new System.Drawing.Font("Segoe UI", 9),
                BackColor = accentColor,
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            openFolderButton.FlatAppearance.BorderColor = accentColor;
            openFolderButton.Click += (s, e) => OpenTargetFolder();

            var logo = new PictureBox
            {
                Image = Image.FromFile(Path.Combine(AppContext.BaseDirectory, "BLRP_Logo.png")),
                Location = new Point(20, 10),
                Size = new Size(52, 52),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };

            // Add controls
            this.Controls.Add(logo);
            this.Controls.Add(titleLabel);
            this.Controls.Add(pathLabel);
            this.Controls.Add(pathTextBox);
            this.Controls.Add(browseButton);
            this.Controls.Add(loadButton);
            this.Controls.Add(scanButton);
            this.Controls.Add(fileTypePanel);
            this.Controls.Add(searchTextBox);
            this.Controls.Add(showOnlyUncheckedCheckBox);
            this.Controls.Add(exportButton);
            this.Controls.Add(exportUncheckedButton);
            this.Controls.Add(resultsLabel);
            this.Controls.Add(resultsListView);
            this.Controls.Add(openTargetButton);
            this.Controls.Add(openFolderButton);
            this.Controls.Add(detailsLabel);
            this.Controls.Add(detailsTextBox);
            BlrpTheme.Apply(this);
        }

        private void BrowseFolder(TextBox pathTextBox)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    pathTextBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void ScanDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                MessageBox.Show("Please select a valid directory.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                if (resultsListView == null) return;

                resultsListView.Items.Clear();
                checkedItems.Clear();
                if (searchTextBox != null) searchTextBox.Clear();

                currentDuplicates = FindDuplicateFiles(path, selectedExtensions);

                if (currentDuplicates.Count == 0)
                {
                    string typeList = string.Join(", ", selectedExtensions);
                    MessageBox.Show($"No duplicate files found for types: {typeList}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                foreach (var group in currentDuplicates.OrderBy(x => x.Key))
                {
                    var item = new ListViewItem("☐");
                    string ext = Path.GetExtension(group.Key).ToUpper();
                    item.SubItems.Add(ext);
                    item.SubItems.Add(group.Key);
                    item.SubItems.Add(group.Value.Count.ToString());
                    item.SubItems.Add(string.Join("; ", group.Value));
                    item.Tag = group.Key;
                    resultsListView.Items.Add(item);
                    checkedItems[group.Key] = false;
                }

                resultsListView.MouseClick += (s, e) => HandleCheckboxClick(e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error scanning directory: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void HandleCheckboxClick(MouseEventArgs e)
        {
            if (resultsListView == null) return;

            var item = resultsListView.GetItemAt(e.X, e.Y);
            if (item != null && e.X < 40)
            {
                bool isNowChecked = item.Text == "☐";
                item.Text = isNowChecked ? "☑" : "☐";

                string filename = item.SubItems[2].Text;
                checkedItems[filename] = isNowChecked;
            }
        }



        private void UpdateDetails(ListView? listView, TextBox? detailsTextBox)
        {
            if (detailsTextBox == null || listView == null) return;

            detailsTextBox.Clear();

            if (listView.SelectedItems.Count > 0)
            {
                var selectedItem = listView.SelectedItems[0];
                string filename = selectedItem.SubItems[2].Text;

                if (currentDuplicates.TryGetValue(filename, out var paths) && paths != null)
                {
                    detailsTextBox.Text = string.Join(Environment.NewLine, paths);
                }
            }
        }

        private void ExportToCSV(bool uncheckedOnly)
        {
            if (currentDuplicates.Count == 0)
            {
                MessageBox.Show("No duplicates to export. Please scan first.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string suffix = uncheckedOnly ? "unchecked" : "all";
            string outputFile = Path.Combine(
                Directory.GetCurrentDirectory(),
                $"ymap_duplicates_{suffix}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv"
            );

            using (var writer = new StreamWriter(outputFile))
            {
                writer.WriteLine("Filename,Instance,Path,Status");

                foreach (var group in currentDuplicates.OrderBy(x => x.Key))
                {
                    bool isChecked = checkedItems.ContainsKey(group.Key) && checkedItems[group.Key];

                    if (uncheckedOnly && isChecked) continue;

                    for (int i = 0; i < group.Value.Count; i++)
                    {
                        string status = isChecked ? "Resolved" : "Pending";
                        writer.WriteLine($"\"{group.Key}\",{i + 1},\"{group.Value[i]}\",{status}");
                    }
                }
            }

            MessageBox.Show($"Results exported to:\n{outputFile}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void FilterResults()
        {
            if (resultsListView == null || searchTextBox == null || showOnlyUncheckedCheckBox == null) return;

            string searchTerm = searchTextBox.Text.ToLower();
            bool showOnlyUnchecked = showOnlyUncheckedCheckBox.Checked;

            resultsListView.Items.Clear();

            foreach (var group in currentDuplicates.OrderBy(x => x.Key))
            {
                bool isChecked = checkedItems.ContainsKey(group.Key) && checkedItems[group.Key];
                bool matchesSearch = string.IsNullOrEmpty(searchTerm) ||
                                    group.Key.ToLower().Contains(searchTerm) ||
                                    group.Value.Any(p => p.ToLower().Contains(searchTerm));

                if (!matchesSearch || (showOnlyUnchecked && isChecked)) continue;

                var item = new ListViewItem(isChecked ? "☑" : "☐");
                string ext = Path.GetExtension(group.Key).ToUpper();
                item.SubItems.Add(ext);
                item.SubItems.Add(group.Key);
                item.SubItems.Add(group.Value.Count.ToString());
                item.SubItems.Add(string.Join("; ", group.Value));
                item.Tag = group.Key;
                resultsListView.Items.Add(item);
            }
        }

        private void LoadFromFile()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "CSV files (*.csv)|*.csv|JSON files (*.json)|*.json|All files (*.*)|*.*";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    MessageBox.Show("Load functionality coming soon!", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void OpenTarget()
        {
            if (resultsListView?.SelectedItems.Count > 0)
            {
                var selectedItem = resultsListView.SelectedItems[0];
                string filename = selectedItem.SubItems[2].Text;

                if (currentDuplicates.TryGetValue(filename, out var paths) && paths?.Count > 0)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = paths[0],
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error opening file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void OpenTargetFolder()
        {
            if (resultsListView?.SelectedItems.Count > 0)
            {
                var selectedItem = resultsListView.SelectedItems[0];
                string filename = selectedItem.SubItems[2].Text;

                if (currentDuplicates.TryGetValue(filename, out var paths) && paths?.Count > 0)
                {
                    try
                    {
                        string folderPath = Path.GetDirectoryName(paths[0]) ?? "";
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = folderPath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error opening folder: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void SortByColumn(int column)
        {
            if (lvwColumnSorter == null) return;

            if (column == lvwColumnSorter.SortColumn)
            {
                lvwColumnSorter.Order = lvwColumnSorter.Order == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            }
            else
            {
                lvwColumnSorter.SortColumn = column;
                lvwColumnSorter.Order = SortOrder.Ascending;
            }

            resultsListView?.Sort();
        }

        private void UpdateSelectedExtensions()
        {
            var extensions = new List<string>();
            var fileTypePanel = this.Controls.OfType<GroupBox>().FirstOrDefault();

            if (fileTypePanel != null)
            {
                foreach (Control control in fileTypePanel.Controls)
                {
                    if (control is CheckBox cb && cb.Checked && cb.Tag is string ext)
                    {
                        extensions.Add(ext);
                    }
                }
            }

            selectedExtensions = extensions.Count > 0 ? extensions.ToArray() : new[] { ".ymap" };
        }

        private Dictionary<string, List<string>> FindDuplicateFiles(string rootPath, string[] extensions)
        {
            var duplicates = new Dictionary<string, List<string>>();
            var filesByName = new Dictionary<string, List<string>>();

            try
            {
                var files = Directory.GetFiles(rootPath, "*.*", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    string ext = Path.GetExtension(file).ToLower();
                    if (!extensions.Contains(ext)) continue;

                    string filename = Path.GetFileName(file);
                    if (!filesByName.ContainsKey(filename))
                    {
                        filesByName[filename] = new List<string>();
                    }
                    filesByName[filename].Add(file);
                }

                foreach (var group in filesByName.Where(x => x.Value.Count > 1))
                {
                    duplicates[group.Key] = group.Value.OrderBy(x => x).ToList();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error scanning files: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            return duplicates;
        }
    }
}
