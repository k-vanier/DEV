// Thème sombre de l'application (couleurs et style des contrôles).
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace BblTuner
{
    public class Theme
    {
        public bool IsDark;
        public Color Back, Card, Border, Plot, Grid, AxisLine, Text, SubText, Setpoint, Error, Accent, Good, Warn, Bad;
        public Color[] Axes;

        public static readonly Theme Dark = new Theme
        {
            IsDark = true,
            Back = C("#14161B"), Card = C("#1D2027"), Border = C("#2C313B"), Plot = C("#1A1D23"),
            Grid = C("#272B34"), AxisLine = C("#3A404B"), Text = C("#E6E8EB"), SubText = C("#9AA3AF"),
            Setpoint = C("#D9DCE1"), Error = C("#FBBF24"), Accent = C("#6E8BFF"),
            Good = C("#4ADE80"), Warn = C("#FBBF24"), Bad = C("#F87171"),
            Axes = new[] { C("#FF79C6"), C("#4ADE80"), C("#60A5FA") } // Roll rose, Pitch vert, Yaw bleu
        };

        public static Theme Current = Dark;

        static Color C(string hex) { return ColorTranslator.FromHtml(hex); }

        public static Color Alpha(Color c, int a) { return Color.FromArgb(a, c); }

        public static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        // ------------------------------------------------------------------ graphiques
        public void StyleChart(Chart ch)
        {
            ch.BackColor = Plot;
            foreach (ChartArea a in ch.ChartAreas) StyleArea(a);
            foreach (Title t in ch.Titles) t.ForeColor = Text;
            foreach (Legend l in ch.Legends) { l.BackColor = Color.Transparent; l.ForeColor = SubText; }
        }

        public void StyleArea(ChartArea a)
        {
            a.BackColor = Plot;
            a.BorderWidth = 0;
            foreach (Axis ax in new[] { a.AxisX, a.AxisY })
            {
                ax.LineColor = AxisLine;
                ax.MajorGrid.LineColor = Grid;
                ax.MajorTickMark.LineColor = AxisLine;
                ax.MajorTickMark.Size = 0.6f;
                ax.LabelStyle.ForeColor = SubText;
                ax.IsLabelAutoFit = false; // sinon les graduations rétrécissent jusqu'à devenir illisibles
                ax.LabelStyle.Font = new Font("Segoe UI", 8f);
                ax.TitleForeColor = SubText;
                ax.ScrollBar.BackColor = Card;
                ax.ScrollBar.ButtonColor = Border;
                ax.ScrollBar.LineColor = AxisLine;
            }
            a.CursorX.SelectionColor = Alpha(Accent, 70);
            a.CursorX.LineColor = Accent;
        }

        // ------------------------------------------------------------------ contrôles
        /// Tags reconnus : "card" / "bar" (fond carte), "sub" (texte secondaire), "value" (couleur gérée ailleurs),
        /// "title" (texte principal), "axis0".."axis2" (bouton à la couleur de l'axe).
        public void StyleControls(Control c)
        {
            string tag = c.Tag as string;
            if (c is TabPage) { ((TabPage)c).UseVisualStyleBackColor = false; c.BackColor = Back; }
            else if (c is Form) { c.BackColor = Back; c.ForeColor = Text; }
            else if (c is Chart) StyleChart((Chart)c);
            else if (c is ToolStrip)
            {
                ToolStrip ts = (ToolStrip)c;
                if (!(ts.Renderer is ThemeRenderer)) ts.Renderer = new ThemeRenderer();
                ts.BackColor = Card; ts.ForeColor = Text;
            }
            else if (c is SplitContainer) c.BackColor = Border; // le séparateur apparaît comme un filet
            else if (c is TabControl) c.BackColor = Back;
            else if (c is ListView)
            {
                ListView lv = (ListView)c;
                lv.BackColor = Card; lv.ForeColor = Text;
                foreach (ListViewItem it in lv.Items) { it.BackColor = Card; it.ForeColor = it.Name == "off" ? SubText : Text; }
            }
            else if (c is DataGridView)
            {
                DataGridView g = (DataGridView)c;
                g.EnableHeadersVisualStyles = false;
                g.BackgroundColor = Card; g.GridColor = Border;
                g.DefaultCellStyle.BackColor = Card; g.DefaultCellStyle.ForeColor = Text;
                g.DefaultCellStyle.SelectionBackColor = Card; g.DefaultCellStyle.SelectionForeColor = Text;
                g.ColumnHeadersDefaultCellStyle.BackColor = Back; g.ColumnHeadersDefaultCellStyle.ForeColor = SubText;
                g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Back;
                g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
                g.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            }
            else if (c is TextBoxBase)
            {
                if (tag == "console") { c.BackColor = Color.FromArgb(24, 26, 31); c.ForeColor = Color.FromArgb(220, 223, 228); }
                else { c.BackColor = Card; c.ForeColor = Text; }
            }
            else if (c is Panel) c.BackColor = tag == "console-card" ? Color.FromArgb(24, 26, 31) : (tag == "card" || tag == "bar") ? Card : Back;
            else if (c is Label)
            {
                c.BackColor = Color.Transparent;
                if (tag == "sub") c.ForeColor = SubText;
                else if (tag != "value") c.ForeColor = Text;
            }
            else if (c is ButtonBase) StyleButton((ButtonBase)c);
            foreach (Control k in c.Controls) StyleControls(k);
            c.Invalidate();
        }

        public void StyleButton(ButtonBase b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            bool on = (b is CheckBox && ((CheckBox)b).Checked) || (b is RadioButton && ((RadioButton)b).Checked);
            Color accent = Accent;
            string tag = b.Tag as string;
            if (tag != null && tag.StartsWith("axis")) accent = Axes[tag[4] - '0'];
            b.FlatAppearance.BorderSize = 1;
            if (on)
            {
                b.BackColor = accent;
                b.ForeColor = Color.White;
                b.FlatAppearance.BorderColor = accent;
                b.FlatAppearance.CheckedBackColor = accent;
                b.FlatAppearance.MouseOverBackColor = Blend(accent, Color.White, 0.12);
                b.FlatAppearance.MouseDownBackColor = Blend(accent, Color.Black, 0.12);
            }
            else
            {
                b.BackColor = Card;
                b.ForeColor = Text;
                b.FlatAppearance.BorderColor = Border;
                b.FlatAppearance.CheckedBackColor = accent;
                b.FlatAppearance.MouseOverBackColor = Blend(Card, accent, 0.15);
                b.FlatAppearance.MouseDownBackColor = Blend(Card, accent, 0.3);
            }
        }
    }
}
