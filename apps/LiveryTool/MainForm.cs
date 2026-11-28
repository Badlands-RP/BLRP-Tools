namespace Badlands.LiveryTool;

// Vehicle metadata now uses the reviewed setup workflow in VehicleSetupForm.
internal sealed class MainForm : Form
{
    public MainForm()
    {
        Text = "Livery Tool — Texture & sign utilities";
        Size = new Size(900, 450); StartPosition = FormStartPosition.CenterParent;
        var status = new Label { Dock = DockStyle.Bottom, Height = 40 };
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24) };
        content.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(800, 0), Text = "Standalone image conversion is optional. The livery queue builds YFTs automatically. For carcols and carvariations, use Set up vehicle in the studio." });
        var convert = new LiveryButton { Text = "Image → DDS…" };
        convert.Click += async (_, _) =>
        {
            using var input = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.tga;*.bmp" };
            if (input.ShowDialog(this) != DialogResult.OK) return;
            using var output = new SaveFileDialog { Filter = "DDS|*.dds", FileName = Path.GetFileNameWithoutExtension(input.FileName) + ".dds" };
            if (output.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                convert.Enabled = false; status.Text = "Converting…";
                await Task.Run(() => new LiveryImageConverter().ConvertToDxt5Dds(input.FileName, output.FileName));
                status.Text = "Converted: " + output.FileName;
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { convert.Enabled = true; }
        };
        var signs = new LiveryButton { Text = "Sign batch tool…" };
        signs.Click += (_, _) => { using var form = new SignBatchBuilderForm(); form.ShowDialog(this); };
        content.Controls.AddRange([convert, signs]);
        Controls.Add(content); Controls.Add(status);
        LiveryTheme.Apply(this);
    }
}
