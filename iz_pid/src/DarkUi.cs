// Briques pour appliquer le thème à toute l'application : barre de titre Windows, barres de défilement,
// onglets dessinés à la main, barres d'outils et d'état.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BblTuner
{
    static class Native
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")]
        static extern bool RedrawWindow(IntPtr h, IntPtr rect, IntPtr rgn, uint flags);

        /// Barre de titre sombre (Windows 10 20H1+ : attribut 20, versions antérieures : 19).
        public static void DarkTitle(Form f, bool dark)
        {
            if (!f.IsHandleCreated) return;
            try
            {
                int v = dark ? 1 : 0;
                if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref v, 4);
                // Windows 10 ne repeint pas la barre de titre tout seul : on simule une désactivation/réactivation
                bool active = Form.ActiveForm == f;
                SendMessage(f.Handle, 0x0086, active ? IntPtr.Zero : new IntPtr(1), IntPtr.Zero); // WM_NCACTIVATE
                SendMessage(f.Handle, 0x0086, active ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);
                RedrawWindow(f.Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0400 | 0x0100); // INVALIDATE | FRAME | UPDATENOW
            }
            catch { }
        }

        static void SetTheme(IntPtr h, string name)
        {
            try { SetWindowTheme(h, name, null); } catch { }
        }

        /// Barres de défilement et en-têtes sombres pour les contrôles Windows natifs.
        public static void ThemeTree(Control c, bool dark)
        {
            if (c.IsHandleCreated)
            {
                if (c is ListView)
                {
                    // en clair : thème Windows par défaut (le thème "Explorer" repeint la sélection par-dessus nos lignes)
                    SetTheme(c.Handle, dark ? "DarkMode_Explorer" : null);
                    IntPtr header = SendMessage(c.Handle, 0x101F, IntPtr.Zero, IntPtr.Zero); // LVM_GETHEADER
                    if (header != IntPtr.Zero) SetTheme(header, dark ? "DarkMode_ItemsView" : "Explorer");
                }
                else if (c is TextBoxBase)
                    SetTheme(c.Handle, (dark || "console".Equals(c.Tag)) ? "DarkMode_Explorer" : "Explorer");
                else if (c is ScrollBar)
                    SetTheme(c.Handle, dark ? "DarkMode_Explorer" : "Explorer");
            }
            foreach (Control k in c.Controls) ThemeTree(k, dark);
        }
    }

    /// Graphique qui ignore les tailles nulles (la librairie lève une exception sinon, pendant la mise en page).
    public class SafeChart : System.Windows.Forms.DataVisualization.Charting.Chart
    {
        protected override void OnResize(EventArgs e)
        {
            if (Width > 0 && Height > 0) base.OnResize(e);
        }
    }

    /// Onglets entièrement dessinés aux couleurs du thème.
    public class ThemedTabControl : TabControl
    {
        public ThemedTabControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SelectedIndexChanged += (s, e) => Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme th = Theme.Current;
            Graphics g = e.Graphics;
            g.Clear(th.Back);
            Rectangle page = DisplayRectangle;
            using (Pen p = new Pen(th.Border))
                g.DrawLine(p, 0, page.Top - 2, Width, page.Top - 2);
            for (int i = 0; i < TabCount; i++)
            {
                Rectangle r = GetTabRect(i);
                bool sel = i == SelectedIndex;
                if (sel)
                {
                    using (SolidBrush b = new SolidBrush(th.Card)) g.FillRectangle(b, r.X, r.Y, r.Width, r.Height + 1);
                    using (SolidBrush b = new SolidBrush(th.Accent)) g.FillRectangle(b, r.X, r.Bottom - 2, r.Width, 3);
                }
                TextRenderer.DrawText(g, TabPages[i].Text, Font, r, sel ? th.Text : th.SubText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }
    }

    class ThemeColors : ProfessionalColorTable
    {
        static Theme T { get { return Theme.Current; } }
        static Color Hover { get { return Theme.Blend(T.Card, T.Accent, 0.18); } }
        static Color Press { get { return Theme.Blend(T.Card, T.Accent, 0.32); } }
        public override Color ToolStripGradientBegin { get { return T.Card; } }
        public override Color ToolStripGradientMiddle { get { return T.Card; } }
        public override Color ToolStripGradientEnd { get { return T.Card; } }
        public override Color ToolStripBorder { get { return T.Border; } }
        public override Color StatusStripGradientBegin { get { return T.Card; } }
        public override Color StatusStripGradientEnd { get { return T.Card; } }
        public override Color SeparatorDark { get { return T.Border; } }
        public override Color SeparatorLight { get { return T.Card; } }
        public override Color ButtonSelectedHighlight { get { return Hover; } }
        public override Color ButtonSelectedGradientBegin { get { return Hover; } }
        public override Color ButtonSelectedGradientMiddle { get { return Hover; } }
        public override Color ButtonSelectedGradientEnd { get { return Hover; } }
        public override Color ButtonSelectedBorder { get { return T.Accent; } }
        public override Color ButtonPressedGradientBegin { get { return Press; } }
        public override Color ButtonPressedGradientMiddle { get { return Press; } }
        public override Color ButtonPressedGradientEnd { get { return Press; } }
        public override Color ButtonPressedBorder { get { return T.Accent; } }
        public override Color ButtonCheckedGradientBegin { get { return Press; } }
        public override Color ButtonCheckedGradientMiddle { get { return Press; } }
        public override Color ButtonCheckedGradientEnd { get { return Press; } }
        public override Color ButtonCheckedHighlight { get { return Press; } }
    }

    class ThemeRenderer : ToolStripProfessionalRenderer
    {
        public ThemeRenderer() : base(new ThemeColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            Theme th = Theme.Current;
            if (!e.Item.Enabled) e.TextColor = Theme.Blend(th.SubText, th.Card, 0.4);
            else e.TextColor = "sub".Equals(e.Item.Tag) ? th.SubText : th.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen p = new Pen(Theme.Current.Border))
            {
                if (e.ToolStrip is StatusStrip) e.Graphics.DrawLine(p, 0, 0, e.ToolStrip.Width, 0);
                else e.Graphics.DrawLine(p, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
            }
        }
    }
}
