using System.Drawing;
using System.Windows.Forms;

internal static class BlrpTheme
{
    public static readonly Color Background = Color.FromArgb(12, 12, 28);
    public static readonly Color Card = Color.FromArgb(25, 25, 52);
    public static readonly Color Input = Color.FromArgb(40, 40, 80);
    public static readonly Color Accent = Color.FromArgb(100, 149, 237);
    public static readonly Color AccentLight = Color.FromArgb(135, 206, 235);

    public static void Apply(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Color.White;
        form.Font = new Font("Cascadia Mono", 9F);
        Apply(form.Controls);
    }

    private static void Apply(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            control.Font = new Font("Cascadia Mono", control.Font.Size, control.Font.Style);
            switch (control)
            {
                case Button button:
                    button.BackColor = Accent;
                    button.ForeColor = Color.White;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = AccentLight;
                    break;
                case TextBoxBase text:
                    text.BackColor = Input;
                    text.ForeColor = Color.White;
                    text.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case ComboBox combo:
                    combo.BackColor = Input;
                    combo.ForeColor = Color.White;
                    combo.FlatStyle = FlatStyle.Flat;
                    break;
                case NumericUpDown number:
                    number.BackColor = Input;
                    number.ForeColor = Color.White;
                    break;
                case DataGridView grid:
                    grid.BackgroundColor = Background;
                    grid.GridColor = Input;
                    grid.EnableHeadersVisualStyles = false;
                    grid.ColumnHeadersDefaultCellStyle.BackColor = Input;
                    grid.ColumnHeadersDefaultCellStyle.ForeColor = AccentLight;
                    grid.DefaultCellStyle.BackColor = Card;
                    grid.DefaultCellStyle.ForeColor = Color.White;
                    grid.DefaultCellStyle.SelectionBackColor = Accent;
                    break;
                case ListView list:
                    list.BackColor = Input;
                    list.ForeColor = Color.White;
                    break;
                case GroupBox group:
                    group.BackColor = Card;
                    group.ForeColor = AccentLight;
                    break;
                case Label label:
                    label.ForeColor = label.Font.Bold ? AccentLight : Color.White;
                    label.BackColor = Color.Transparent;
                    break;
                case CheckBox check:
                    check.ForeColor = Color.White;
                    check.BackColor = Color.Transparent;
                    break;
                case Panel or TableLayoutPanel or FlowLayoutPanel:
                    control.BackColor = Color.Transparent;
                    break;
            }
            if (control.HasChildren) Apply(control.Controls);
        }
    }
}
