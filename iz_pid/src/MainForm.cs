using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace BblTuner
{
    public partial class MainForm : Form
    {
        public List<string> InitialFiles = new List<string>();
        public int InitialTab;

        readonly float dpi;
        ToolStripButton btnOpen;
        ToolStripLabel lblFile;
        ListView lvFlights, lvHeaders;
        TabControl tabs;
        DataGridView grid;
        RichTextBox rtb;
        Chart chStep, chNoise;
        TextBox txtCli;
        ToolStripStatusLabel lblStatus;
        ToolStripProgressBar prog;
        CheckBox chkNoiseUnf, chkNoiseDterm;

        string filePath;
        List<FlightLog> logs = new List<FlightLog>();
        Dictionary<int, FlightResult> results = new Dictionary<int, FlightResult>();
        FlightResult lastCombined;
        Recommendation lastRec;
        bool busy, suppressCheck;

        static readonly Color[] FlightColors = {
            Color.FromArgb(31,119,180), Color.FromArgb(255,127,14), Color.FromArgb(44,160,44), Color.FromArgb(214,39,40),
            Color.FromArgb(148,103,189), Color.FromArgb(140,86,75), Color.FromArgb(227,119,194), Color.FromArgb(127,127,127),
            Color.FromArgb(188,189,34), Color.FromArgb(23,190,207) };

        int S(int px) { return (int)Math.Round(px * dpi); }

        public MainForm()
        {
            using (Graphics g = CreateGraphics()) dpi = g.DpiX / 96f;
            Text = "BBL Tuner – analyse Blackbox Betaflight";
            Font = new Font("Segoe UI", 9f);
            Size = new Size(S(1400), S(880));
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) =>
            {
                string[] f = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (f != null && f.Length > 0) LoadFile(f[0]);
            };
            BuildUi();
            ApplyThemeAll();
            Shown += (s, e) => { ApplyThemeAll(); tabs.SelectedIndex = Math.Max(0, Math.Min(InitialTab, tabs.TabCount - 1)); foreach (string p in InitialFiles.OrderBy(x => BfConfig.LooksLikeConfig(x) ? 0 : 1)) LoadFile(p); };
        }

        // =============================================================== UI
        void BuildUi()
        {
            ToolStrip ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(S(6), S(4), S(6), S(4)), ImageScalingSize = new Size(S(16), S(16)) };
            btnOpen = new ToolStripButton("Ouvrir un fichier .bbl…");
            btnOpen.Click += (s, e) => OpenDialog();
            lblFile = new ToolStripLabel("Glisse un fichier .bbl / .bfl ici ou clique sur Ouvrir") { Tag = "sub" };
            ts.Items.AddRange(new ToolStripItem[] { btnOpen, new ToolStripSeparator(), lblFile });

            StatusStrip ss = new StatusStrip { SizingGrip = false };
            lblStatus = new ToolStripStatusLabel("Prêt") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            prog = new ToolStripProgressBar { Visible = false, Size = new Size(S(200), S(16)) };
            ss.Items.Add(lblStatus); ss.Items.Add(prog);

            SplitContainer split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };

            // ---- panneau gauche : vols + en-têtes
            SplitContainer left = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            lvFlights = new ListView { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false, MultiSelect = false };
            lvFlights.Columns.Add("Vol", S(55));
            lvFlights.Columns.Add("Durée", S(60));
            lvFlights.Columns.Add("Log", S(60));
            lvFlights.Columns.Add("PID Roll", S(120));
            lvFlights.Columns.Add("PID Pitch", S(120));
            lvFlights.Columns.Add("PID Yaw", S(100));
            lvFlights.ItemChecked += (s, e) => OnFlightChecked(e.Item);
            lvFlights.SelectedIndexChanged += (s, e) =>
            {
                // cliquer sur un vol le coche (et décoche les autres)
                if (!suppressCheck && !busy && lvFlights.SelectedItems.Count > 0 && !lvFlights.SelectedItems[0].Checked)
                    lvFlights.SelectedItems[0].Checked = true;
                ShowSelectedFlight();
            };
            ThemeListView(lvFlights);
            left.Panel1.Controls.Add(Section("VOLS DU FICHIER  ·  clique sur le vol à analyser", lvFlights));

            lvHeaders = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
            lvHeaders.Columns.Add("Paramètre", S(170));
            lvHeaders.Columns.Add("Valeur", S(330));
            ThemeListView(lvHeaders);
            left.Panel2.Controls.Add(Section("EN-TÊTE DU VOL SÉLECTIONNÉ", lvHeaders));
            split.Panel1.Controls.Add(left);
            split.SplitterWidth = 1;
            left.SplitterWidth = 1;

            // ---- onglets
            tabs = new ThemedTabControl { Dock = DockStyle.Fill, Padding = new Point(S(9), S(6)) };

            TabPage tpRec = new TabPage("Recommandations");
            grid = new DataGridView
            {
                Dock = DockStyle.Top, Height = S(150), ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BorderStyle = BorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
            };
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 10f);
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5f);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.RowTemplate.Height = S(32);
            foreach (string c in new[] { "Axe", "P", "I", "D", "D min / max", "Feedforward", "Verdict" }) grid.Columns.Add(c, c);
            grid.Columns[0].FillWeight = 60; grid.Columns[5].FillWeight = 125; grid.Columns[6].FillWeight = 90;
            rtb = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10f), DetectUrls = false };
            Panel recPad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(10)) };
            Panel gap = new Panel { Dock = DockStyle.Top, Height = S(10) };
            Panel rtbCard = new Panel { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(S(12), S(6), S(6), S(6)) };
            rtbCard.Controls.Add(rtb);
            recPad.Controls.Add(rtbCard); recPad.Controls.Add(gap); recPad.Controls.Add(grid);
            tpRec.Controls.Add(recPad);

            TabPage tpStep = new TabPage("Step response");
            chStep = MakeChart();
            Panel stepPad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(10)) };
            stepPad.Controls.Add(chStep);
            tpStep.Controls.Add(stepPad);

            TabPage tpTrace = BuildTraceTab();


            TabPage tpResp = BuildRespTab();

            TabPage tpNoise = new TabPage("Bruit");
            chNoise = MakeChart();
            FlowLayoutPanel np = NewBar();
            chkNoiseUnf = Tog("Gyro brut (non filtré)", 160, true, null);
            chkNoiseDterm = Tog("D-term", 80, true, null);
            chkNoiseUnf.CheckedChanged += (s, e) => ShowNoise();
            chkNoiseDterm.CheckedChanged += (s, e) => ShowNoise();
            np.Controls.Add(SubLabel("Vol sélectionné – afficher :", 0));
            np.Controls.Add(chkNoiseUnf); np.Controls.Add(chkNoiseDterm);
            Panel noisePad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(10)) };
            noisePad.Controls.Add(chNoise);
            tpNoise.Controls.Add(noisePad); tpNoise.Controls.Add(np);

            TabPage tpCli = new TabPage("CLI");
            txtCli = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 11f), WordWrap = false, BorderStyle = BorderStyle.None, Tag = "console" };
            FlowLayoutPanel cliBar = NewBar();
            Button btnCopy = Btn("Copier dans le presse-papiers", 220);
            btnCopy.Click += (s, e) => { if (txtCli.Text.Length > 0) { Clipboard.SetText(txtCli.Text); lblStatus.Text = "Commandes CLI copiées."; } };
            cliBar.Controls.Add(btnCopy);
            cliBar.Controls.Add(SubLabel("À coller dans l'onglet CLI de Betaflight Configurator.", 0));
            Panel cliPad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(10)) };
            Panel cliCard = new Panel { Dock = DockStyle.Fill, Tag = "console-card", Padding = new Padding(S(12), S(10), S(6), S(6)) };
            cliCard.Controls.Add(txtCli);
            cliPad.Controls.Add(cliCard);
            tpCli.Controls.Add(cliPad); tpCli.Controls.Add(cliBar);

            TabPage tpConfig = BuildConfigTab();

            TabPage tpHelp = new TabPage("Aide");
            RichTextBox help = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10f) };
            help.Text = HelpText;
            Panel hp = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(10)) };
            Panel helpCard = new Panel { Dock = DockStyle.Fill, Tag = "card", Padding = new Padding(S(14), S(10), S(6), S(6)) };
            helpCard.Controls.Add(help);
            hp.Controls.Add(helpCard);
            tpHelp.Controls.Add(hp);

            tabs.TabPages.AddRange(new[] { tpRec, tpStep, tpResp, tpTrace, tpNoise, tpCli, tpConfig, tpHelp });
            split.Panel2.Controls.Add(tabs);

            Controls.Add(split);
            Controls.Add(ts);
            Controls.Add(ss);
            Load += (s, e) => { split.SplitterDistance = S(520); left.SplitterDistance = left.Height / 2; };
        }

        Chart MakeChart()
        {
            Chart ch = new SafeChart { Dock = DockStyle.Fill, BackColor = Color.White, AntiAliasing = AntiAliasingStyles.All, TextAntiAliasingQuality = TextAntiAliasingQuality.High };
            Legend lg = new Legend("lg") { Docking = Docking.Bottom, Alignment = StringAlignment.Center, Font = new Font("Segoe UI", 9f) };
            ch.Legends.Add(lg);
            return ch;
        }

        ChartArea AddArea(Chart ch, string name, string title, float x, float y, float w, float h)
        {
            ChartArea a = new ChartArea(name);
            a.Position = new ElementPosition(x, y + 4f, w, h - 4f); // place pour le titre au-dessus
            a.AxisX.MajorGrid.LineColor = Color.FromArgb(230, 230, 230);
            a.AxisY.MajorGrid.LineColor = Color.FromArgb(230, 230, 230);
            a.AxisX.LabelStyle.Font = new Font("Segoe UI", 8f);
            a.AxisY.LabelStyle.Font = new Font("Segoe UI", 8f);
            a.AxisX.LineColor = Color.Gray; a.AxisY.LineColor = Color.Gray;
            a.AxisX.TitleFont = new Font("Segoe UI", 8.5f); a.AxisY.TitleFont = new Font("Segoe UI", 8.5f);
            ch.ChartAreas.Add(a);
            Title t = new Title(title) { DockedToChartArea = name, IsDockedInsideChartArea = false, Font = new Font("Segoe UI Semibold", 10f), Alignment = ContentAlignment.TopLeft };
            ch.Titles.Add(t);
            return a;
        }

        Series AddSeries(Chart ch, string area, string name, double[] x, double[] y, Color c, int width, bool legend)
        {
            Series s = new Series(name) { ChartArea = area, ChartType = SeriesChartType.FastLine, Color = c, BorderWidth = width, Legend = "lg", IsVisibleInLegend = legend };
            if (x != null && y != null && x.Length > 0) s.Points.DataBindXY(x, y);
            ch.Series.Add(s);
            return s;
        }

        static void ClearChart(Chart ch)
        {
            ch.Series.Clear(); ch.ChartAreas.Clear(); ch.Titles.Clear();
        }

        // =============================================================== chargement
        void OpenDialog()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "Logs Blackbox ou config Betaflight (*.bbl;*.bfl;*.txt)|*.bbl;*.bfl;*.txt;*.BBL;*.BFL|Tous les fichiers (*.*)|*.*";
                dlg.Title = "Ouvrir un log Blackbox Betaflight";
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadFile(dlg.FileName);
            }
        }

        async void LoadFile(string path)
        {
            if (BfConfig.LooksLikeConfig(path)) { LoadConfig(path); return; }
            if (busy) return;
            busy = true;
            SetUiBusy(true, "Lecture de " + Path.GetFileName(path) + "…");
            try
            {
                filePath = path;
                byte[] data = await Task.Run(() => File.ReadAllBytes(path));
                List<FlightLog> found = await Task.Run(() => BblReader.Split(data));
                if (found.Count == 0)
                {
                    ShowMessage("Aucun log Betaflight trouvé dans ce fichier.", false);
                    return;
                }
                Dictionary<int, FlightResult> res = new Dictionary<int, FlightResult>();
                prog.Maximum = found.Count; prog.Value = 0;
                foreach (FlightLog log in found)
                {
                    lblStatus.Text = "Décodage et analyse du vol " + log.Index + " / " + found.Count + "…";
                    FlightLog l = log;
                    FlightResult r = await Task.Run(() => { BblReader.Parse(data, l); return Analyzer.Analyze(l); });
                    res[log.Index] = r;
                    prog.Value = log.Index;
                }
                logs = found; results = res;
                lblFile.Text = Path.GetFileName(path) + "  –  " + found.Count + " vol(s)";
                FillFlightList();
            }
            catch (Exception ex)
            {
                ShowMessage(ex.Message, true);
            }
            finally
            {
                busy = false;
                SetUiBusy(false, null);
            }
            if (logs.Count > 0) UpdateCombined();
            if (config != null) { BuildProfileButtons(); RefreshConfig(); }
        }

        void SetUiBusy(bool b, string msg)
        {
            btnOpen.Enabled = !b;
            prog.Visible = b;
            UseWaitCursor = b;
            if (msg != null) lblStatus.Text = msg;
        }

        void FillFlightList()
        {
            suppressCheck = true;
            lvFlights.BeginUpdate();
            lvFlights.Items.Clear();
            foreach (FlightLog log in logs)
            {
                FlightResult r = results[log.Index];
                PidSet p = PidSet.FromHeaders(log);
                ListViewItem it = new ListViewItem("#" + log.Index);
                it.SubItems.Add(log.Parsed ? FormatDur(log.Duration) : "erreur");
                it.SubItems.Add(log.SampleRate > 0 ? (log.SampleRate / 1000).ToString("0.#", CultureInfo.InvariantCulture) + " kHz" : "—");
                it.SubItems.Add(p.Valid ? p.AxisString(0) : "?");
                it.SubItems.Add(p.Valid ? p.AxisString(1) : "?");
                it.SubItems.Add(p.Valid ? p.AxisString(2) : "?");
                it.Tag = log;
                bool usable = r.Error == null && r.FlyingSec >= 5 && r.Axes.Any(a => a.Windows >= 3);
                it.Checked = false;
                if (!usable) { it.Name = "off"; it.ForeColor = Theme.Current.SubText; it.ToolTipText = r.Error ?? "Vol trop court ou sans mouvement exploitable"; }
                lvFlights.Items.Add(it);
            }
            lvFlights.ShowItemToolTips = true;
            lvFlights.EndUpdate();
            suppressCheck = false;
            // un seul vol coché : le dernier vol exploitable
            ListViewItem sel = lvFlights.Items.Cast<ListViewItem>().LastOrDefault(i => i.Name != "off") ?? (lvFlights.Items.Count > 0 ? lvFlights.Items[lvFlights.Items.Count - 1] : null);
            if (sel != null)
            {
                suppressCheck = true;
                sel.Checked = true;
                sel.Selected = true;
                suppressCheck = false;
                sel.EnsureVisible();
            }
        }

        /// Un seul vol coché à la fois : cocher un vol décoche les autres et le sélectionne.
        void OnFlightChecked(ListViewItem item)
        {
            if (suppressCheck || busy) return;
            if (item.Checked)
            {
                suppressCheck = true;
                foreach (ListViewItem it in lvFlights.Items) if (it != item) it.Checked = false;
                item.Selected = true;
                suppressCheck = false;
                item.EnsureVisible();
                ShowSelectedFlight();
            }
            UpdateCombined();
        }

        static string FormatDur(double s)
        {
            int m = (int)(s / 60);
            return m + ":" + ((int)s % 60).ToString("00");
        }

        // =============================================================== analyse combinée
        void UpdateCombined()
        {
            List<FlightLog> checkedLogs = lvFlights.CheckedItems.Cast<ListViewItem>().Select(i => (FlightLog)i.Tag).OrderBy(l => l.Index).ToList();
            if (checkedLogs.Count == 0)
            {
                lastCombined = null; lastRec = null;
                grid.Rows.Clear(); rtb.Clear(); txtCli.Clear(); ClearChart(chStep);
                lblStatus.Text = "Aucun vol coché.";
                RefreshConfig();
                return;
            }
            // référence = le vol coché le plus récent ; on combine les vols ayant les mêmes PID
            FlightLog refLog = checkedLogs[checkedLogs.Count - 1];
            PidSet refPid = PidSet.FromHeaders(refLog);
            List<FlightLog> group = checkedLogs.Where(l => PidSet.FromHeaders(l).SameAs(refPid)).ToList();
            List<FlightLog> excluded = checkedLogs.Except(group).ToList();

            lastCombined = Analyzer.Combine(group.Select(l => results[l.Index]).ToList());
            lastRec = Recommender.Compute(lastCombined, refPid);
            if (excluded.Count > 0)
                lastRec.Lines.Insert(0, new RecLine(LineKind.Warn, "Les vols " + string.Join(", ", excluded.Select(l => "#" + l.Index)) +
                    " ont d'autres PID que le vol de référence #" + refLog.Index + " : ils sont affichés dans la step response pour comparaison, mais pas utilisés pour les propositions."));
            lastRec.Lines.Insert(0, new RecLine(LineKind.Info, "Base des propositions : vol(s) " + string.Join(", ", group.Select(l => "#" + l.Index)) + "  (fichier " + Path.GetFileName(filePath) + ")."));

            ShowRecommendation(lastRec, lastCombined);
            ShowSteps(checkedLogs, lastCombined);
            ShowNoise();
            txtCli.Text = lastRec.Cli.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
            lblStatus.Text = "Analyse terminée : vol #" + refLog.Index + ".";
            RefreshConfig();
        }

        void ShowRecommendation(Recommendation rec, FlightResult r)
        {
            Theme th = Theme.Current;
            grid.Rows.Clear();
            PidSet c = rec.Current, p = rec.Proposed;
            for (int a = 0; a < 3; a++)
            {
                AxisResult ax = r.Axes[a];
                string verdict;
                Color vc;
                if (ax.Step == null || ax.Windows < 3) { verdict = "Données insuffisantes"; vc = th.SubText; }
                else
                {
                    bool changed = c.P[a] != p.P[a] || c.I[a] != p.I[a] || c.D[a] != p.D[a] || c.F[a] != p.F[a];
                    if (!changed) { verdict = "Propre"; vc = th.Good; }
                    else if (Math.Abs(ax.Peak - 1.04) > 0.12) { verdict = "À corriger"; vc = th.Bad; }
                    else { verdict = "À affiner"; vc = th.Warn; }
                }
                int row = grid.Rows.Add(Analyzer.AxisName(a), Cell(c.P[a], p.P[a]), Cell(c.I[a], p.I[a]), Cell(c.D[a], p.D[a]),
                    c.HasDmin ? Cell(c.Dmin[a], p.Dmin[a]) : "—", c.HasF ? Cell(c.F[a], p.F[a]) : "—", verdict);
                DataGridViewRow gr = grid.Rows[row];
                gr.Cells[0].Style.Font = new Font("Segoe UI Semibold", 10.5f);
                gr.Cells[0].Style.ForeColor = th.Axes[a];
                ColorCell(gr.Cells[1], c.P[a], p.P[a]); ColorCell(gr.Cells[2], c.I[a], p.I[a]); ColorCell(gr.Cells[3], c.D[a], p.D[a]);
                if (c.HasDmin) ColorCell(gr.Cells[4], c.Dmin[a], p.Dmin[a]);
                if (c.HasF) ColorCell(gr.Cells[5], c.F[a], p.F[a]);
                gr.Cells[6].Style.ForeColor = vc;
                gr.Cells[6].Style.Font = new Font("Segoe UI Semibold", 10.5f);
            }
            grid.ClearSelection();

            rtb.Clear();
            foreach (RecLine l in rec.Lines)
            {
                Color col; FontStyle st = FontStyle.Regular; string prefix = "   ";
                float size = 10f;
                switch (l.Kind)
                {
                    case LineKind.Title: col = th.Text; st = FontStyle.Bold; size = 11.5f; prefix = "\n"; break;
                    case LineKind.Ok: col = th.Good; prefix = "   ✔ "; break;
                    case LineKind.Warn: col = th.Warn; prefix = "   ⚠ "; break;
                    case LineKind.Bad: col = th.Bad; prefix = "   ✖ "; break;
                    case LineKind.Action: col = th.Accent; st = FontStyle.Bold; prefix = "   "; break;
                    default: col = Theme.Blend(th.Text, th.SubText, 0.4); prefix = "   • "; break;
                }
                rtb.SelectionStart = rtb.TextLength;
                rtb.SelectionColor = col;
                rtb.SelectionFont = new Font("Segoe UI", size, st);
                rtb.AppendText(prefix + l.Text + "\n");
            }
            BeginInvoke((Action)(() => { rtb.Select(0, 0); rtb.ScrollToCaret(); }));
        }

        static string Cell(int cur, int pro)
        {
            if (cur == pro) return cur.ToString();
            int pct = cur != 0 ? (int)Math.Round((pro - cur) * 100.0 / cur) : 0;
            return cur + " → " + pro + "  (" + (pct >= 0 ? "+" : "") + pct + "%)";
        }

        static void ColorCell(DataGridViewCell c, int cur, int pro)
        {
            Theme th = Theme.Current;
            if (pro > cur) { c.Style.BackColor = Theme.Blend(th.Card, th.Good, 0.16); c.Style.ForeColor = th.Good; }
            else if (pro < cur) { c.Style.BackColor = Theme.Blend(th.Card, th.Warn, 0.16); c.Style.ForeColor = th.Warn; }
        }

        // =============================================================== graphiques
        void ShowSteps(List<FlightLog> checkedLogs, FlightResult combined)
        {
            ClearChart(chStep);
            double[] t = Enumerable.Range(0, Analyzer.RespMs + 1).Select(i => (double)i).ToArray();
            for (int a = 0; a < 3; a++)
            {
                string an = "s" + a;
                AxisResult ca = combined.Axes[a];
                string title = Analyzer.AxisName(a);
                if (ca.Step != null)
                    title += string.Format(CultureInfo.InvariantCulture, "   –   pic {0:0.00}  ·  latence {1:0.0} ms  ·  {2} fenêtres", ca.Peak, ca.Lat50Ms, ca.Windows);
                ChartArea area = AddArea(chStep, an, title, 0, a * 30f, 100, 30f);
                area.AxisX.Minimum = 0; area.AxisX.Maximum = Analyzer.RespMs; area.AxisX.Interval = 50;
                // échelle 0–1.6 par défaut, élargie si une courbe en sort
                double yMin = 0, yMax = 1.6;
                foreach (FlightLog lg in checkedLogs)
                {
                    double[] st = results[lg.Index].Axes[a].Step;
                    if (st == null) continue;
                    foreach (double v in st) { if (v > yMax) yMax = v; if (v < yMin) yMin = v; }
                }
                area.AxisY.Minimum = Math.Floor(yMin * 5) / 5;
                area.AxisY.Maximum = Math.Ceiling((yMax + 0.05) * 5) / 5;
                area.AxisY.Interval = area.AxisY.Maximum - area.AxisY.Minimum > 2.4 ? 0.4 : 0.2;
                area.AxisY.LabelStyle.Format = "0.0";
                if (a == 2) area.AxisX.Title = "temps (ms)";
                // bande cible 1.0 – 1.1
                StripLine sl = new StripLine { IntervalOffset = 0.98, StripWidth = 0.12, BackColor = Theme.Alpha(Theme.Current.Good, 40) };
                area.AxisY.StripLines.Add(sl);
                StripLine one = new StripLine { IntervalOffset = 1.0, StripWidth = 0, BorderColor = Theme.Current.SubText, BorderDashStyle = ChartDashStyle.Dash, BorderWidth = 1 };
                area.AxisY.StripLines.Add(one);

                int k = 0;
                foreach (FlightLog log in checkedLogs)
                {
                    FlightResult r = results[log.Index];
                    AxisResult ra = r.Axes[a];
                    Color col = FlightColors[k++ % FlightColors.Length];
                    if (ra.Step == null) continue;
                    string name = "Vol #" + log.Index + "  " + PidSet.FromHeaders(log).AxisString(a);
                    Series s = AddSeries(chStep, an, an + name, t, ra.Step, Color.FromArgb(checkedLogs.Count > 1 ? 150 : 255, col), checkedLogs.Count > 1 ? 1 : 2, a == 0);
                    s.LegendText = "Vol #" + log.Index;
                    s.ChartType = SeriesChartType.Line;
                }
                if (ca.Step != null && combined.Logs.Count > 1)
                {
                    Series s = AddSeries(chStep, an, an + "comb", t, ca.Step, Theme.Current.Text, 3, a == 0);
                    s.LegendText = "Combinaison (base des propositions)";
                    s.ChartType = SeriesChartType.Line;
                }
            }
            Theme.Current.StyleChart(chStep);
        }

        FlightLog SelectedLog()
        {
            if (lvFlights.SelectedItems.Count == 0) return null;
            return (FlightLog)lvFlights.SelectedItems[0].Tag;
        }

        void ShowSelectedFlight()
        {
            FlightLog log = SelectedLog();
            lvHeaders.BeginUpdate();
            lvHeaders.Items.Clear();
            if (log != null)
            {
                FlightResult r;
                results.TryGetValue(log.Index, out r);
                AddHeader("Firmware", log.Firmware);
                if (log.CraftName.Length > 0) AddHeader("Nom du quad", log.CraftName);
                AddHeader("Durée", FormatDur(log.Duration) + (r != null ? string.Format(CultureInfo.InvariantCulture, "  (en vol {0:0} s)", r.FlyingSec) : ""));
                AddHeader("Trames", log.FrameCount + (log.CorruptCount > 0 ? "  (" + log.CorruptCount + " corrompues ignorées)" : ""));
                AddHeader("Fréquence log", log.SampleRate.ToString("0", CultureInfo.InvariantCulture) + " Hz");
                if (r != null && r.Error != null) AddHeader("Analyse", r.Error);
                AddHeader("──────────", "");
                foreach (KeyValuePair<string, string> kv in log.Headers)
                {
                    if (kv.Key.StartsWith("Field ")) continue;
                    AddHeader(kv.Key, kv.Value);
                }
            }
            lvHeaders.EndUpdate();
            ShowTrace(log);
            ShowResp(log);
            ShowNoise();
        }

        void AddHeader(string k, string v)
        {
            ListViewItem it = new ListViewItem(k);
            it.SubItems.Add(v);
            lvHeaders.Items.Add(it);
        }

        void ShowNoise()
        {
            ClearChart(chNoise);
            FlightLog log = SelectedLog();
            FlightResult r = null;
            string src;
            if (log != null && results.TryGetValue(log.Index, out r)) src = "vol #" + log.Index;
            else { r = lastCombined; src = "combinaison"; }
            if (r == null || r.SegLen == 0) { Theme.Current.StyleChart(chNoise); return; }
            double[] f = r.Freqs();
            double fmax = Math.Min(r.Fs / 2, 1000);
            string[] names = { "Roll", "Pitch", "Yaw" };
            for (int a = 0; a < 3; a++)
            {
                string an = "n" + a;
                AxisResult ax = r.Axes[a];
                string title = names[a] + " – " + src + string.Format(CultureInfo.InvariantCulture,
                    "   ·   gyro filtré > 100 Hz : {0:0.00} °/s RMS", ax.GyroHF);
                ChartArea area = AddArea(chNoise, an, title, 0, a * 24f, 100, 24f);
                area.AxisX.Minimum = 0; area.AxisX.Maximum = fmax; area.AxisX.Interval = fmax > 500 ? 100 : 50;
                area.AxisY.Title = "dB";
                if (a > 0) { area.AlignWithChartArea = "n0"; area.AlignmentOrientation = AreaAlignmentOrientations.Vertical; area.AlignmentStyle = AreaAlignmentStyles.PlotPosition; }
                if (chkNoiseUnf.Checked && ax.PsdUnf != null) AddSeries(chNoise, an, an + "u", f, Db(ax.PsdUnf), Theme.Alpha(Theme.Current.SubText, 170), 1, a == 0).LegendText = "Gyro brut";
                if (ax.PsdGyro != null) AddSeries(chNoise, an, an + "g", f, Db(ax.PsdGyro), Theme.Current.Axes[a], 2, a == 0).LegendText = "Gyro filtré";
                if (chkNoiseDterm.Checked && ax.PsdDterm != null) AddSeries(chNoise, an, an + "d", f, Db(ax.PsdDterm), Theme.Alpha(Theme.Blend(Theme.Current.Accent, Theme.Current.Axes[0], 0.5), 200), 1, a == 0).LegendText = "D-term";
            }
            ChartArea ma = AddArea(chNoise, "n3", "Moteurs (moyenne) – " + src + string.Format(CultureInfo.InvariantCulture,
                "   ·   bruit > 100 Hz : {0:0.00} % RMS", r.MotorNoisePct), 0, 72f, 100, 22f);
            ma.AxisX.Minimum = 0; ma.AxisX.Maximum = fmax; ma.AxisX.Interval = fmax > 500 ? 100 : 50; ma.AxisX.Title = "fréquence (Hz)"; ma.AxisY.Title = "dB";
            ma.AlignWithChartArea = "n0"; ma.AlignmentOrientation = AreaAlignmentOrientations.Vertical; ma.AlignmentStyle = AreaAlignmentStyles.PlotPosition;
            if (r.PsdMotor != null) AddSeries(chNoise, "n3", "nm", f, Db(r.PsdMotor), Theme.Current.Text, 2, true).LegendText = "Moteurs";
            Theme.Current.StyleChart(chNoise);
        }

        static double[] Db(double[] p)
        {
            double[] r = new double[p.Length];
            for (int i = 0; i < p.Length; i++) r[i] = 10 * Math.Log10(Math.Max(p[i], 1e-9));
            r[0] = r.Length > 1 ? r[1] : r[0]; // composante continue sans intérêt
            return r;
        }

        const string HelpText =
