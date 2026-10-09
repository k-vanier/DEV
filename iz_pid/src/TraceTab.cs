// Onglet "Gyro / consigne" : vue d'ensemble du vol (consigne et gyro sur les 3 axes, gaz et moteurs).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace BblTuner
{
    public partial class MainForm
    {
        TabPage tpTracePage;
        Chart chTrace;
        CheckBox[] chkTraceShow = new CheckBox[4];
        Label lblTraceTitle;

        TabPage BuildTraceTab()
        {
            TabPage tp = new TabPage("Gyro / consigne");
            tpTracePage = tp;

            FlowLayoutPanel bar = NewBar();
            lblTraceTitle = new Label { Tag = "title", AutoSize = true, Font = new Font("Segoe UI Semibold", 11f), Margin = new Padding(0, S(4), S(16), 0) };
            bar.Controls.Add(lblTraceTitle);
            FlowLayoutPanel gShow = NewGroup();
            string[] names = { "Roll", "Pitch", "Yaw", "Gaz et moteurs" };
            for (int k = 0; k < 4; k++)
            {
                CheckBox cb = Tog(names[k], k < 3 ? 64 : 116, true, k < 3 ? "axis" + k : null);
                cb.CheckedChanged += (s, e) => ShowTrace(SelectedLog());
                chkTraceShow[k] = cb;
                gShow.Controls.Add(cb);
            }
            FlowLayoutPanel gOpt = NewGroup();
            Button btnReset = Btn("Réinitialiser le zoom", 150);
            btnReset.Click += (s, e) => ResetTraceZoom();
            gOpt.Controls.Add(btnReset);
            bar.Controls.Add(gShow); bar.Controls.Add(gOpt);
            bar.Controls.Add(SubLabel("Cliquer-glisser sur le graphique pour zoomer, double-clic pour revenir à la vue complète.", 0));

            Panel body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(10), S(4), S(10), S(10)) };
            chTrace = NewThemedChart();
            chTrace.MouseDoubleClick += (s, e) => ResetTraceZoom();
            body.Controls.Add(chTrace);

            tp.Controls.Add(body); tp.Controls.Add(bar);
            Theme.Current.StyleControls(tp);
            return tp;
        }

        void ResetTraceZoom()
        {
            foreach (ChartArea a in chTrace.ChartAreas) a.AxisX.ScaleView.ZoomReset(0);
        }

        void ShowTrace(FlightLog log)
        {
            if (chTrace == null) return;
            Theme th = Theme.Current;
            ClearChart(chTrace);
            if (log == null || !log.Parsed || log.Time.Length < 10)
            {
                lblTraceTitle.Text = "Aucun vol sélectionné";
                th.StyleChart(chTrace);
                return;
            }
            lblTraceTitle.Text = "Vol #" + log.Index + "  ·  " + FormatDur(log.Duration);
            List<int> shown = Enumerable.Range(0, 4).Where(k => chkTraceShow[k].Checked).ToList();
            if (shown.Count == 0) { th.StyleChart(chTrace); return; }

            const int target = 6000;
            float avail = 93f, y = 0;
            float[] weights = shown.Select(k => k < 3 ? 1f : 0.8f).ToArray();
            float wsum = weights.Sum();
            string[] titles = { "Roll (°/s)", "Pitch (°/s)", "Yaw (°/s)", "Gaz et moteurs (%)" };
            string first = null;
            ChartArea last = null;
            for (int i = 0; i < shown.Count; i++)
            {
                int k = shown[i];
                string an = "t" + k;
                float h = avail * weights[i] / wsum;
                ChartArea area = AddArea(chTrace, an, titles[k], 0, y, 100, h);
                y += h;
                area.CursorX.IsUserSelectionEnabled = true;
                area.CursorX.Interval = 0.001;
                area.AxisX.ScaleView.Zoomable = true;
                area.AxisX.ScrollBar.IsPositionedInside = false;
                area.AxisX.LabelStyle.Format = "0.0";
                area.AxisY.LabelStyle.Format = "0";
                if (first == null) first = an;
                else { area.AlignWithChartArea = first; area.AlignmentOrientation = AreaAlignmentOrientations.Vertical; area.AlignmentStyle = AreaAlignmentStyles.All; }
                last = area;
                double[] x, yy;
                if (k < 3)
                {
                    float[] sp = log.Col("setpoint[" + k + "]"), gy = log.Col("gyroADC[" + k + "]");
                    if (sp != null)
                    {
                        Dsp.Decimate(log.Time, sp, target, out x, out yy);
                        Series s = AddSeries(chTrace, an, an + "sp", x, yy, Theme.Alpha(th.Setpoint, th.IsDark ? 150 : 170), 1, i == 0);
                        s.ChartType = SeriesChartType.Line;
                        s.LegendText = "Consigne (ordre donné)";
                    }
                    if (gy != null)
                    {
                        Dsp.Decimate(log.Time, gy, target, out x, out yy);
                        Series s = AddSeries(chTrace, an, an + "gy", x, yy, th.Axes[k], 1, false);
                        s.ChartType = SeriesChartType.Line;
                    }
                }
                else
                {
                    area.AxisY.Minimum = 0; area.AxisY.Maximum = 100; area.AxisY.Interval = 25;
                    List<float[]> motors = Analyzer.Motors(log);
                    double mMin, mMax;
                    Analyzer.MotorRange(log, out mMin, out mMax);
                    Color[] mc = { th.Axes[0], th.Axes[1], th.Axes[2], th.Accent, th.Warn, th.SubText, th.Good, th.Bad };
                    int mi = 0;
                    foreach (float[] m in motors)
                    {
                        float[] pct = m.Select(v => (float)((v - mMin) / (mMax - mMin) * 100)).ToArray();
                        Dsp.Decimate(log.Time, pct, target / 2, out x, out yy);
                        Series s = AddSeries(chTrace, an, "m" + mi, x, yy, Theme.Alpha(mc[mi % mc.Length], 120), 1, i == 0 && mi == 0);
                        s.ChartType = SeriesChartType.Line;
                        s.LegendText = "Moteurs";
                        mi++;
                    }
                    float[] thr = Analyzer.Throttle(log);
                    if (thr != null)
                    {
                        float[] tpc = thr.Select(v => v * 100f).ToArray();
                        Dsp.Decimate(log.Time, tpc, target / 2, out x, out yy);
                        Series s = AddSeries(chTrace, an, "thr", x, yy, Theme.Alpha(th.Text, th.IsDark ? 35 : 25), 1, true);
                        s.ChartType = SeriesChartType.Area;
                        s.BorderColor = Theme.Alpha(th.Text, 200);
                        s.BorderWidth = 2;
                        s.LegendText = "Gaz";
                    }
                }
            }
            if (last != null) last.AxisX.Title = "temps (s)";
            // légende : une entrée "Gyro" par axe affiché, à la couleur de l'axe
            foreach (int k in shown.Where(k => k < 3))
            {
                Series g = chTrace.Series.FindByName("t" + k + "gy");
                if (g != null) { g.IsVisibleInLegend = true; g.LegendText = "Gyro filtré " + Analyzer.AxisName(k); }
            }
            th.StyleChart(chTrace);
        }
    }
}
