// Petits composants stylés partagés par les onglets "Réponse temporelle" et "Gyro / consigne".
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace BblTuner
{
    public partial class MainForm
    {

        FlowLayoutPanel NewBar()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
                Padding = new Padding(S(10), S(8), S(10), S(6)), Tag = "bar"
            };
        }

        FlowLayoutPanel NewGroup()
        {
            return new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 0, S(14), S(4)), Padding = Padding.Empty };
        }

        RadioButton Seg(string text, int width, string tag)
        {
            RadioButton rb = new RadioButton
            {
                Appearance = Appearance.Button, Text = text, TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(S(width), S(28)), Margin = new Padding(0, 0, S(3), 0), Tag = tag, Font = new Font("Segoe UI", 9f)
            };
            rb.CheckedChanged += (s, e) => Theme.Current.StyleButton(rb);
            return rb;
        }

        CheckBox Tog(string text, int width, bool on, string tag)
        {
            CheckBox cb = new CheckBox
            {
                Appearance = Appearance.Button, Text = text, TextAlign = ContentAlignment.MiddleCenter, Checked = on,
                Size = new Size(S(width), S(28)), Margin = new Padding(0, 0, S(3), 0), Tag = tag, Font = new Font("Segoe UI", 9f)
            };
            cb.CheckedChanged += (s, e) => Theme.Current.StyleButton(cb);
            return cb;
        }

        Button Btn(string text, int width)
        {
            return new Button { Text = text, Size = new Size(S(width), S(28)), Margin = new Padding(0, 0, S(3), 0), Font = new Font("Segoe UI", 9f) };
        }

        Label Spacer(int width) { return new Label { Width = S(width), Height = S(28), Margin = Padding.Empty }; }

        Label SubLabel(string text, int width)
        {
            return new Label
            {
                Text = text, Tag = "sub", AutoSize = width <= 0, Width = S(Math.Max(width, 1)), Height = S(28),
                TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0, 0, S(3), 0)
            };
        }

        /// Carte de statistique : petit titre + grande valeur.
        Panel StatCard(string title, out Label value)
        {
            Panel p = new Panel { Tag = "card", Size = new Size(S(150), S(56)), Margin = new Padding(0, 0, S(8), 0) };
            Label t = new Label { Text = title, Tag = "sub", AutoSize = false, Location = new Point(S(12), S(7)), Size = new Size(S(134), S(16)), Font = new Font("Segoe UI", 8.5f) };
            value = new Label { Text = "—", Tag = "value", AutoSize = false, Location = new Point(S(11), S(23)), Size = new Size(S(136), S(28)), Font = new Font("Segoe UI Semibold", 13f) };
            p.Controls.Add(t);
            p.Controls.Add(value);
            p.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(Theme.Current.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };
            return p;
        }

        Chart NewThemedChart()
        {
            Chart ch = new SafeChart { Dock = DockStyle.Fill, MinimumSize = new Size(S(60), S(60)), AntiAliasing = AntiAliasingStyles.All, TextAntiAliasingQuality = TextAntiAliasingQuality.High };
            Legend lg = new Legend("lg") { Docking = Docking.Top, Alignment = StringAlignment.Far, Font = new Font("Segoe UI", 9f), IsDockedInsideChartArea = false };
            ch.Legends.Add(lg);
            return ch;
        }

        /// Applique le thème courant à toute la fenêtre.
        void ApplyThemeAll()
        {
            Theme th = Theme.Current;
            th.StyleControls(this);
            if (IsHandleCreated)
            {
                Native.DarkTitle(this, th.IsDark);
                Native.ThemeTree(this, th.IsDark);
            }
            Invalidate(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitle(this, Theme.Current.IsDark);
        }

        /// Encadré avec titre (remplace les GroupBox, qui ne se laissent pas colorer).
        Control Section(string title, Control content)
        {
            Panel outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(8), S(4), S(8), S(8)) };
            Label head = new Label { Text = title, Tag = "sub", Dock = DockStyle.Top, Height = S(26), Font = new Font("Segoe UI Semibold", 9f), TextAlign = ContentAlignment.MiddleLeft };
            Panel card = new Panel { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(1) };
            card.Paint += (s, e) =>
            {
                using (Pen pen = new Pen(Theme.Current.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };
            content.Dock = DockStyle.Fill;
            card.Controls.Add(content);
            outer.Controls.Add(card);
            outer.Controls.Add(head);
            return outer;
        }

        /// En-têtes de colonnes dessinés aux couleurs du thème.
        void ThemeListView(ListView lv)
        {
            lv.BorderStyle = BorderStyle.None;
            lv.OwnerDraw = true;
            lv.DrawColumnHeader += (s, e) =>
            {
                Theme th = Theme.Current;
                using (SolidBrush b = new SolidBrush(th.Back)) e.Graphics.FillRectangle(b, e.Bounds);
                Rectangle tr = new Rectangle(e.Bounds.X + S(6), e.Bounds.Y, e.Bounds.Width - S(8), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, lv.Font, tr, th.SubText,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                using (Pen p = new Pen(th.Border))
                {
                    e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                    e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top + S(4), e.Bounds.Right - 1, e.Bounds.Bottom - S(5));
                }
            };
            lv.DrawItem += (s, e) => e.DrawDefault = false;
            lv.DrawSubItem += (s, e) =>
            {
                Theme th = Theme.Current;
                ListViewItem it = e.Item;
                bool sel = it.Selected;
                Color back = sel ? Theme.Blend(th.Card, th.Accent, lv.Focused ? 0.45 : 0.28) : th.Card;
                Color fore = it.Name == "off" ? th.SubText : th.Text;
                if (sel) fore = th.IsDark || lv.Focused ? Color.White : th.Text;
                using (SolidBrush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
                Rectangle tr = e.Bounds;
                if (e.ColumnIndex == 0 && lv.CheckBoxes)
                {
                    int sz = S(13);
                    Rectangle box = new Rectangle(e.Bounds.X + S(5), e.Bounds.Y + (e.Bounds.Height - sz) / 2, sz, sz);
                    if (it.Checked)
                    {
                        using (SolidBrush b = new SolidBrush(th.Accent)) e.Graphics.FillRectangle(b, box);
                        using (Pen p = new Pen(Color.White, Math.Max(1.5f, S(2))))
                        {
                            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                            e.Graphics.DrawLines(p, new[] {
                                new Point(box.X + sz * 2 / 10, box.Y + sz / 2), new Point(box.X + sz * 4 / 10, box.Y + sz * 3 / 4),
                                new Point(box.X + sz * 8 / 10, box.Y + sz * 3 / 10) });
                            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
                        }
                    }
                    else
                        using (Pen p = new Pen(th.SubText)) e.Graphics.DrawRectangle(p, box);
                    tr = new Rectangle(box.Right + S(6), e.Bounds.Y, e.Bounds.Right - box.Right - S(6), e.Bounds.Height);
                }
                else tr = new Rectangle(e.Bounds.X + S(6), e.Bounds.Y, e.Bounds.Width - S(8), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, lv.Font, tr, fore,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            };
            // redessine toute la liste quand la sélection, la coche ou le focus change
            // (sinon Windows ne repeint qu'une partie de la ligne)
            lv.GotFocus += (s, e) => lv.Invalidate();
            lv.LostFocus += (s, e) => lv.Invalidate();
            lv.ItemSelectionChanged += (s, e) => lv.Invalidate();
            lv.ItemChecked += (s, e) => lv.Invalidate();
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(lv, true, null);
        }

        void ShowMessage(string text, bool error) { ThemedDialog(text, error ? "Erreur" : "Attention", error, false); }

        bool AskYesNo(string text) { return ThemedDialog(text, "Confirmation", false, true); }

        /// Boîte de message aux couleurs du thème (barre de titre comprise).
        bool ThemedDialog(string text, string title, bool error, bool yesNo)
        {
            Theme th = Theme.Current;
            using (Form dlg = new Form())
            {
                dlg.Text = "BBL Tuner";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MinimizeBox = false; dlg.MaximizeBox = false; dlg.ShowInTaskbar = false;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.Font = new Font("Segoe UI", 9.5f);
                dlg.AutoSize = true; dlg.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                dlg.Padding = new Padding(S(18), S(16), S(18), S(14));
                TableLayoutPanel tl = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, RowCount = 3, Dock = DockStyle.Fill };
                Label head = new Label { Text = title, AutoSize = true, Tag = "value", ForeColor = error ? th.Bad : th.Warn, Font = new Font("Segoe UI Semibold", 11f), Margin = new Padding(0, 0, 0, S(6)) };
                Label msg = new Label { Text = text, AutoSize = true, MaximumSize = new Size(S(460), 0), Margin = new Padding(0, 0, 0, S(14)) };
                FlowLayoutPanel btns = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
                Button ok = new Button { Text = yesNo ? "Oui, écrire" : "OK", Size = new Size(S(yesNo ? 110 : 90), S(30)), DialogResult = DialogResult.OK };
                btns.Controls.Add(ok);
                if (yesNo)
                {
                    Button no = new Button { Text = "Annuler", Size = new Size(S(90), S(30)), DialogResult = DialogResult.Cancel };
                    btns.Controls.Add(no);
                    dlg.CancelButton = no;
                }
                tl.Controls.Add(head, 0, 0); tl.Controls.Add(msg, 0, 1); tl.Controls.Add(btns, 0, 2);
                dlg.Controls.Add(tl);
                dlg.AcceptButton = yesNo ? null : ok;
                th.StyleControls(dlg);
                dlg.HandleCreated += (s, e) => Native.DarkTitle(dlg, th.IsDark);
                return dlg.ShowDialog(this) == DialogResult.OK;
            }
        }
    }
}