@"COMMENT UTILISER BBL TUNER

1. Préparer le quad (Betaflight Configurator → Blackbox)
   • Débit de log : 2 kHz conseillé (1,6 à 4 kHz). En dessous de 1 kHz l'analyse du bruit est limitée.
   • Mode debug : GYRO_SCALED permet d'avoir le gyro brut (non filtré) dans le log, pour le spectre de bruit.
   • Efface la mémoire blackbox avant la session pour ne garder que les vols utiles.

2. Voler pour l'analyse (1 à 2 minutes par vol)
   • Des flips et rolls francs sur roll et pitch, des allers-retours rapides du manche, quelques coups de yaw.
   • Quelques montées plein gaz et redescentes (propwash) pour le bruit.
   • Garde les mêmes PID pendant un vol. Si tu changes de PID entre deux vols, le logiciel les compare.

3. Analyser
   • Ouvre le fichier .bbl (ou glisse-le sur la fenêtre). Chaque armement = un vol dans la liste.
   • Clique sur un vol pour l'analyser : un seul vol est coché à la fois. Le dernier vol exploitable est choisi à l'ouverture.
   • L'onglet Recommandations donne les PID proposés et les raisons. L'onglet CLI donne les commandes à coller dans Betaflight.
   • Onglet Config Betaflight : ouvre ta config (CLI « diff all » enregistré en .txt). Le logiciel choisit le profil qui correspond
     au vol, y écrit les PID proposés et enregistre une copie corrigée (l'original n'est jamais modifié).

