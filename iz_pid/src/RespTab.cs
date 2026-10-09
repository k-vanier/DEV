// Onglet "Réponse temporelle" : consigne (ordre donné) comparée au gyro, fenêtre par fenêtre,
// avec navigation de manœuvre en manœuvre et mini-vue du vol entier.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace BblTuner
{
    public partial class MainForm
    {
        TabPage tpRespPage;
        RadioButton[] rbRespAxis = new RadioButton[3];
        RadioButton[] rbRespWin = new RadioButton[5];
        Chart chResp, chOverview;
        Label lblRespMan, vSp, vGy, vTrack, vLag, vRms;
        CheckBox chkRespErr, chkRespTerms;
        FlightLog respLog;
        List<double> respMans = new List<double>();
        int respManIdx = -1;
        double respStart, respWinSec = 1;
        bool respOverviewDrag;

        static readonly double[] RespWindows = { 0.5, 1, 2, 5, 0 }; // 0 = tout le vol
        static readonly string[] RespWinLabels = { "0,5 s", "1 s", "2 s", "5 s", "Tout" };
        const int RespWinDefault = 1; // 1 s
        static readonly string[] TermNames = { "P", "I", "D", "F" };
        static readonly string[] TermFields = { "axisP[", "axisI[", "axisD[", "axisF[" };

        TabPage BuildRespTab()
        {
            TabPage tp = new TabPage("Réponse temporelle");
            tpRespPage = tp;

            FlowLayoutPanel bar = NewBar();
            FlowLayoutPanel gAxis = NewGroup();
            for (int a = 0; a < 3; a++)
            {
                RadioButton rb = Seg(Analyzer.AxisName(a), 64, "axis" + a);
                rb.Checked = a == 0;
                rb.CheckedChanged += (s, e) =>
                {
                    if (!((RadioButton)s).Checked) return;
                    DetectManeuvers(); SetupRespChart(); SetupOverview(); JumpMan(0, true);
                };
                rbRespAxis[a] = rb;
                gAxis.Controls.Add(rb);
            }
            FlowLayoutPanel gWin = NewGroup();
            for (int w = 0; w < RespWindows.Length; w++)
            {
                RadioButton rb = Seg(RespWinLabels[w], 50, null);
                rb.Checked = w == RespWinDefault;
                rb.CheckedChanged += (s, e) =>
                {
                    if (!((RadioButton)s).Checked) return;
                    bool fromAll = respWinSec >= RespDuration() - 1e-6;
                    double center = respStart + respWinSec / 2;
                    respWinSec = RespWin();
                    // en revenant de "Tout", on se replace sur la manœuvre en cours plutôt qu'au milieu du vol
                    if (fromAll && respMans.Count > 0) JumpMan(0, respManIdx < 0);
                    else SetRespStart(center - respWinSec / 2);
                };
                rbRespWin[w] = rb;
                gWin.Controls.Add(rb);
            }
            FlowLayoutPanel gNav = NewGroup();
            Button btnPrev = Btn("◀", 34), btnNext = Btn("▶", 34);
            lblRespMan = SubLabel("", 120);
            gNav.Controls.Add(btnPrev); gNav.Controls.Add(lblRespMan); gNav.Controls.Add(btnNext);
            FlowLayoutPanel gOpt = NewGroup();
            chkRespErr = Tog("Erreur", 70, true, null);
            chkRespTerms = Tog("Termes PID", 92, false, null);
            gOpt.Controls.Add(chkRespErr); gOpt.Controls.Add(chkRespTerms);
            bar.Controls.Add(gAxis); bar.Controls.Add(gWin); bar.Controls.Add(gNav); bar.Controls.Add(gOpt);

            FlowLayoutPanel cards = new FlowLayoutPanel { Dock = DockStyle.Top, Height = S(72), Padding = new Padding(S(10), S(8), S(10), S(8)), WrapContents = false };
            cards.Controls.Add(StatCard("Consigne max", out vSp));
            cards.Controls.Add(StatCard("Gyro filtré max", out vGy));
            cards.Controls.Add(StatCard("Suivi en crête", out vTrack));
            cards.Controls.Add(StatCard("Retard du gyro", out vLag));
            cards.Controls.Add(StatCard("Erreur RMS", out vRms));

            Panel body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(10), 0, S(10), S(10)) };
            chResp = NewThemedChart();
            chOverview = new SafeChart { Dock = DockStyle.Bottom, Height = S(74), MinimumSize = new Size(S(60), S(40)), AntiAliasing = AntiAliasingStyles.All, Cursor = Cursors.Hand };
            Panel gap = new Panel { Dock = DockStyle.Bottom, Height = S(8) };
            body.Controls.Add(chResp); body.Controls.Add(gap); body.Controls.Add(chOverview);

            tp.Controls.Add(body); tp.Controls.Add(cards); tp.Controls.Add(bar);

            chkRespErr.CheckedChanged += (s, e) => { SetupRespChart(); UpdateResp(); };
            chkRespTerms.CheckedChanged += (s, e) => { SetupRespChart(); UpdateResp(); };
            btnPrev.Click += (s, e) => JumpMan(-1, false);
            btnNext.Click += (s, e) => JumpMan(+1, false);
            chResp.MouseEnter += (s, e) => chResp.Focus();
            chResp.MouseWheel += (s, e) => SetRespStart(respStart - Math.Sign(e.Delta) * respWinSec * 0.15);
            chOverview.MouseDown += (s, e) => { respOverviewDrag = true; OverviewSeek(e.X); };
            chOverview.MouseMove += (s, e) => { if (respOverviewDrag) OverviewSeek(e.X); };
            chOverview.MouseUp += (s, e) => respOverviewDrag = false;

            Theme.Current.StyleControls(tp);
            return tp;
        }

        int RespAxis()
        {
            for (int a = 0; a < 3; a++) if (rbRespAxis[a].Checked) return a;
            return 0;
        }

        void ShowResp(FlightLog log)
        {
            respLog = (log != null && log.Parsed && log.Time != null && log.Time.Length > 10) ? log : null;
            respWinSec = RespWin();
            DetectManeuvers();
            SetupRespChart();
            SetupOverview();
            JumpMan(0, true);
        }

        double RespDuration() { return respLog == null ? 0 : respLog.Time[respLog.Time.Length - 1]; }

        double RespWin()
        {
            int idx = RespWinDefault;
            for (int w = 0; w < rbRespWin.Length; w++) if (rbRespWin[w].Checked) idx = w;
            double win = RespWindows[idx];
            double d = RespDuration();
            return (win <= 0 || win > d) ? Math.Max(d, 0.001) : win;
        }

        void SetRespStart(double t)
        {
            double max = Math.Max(0, RespDuration() - respWinSec);
            respStart = Math.Max(0, Math.Min(max, t));
            UpdateResp();
        }

        void OverviewSeek(int px)
        {
            if (respLog == null || chOverview.ChartAreas.Count == 0) return;
            double x;
            try { x = chOverview.ChartAreas[0].AxisX.PixelPositionToValue(px); }
            catch { return; }
            SetRespStart(x - respWinSec / 2);
        }

        /// Repère les ordres francs : variation de consigne rapide sur 20 ms.
        void DetectManeuvers()
        {
            respMans.Clear();
            respManIdx = -1;
            if (respLog == null) return;
            float[] sp = respLog.Col("setpoint[" + RespAxis() + "]");
            if (sp == null) return;
            double fs = respLog.SampleRate;
            int lag = Math.Max(1, (int)(0.02 * fs));
            double maxAbs = 0;
            foreach (float v in sp) maxAbs = Math.Max(maxAbs, Math.Abs(v));
            double thr = Math.Max(100, 0.25 * maxAbs);
            double[] t = respLog.Time;
            double last = double.MinValue;
            for (int i = lag; i < sp.Length; i++)
            {
                if (Math.Abs(sp[i] - sp[i - lag]) < thr) continue;
                double ts = t[i - lag];
                if (ts - last < 0.5) continue;
                respMans.Add(ts);
                last = ts;
            }
        }

        void JumpMan(int dir, bool reset)
        {
            if (respLog == null) { UpdateResp(); return; }
            if (respMans.Count == 0) { SetRespStart(reset ? 0 : respStart); return; }
            if (dir != 0 && rbRespWin[RespWindows.Length - 1].Checked)
                rbRespWin[RespWinDefault].Checked = true; // "Tout" n'a pas de sens pour naviguer : passe à 1 s
            if (reset) respManIdx = 0;
            else respManIdx = Math.Max(0, Math.Min(respMans.Count - 1, respManIdx + dir));
            SetRespStart(respMans[respManIdx] - respWinSec * 0.2);
        }

        void SetupRespChart()
        {
            if (chResp == null) return;
            Theme th = Theme.Current;
            ClearChart(chResp);
            int a = RespAxis();
            bool err = chkRespErr.Checked, terms = chkRespTerms.Checked;
            float wMain = 3, wErr = err ? 1.1f : 0, wTerms = terms ? 1.5f : 0;
            float avail = 92f, sum = wMain + wErr + wTerms, y = 0;

            float h = avail * wMain / sum;
            ChartArea main = AddArea(chResp, "r0", Analyzer.AxisName(a) + "  ·  vitesse de rotation (°/s)", 0, y, 100, h);
            y += h;
            Series sSp = AddSeries(chResp, "r0", "r0sp", null, null, Theme.Alpha(th.Setpoint, 220), 2, true);
            sSp.ChartType = SeriesChartType.Line;
            sSp.LegendText = "Consigne (ordre donné)";
            Series sGy = AddSeries(chResp, "r0", "r0gy", null, null, th.Axes[a], 2, true);
            sGy.ChartType = SeriesChartType.Line;
            sGy.LegendText = "Gyro filtré (réponse du drone)";
            List<ChartArea> areas = new List<ChartArea> { main };

            if (err)
            {
                h = avail * wErr / sum;
                ChartArea ea = AddArea(chResp, "r1", "Erreur  =  consigne − gyro (°/s)", 0, y, 100, h);
                y += h;
                Series se = AddSeries(chResp, "r1", "r1err", null, null, Theme.Alpha(th.Error, 80), 1, true);
                se.ChartType = SeriesChartType.Area;
                se.BorderColor = th.Error;
                se.BorderWidth = 1;
                se.LegendText = "Erreur";
                areas.Add(ea);
            }
            if (terms)
            {
                h = avail * wTerms / sum;
                ChartArea ta = AddArea(chResp, "r2", "Termes du PID (sortie)", 0, y, 100, h);
                y += h;
                Color[] tc = { th.Accent, Theme.Blend(th.Accent, th.Axes[0], 0.6), th.Axes[0], th.Axes[1] };
                for (int k = 0; k < 4; k++)
                {
                    if (respLog != null && respLog.Col(TermFields[k] + a + "]") == null) continue;
                    Series st = AddSeries(chResp, "r2", "r2" + TermNames[k], null, null, tc[k], 1, true);
                    st.ChartType = SeriesChartType.Line;
                    st.LegendText = "Terme " + TermNames[k];
                }
                areas.Add(ta);
            }
            foreach (ChartArea ca in areas)
            {
                ca.AxisX.LabelStyle.Format = "0.000";
                ca.AxisY.LabelStyle.Format = "0";
                if (ca != main) { ca.AlignWithChartArea = "r0"; ca.AlignmentOrientation = AreaAlignmentOrientations.Vertical; ca.AlignmentStyle = AreaAlignmentStyles.All; }
            }
            areas[areas.Count - 1].AxisX.Title = "temps (s)";
            th.StyleChart(chResp);
        }

        void SetupOverview()
        {
            if (chOverview == null) return;
            Theme th = Theme.Current;
            chOverview.Series.Clear(); chOverview.ChartAreas.Clear();
            ChartArea a = new ChartArea("ov");
            a.Position = new ElementPosition(0, 0, 100, 100);
            a.InnerPlotPosition = new ElementPosition(4, 6, 94, 66);
            a.AxisY.LabelStyle.Enabled = false;
            a.AxisY.MajorGrid.Enabled = false;
            a.AxisY.MajorTickMark.Enabled = false;
            a.AxisX.MajorGrid.Enabled = false;
            a.AxisX.LabelStyle.Font = new Font("Segoe UI", 7.5f);
            a.AxisX.LabelStyle.Format = "0' s'";
            chOverview.ChartAreas.Add(a);
            th.StyleChart(chOverview);
            if (respLog == null) return;
            int ax = RespAxis();
            a.AxisX.Minimum = 0;
            a.AxisX.Maximum = Math.Max(0.001, RespDuration());
            a.AxisX.Interval = NiceInterval(RespDuration() / 10);
            float[] sp = respLog.Col("setpoint[" + ax + "]"), gy = respLog.Col("gyroADC[" + ax + "]");
            double[] x, y;
            if (sp != null)
            {
                Dsp.Decimate(respLog.Time, sp, 1600, out x, out y);
                Series s = new Series("ovsp") { ChartArea = "ov", ChartType = SeriesChartType.Line, Color = Theme.Alpha(th.Setpoint, 110), BorderWidth = 1 };
                s.Points.DataBindXY(x, y);
                chOverview.Series.Add(s);
            }
            if (gy != null)
            {
                Dsp.Decimate(respLog.Time, gy, 1600, out x, out y);
                Series s = new Series("ovgy") { ChartArea = "ov", ChartType = SeriesChartType.Line, Color = Theme.Alpha(th.Axes[ax], 200), BorderWidth = 1 };
                s.Points.DataBindXY(x, y);
                chOverview.Series.Add(s);
            }
            a.AxisY.StripLines.Clear();
            a.AxisX.StripLines.Add(new StripLine { IntervalOffset = 0, StripWidth = 1, BackColor = Theme.Alpha(th.Accent, 60), BorderColor = th.Accent, BorderWidth = 1 });
        }

        static int LowerBound(double[] a, double v)
        {
            int lo = 0, hi = a.Length;
            while (lo < hi) { int m = (lo + hi) >> 1; if (a[m] < v) lo = m + 1; else hi = m; }
            return lo;
        }

        void BindSlice(string series, double[] t, float[] y, int i0, int i1)
        {
            Series s = chResp.Series.FindByName(series);
            if (s == null) return;
            s.Points.Clear();
            if (y == null || i1 <= i0) return;
            int n = i1 - i0;
            double[] ts = new double[n];
            float[] ys = new float[n];
            Array.Copy(t, i0, ts, 0, n);
            Array.Copy(y, i0, ys, 0, n);
            double[] xo, yo;
            Dsp.Decimate(ts, ys, 6000, out xo, out yo);
            s.Points.DataBindXY(xo, yo);
        }

        void SetCard(Label l, string text, Color c) { l.Text = text; l.ForeColor = c; }

        void UpdateResp()
        {
            if (chResp == null) return;
            Theme th = Theme.Current;
            if (respLog == null)
            {
                foreach (Series s in chResp.Series) s.Points.Clear();
                foreach (Label l in new[] { vSp, vGy, vTrack, vLag, vRms }) SetCard(l, "—", th.SubText);
                lblRespMan.Text = "Aucun vol";
                return;
            }
            int a = RespAxis();
            double[] t = respLog.Time;
            double t0 = respStart, t1 = t0 + respWinSec;
            int i0 = LowerBound(t, t0), i1 = Math.Min(t.Length, LowerBound(t, t1) + 1);
            float[] sp = respLog.Col("setpoint[" + a + "]");
            float[] gy = respLog.Col("gyroADC[" + a + "]");

            BindSlice("r0sp", t, sp, i0, i1);
            BindSlice("r0gy", t, gy, i0, i1);
            if (sp != null && gy != null && chkRespErr.Checked)
            {
                float[] err = new float[t.Length];
                for (int i = i0; i < i1; i++) err[i] = sp[i] - gy[i];
                BindSlice("r1err", t, err, i0, i1);
            }
            if (chkRespTerms.Checked)
                for (int k = 0; k < 4; k++) BindSlice("r2" + TermNames[k], t, respLog.Col(TermFields[k] + a + "]"), i0, i1);

            double span = t1 - t0;
            foreach (ChartArea ca in chResp.ChartAreas)
            {
                ca.AxisX.Minimum = t0; ca.AxisX.Maximum = t1;
                ca.AxisX.Interval = NiceInterval(span / 8);
                ca.AxisX.LabelStyle.Format = span < 2 ? "0.000" : span < 20 ? "0.0" : "0";
                ca.RecalculateAxesScale();
            }
            // échelle verticale minimale : sur un passage calme, le bruit résiduel ne doit pas remplir le graphique
            SetMinYSpan("r0", i0, i1, 100, sp, gy);
            if (sp != null && gy != null && chkRespErr.Checked)
            {
                float[] errSlice = new float[t.Length];
                for (int i = i0; i < i1; i++) errSlice[i] = sp[i] - gy[i];
                SetMinYSpan("r1", i0, i1, 50, errSlice, null);
            }
            // repères verticaux au début de chaque manœuvre visible
            if (chResp.ChartAreas.Count > 0)
            {
                ChartArea main = chResp.ChartAreas[0];
                main.AxisX.StripLines.Clear();
                foreach (double m in respMans)
                    if (m >= t0 && m <= t1)
                        main.AxisX.StripLines.Add(new StripLine { IntervalOffset = m, StripWidth = 0, BorderColor = Theme.Alpha(th.Accent, 140), BorderDashStyle = ChartDashStyle.Dash, BorderWidth = 1 });
            }
            // fenêtre sur la mini-vue
            if (chOverview.ChartAreas.Count > 0 && chOverview.ChartAreas[0].AxisX.StripLines.Count > 0)
            {
                StripLine sl = chOverview.ChartAreas[0].AxisX.StripLines[0];
                sl.IntervalOffset = t0;
                sl.StripWidth = Math.Max(span, RespDuration() / 400);
                chOverview.Invalidate();
            }

            // statistiques de la fenêtre
            if (sp == null || gy == null || i1 - i0 < 4)
            {
                foreach (Label l in new[] { vSp, vGy, vTrack, vLag, vRms }) SetCard(l, "—", th.SubText);
            }
            else
            {
                double maxSp = 0, maxGy = 0, se = 0;
                for (int i = i0; i < i1; i++)
                {
                    maxSp = Math.Max(maxSp, Math.Abs(sp[i]));
                    maxGy = Math.Max(maxGy, Math.Abs(gy[i]));
                    double e = sp[i] - gy[i];
                    se += e * e;
                }
                double rms = Math.Sqrt(se / (i1 - i0));
                SetCard(vSp, maxSp.ToString("0", CultureInfo.InvariantCulture) + " °/s", th.Text);
                SetCard(vGy, maxGy.ToString("0", CultureInfo.InvariantCulture) + " °/s", th.Axes[a]);
                SetCard(vRms, rms.ToString("0.0", CultureInfo.InvariantCulture) + " °/s", th.Text);
                if (maxSp > 20)
                {
                    double pct = maxGy / maxSp * 100;
                    Color c = (pct >= 95 && pct <= 110) ? th.Good : (pct >= 85 && pct <= 120) ? th.Warn : th.Bad;
                    SetCard(vTrack, pct.ToString("0", CultureInfo.InvariantCulture) + " %", c);
                }
                else SetCard(vTrack, "—", th.SubText);

                double fs = respLog.SampleRate;
                double lag = double.NaN;
                if (i1 - i0 > fs * 0.1 && maxSp > 20)
                {
                    int n = i1 - i0;
                    float[] a1 = new float[n], b1 = new float[n];
                    Array.Copy(sp, i0, a1, 0, n); Array.Copy(gy, i0, b1, 0, n);
                    bool[] mask = new bool[n];
                    for (int i = 0; i < n; i++) mask[i] = true;
                    lag = Dsp.LagMs(a1, b1, mask, fs, -15, 60);
                }
                if (double.IsNaN(lag)) SetCard(vLag, "—", th.SubText);
                else SetCard(vLag, lag.ToString("0.0", CultureInfo.InvariantCulture) + " ms", lag < 12 ? th.Good : lag < 20 ? th.Warn : th.Bad);
            }

            if (respMans.Count == 0) lblRespMan.Text = "Aucune manœuvre";
            else
            {
                int idx = respMans.FindIndex(m => m >= t0 && m <= t1);
                bool showingAll = respWinSec >= RespDuration() - 1e-6;
                if (idx >= 0 && !showingAll) respManIdx = idx; // en vue "Tout", on garde la manœuvre où l'on était
                lblRespMan.Text = idx >= 0 ? "Manœuvre " + (idx + 1) + " / " + respMans.Count : respMans.Count + " manœuvres";
            }
        }

        /// Fixe l'axe Y d'une zone pour couvrir les données et au moins [-minHalf, +minHalf].
        void SetMinYSpan(string area, int i0, int i1, double minHalf, float[] a, float[] b)
        {
            ChartArea ca = chResp.ChartAreas.FindByName(area);
            if (ca == null) return;
            double lo = -minHalf, hi = minHalf;
            for (int i = i0; i < i1; i++)
            {
                if (a != null) { lo = Math.Min(lo, a[i]); hi = Math.Max(hi, a[i]); }
                if (b != null) { lo = Math.Min(lo, b[i]); hi = Math.Max(hi, b[i]); }
            }
            double step = NiceInterval((hi - lo) / (area == "r0" ? 6 : 2)); // le petit graphique d'erreur n'a la place que pour 3 graduations
            ca.AxisY.Minimum = Math.Floor(lo / step) * step;
            ca.AxisY.Maximum = Math.Ceiling(hi / step) * step;
            ca.AxisY.Interval = step;
        }

        static double NiceInterval(double raw)
        {
            if (raw <= 0) return 1;
            double p = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double m = raw / p;
            return (m < 1.5 ? 1 : m < 3.5 ? 2 : m < 7.5 ? 5 : 10) * p;
        }
    }
}