4. Itérer
   • Applique, refais un vol identique, ré-analyse. Les changements sont limités à ±25 % par itération pour rester sûrs.
   • Vérifie toujours la température des moteurs après un changement de D.

LIRE LA STEP RESPONSE
   C'est la réponse du quad à un échelon de consigne, calculée par déconvolution (méthode de PID-Analyzer et PIDToolbox).
   • Pic idéal entre 1,00 et 1,10 (bande verte) : réponse rapide sans dépassement.
   • Pic > 1,10 : dépassement, il manque de la D (ou trop de P).
   • Pic < 0,98 : réponse sur-amortie, trop de D par rapport à P.
   • Vagues après le pic : oscillation, il manque de la D.
   • Courbe qui reste sous 1 longtemps : I trop faible. Qui reste au-dessus : I trop fort.
   • Latence : temps pour atteindre 50 % de la consigne. Plus c'est bas, plus le quad est vif.

RÔLE DES TERMES (Betaflight 4.x)
   • P : force de correction proportionnelle à l'erreur. Trop : oscillation rapide. Pas assez : mou.
   • I : corrige l'erreur accumulée et tient l'attitude (vent, centre de gravité). Trop : rebond lent.
   • D : amortissement, freine le dépassement et le propwash. Amplifie le bruit et chauffe les moteurs.
   • D min / D max : D dynamique, plus basse en vol calme et plus haute lors des mouvements rapides et du propwash.
   • Feedforward : réagit au mouvement du manche, pas à l'erreur. Réduit le retard du gyro sur la consigne.

LIMITES
   Les seuils sont des heuristiques issues des guides de tuning (Betaflight, PIDToolbox, Oscar Liang).
   Ils conviennent bien à un 5 pouces freestyle. Pour un cinewhoop, un long range ou un X-class, juge aussi au ressenti.
   Le filtrage (RPM, notch dynamique, lowpass) se règle avant les PID.";
    }
}
