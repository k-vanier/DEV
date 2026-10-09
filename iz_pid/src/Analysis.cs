using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BblTuner
{
    public class AxisResult
    {
        public int Axis;
        public List<double[]> Steps = new List<double[]>();   // fenêtres valides, grille 1 ms (0..500)
        public double[] Step;                                  // moyenne
        public int Windows { get { return Steps.Count; } }
        public double Peak = double.NaN, TPeakMs = double.NaN, Lat50Ms = double.NaN, T90Ms = double.NaN;
        public double SteadyState = double.NaN, RingAmp = double.NaN;
        public double LagMs = double.NaN, RmsErr = double.NaN, MaxRate;
        public double[] PsdGyro, PsdUnf, PsdDterm, PsdErr;
        public double GyroHF = double.NaN, UnfHF = double.NaN, DtermHF = double.NaN;
        public double ErrBand = double.NaN, ErrBandPeakHz = double.NaN;
        public double UnfPeakHz = double.NaN;
    }

    public class FlightResult
    {
        public List<FlightLog> Logs = new List<FlightLog>();
        public string Error;
        public double Fs;
        public int SegLen;
        public double ArmedSec, FlyingSec, AvgThrottle;
        public double MotorSatPct = double.NaN, MotorNoisePct = double.NaN;
        public double[] PsdMotor;
        public AxisResult[] Axes = { new AxisResult { Axis = 0 }, new AxisResult { Axis = 1 }, new AxisResult { Axis = 2 } };

        public double[] Freqs()
        {
            if (SegLen == 0) return null;
            double[] f = new double[SegLen / 2 + 1];
            for (int k = 0; k < f.Length; k++) f[k] = k * Fs / SegLen;
            return f;
        }
    }

    public static class Analyzer
    {
        public const int RespMs = 500;
        public const double CutHz = 30.0;
        public static double WinSec = 2.0, TukeyAlpha = -1; // fenêtre de 2 s : biais de valeur finale < 2 %
        static readonly string[] AxisNames = { "Roll", "Pitch", "Yaw" };
        public static string AxisName(int a) { return AxisNames[a]; }

        public static float[] Throttle(FlightLog log)
        {
            float[] sp3 = log.Col("setpoint[3]");
            int n = log.Time.Length;
            float[] t = new float[n];
            if (sp3 != null)
            {
                for (int i = 0; i < n; i++) t[i] = Math.Max(0f, Math.Min(1f, sp3[i] / 1000f));
                return t;
            }
            float[] rc = log.Col("rcCommand[3]");
            if (rc != null)
            {
                for (int i = 0; i < n; i++) t[i] = Math.Max(0f, Math.Min(1f, (rc[i] - 1000f) / 1000f));
                return t;
            }
            return null;
        }

        public static void MotorRange(FlightLog log, out double min, out double max)
        {
            int[] mo = log.HInts("motorOutput");
            if (mo != null && mo.Length >= 2) { min = mo[0]; max = mo[1]; }
            else { min = log.HInt("minthrottle", 1000); max = log.HInt("maxthrottle", 2000); }
            if (max <= min) max = min + 1000;
        }

        public static List<float[]> Motors(FlightLog log)
        {
            List<float[]> m = new List<float[]>();
            for (int i = 0; i < 8; i++)
            {
                float[] c = log.Col("motor[" + i + "]");
                if (c != null) m.Add(c);
            }
            return m;
        }

        public static FlightResult Analyze(FlightLog log)
        {
            FlightResult r = new FlightResult();
            r.Logs.Add(log);
            if (!log.Parsed) { r.Error = "Lecture impossible : " + log.ParseError; return r; }
            int n = log.Time.Length;
            double fs = log.SampleRate;
            r.Fs = fs;
            if (n < 200 || fs < 100) { r.Error = "Log trop court ou fréquence d'enregistrement trop basse."; return r; }

            float[] thr = Throttle(log);
            List<float[]> motors = Motors(log);
            double mMin, mMax;
            MotorRange(log, out mMin, out mMax);
            double mRange = mMax - mMin;

            bool[] armed = new bool[n], flying = new bool[n];
            int nArmed = 0, nFly = 0, nSat = 0;
            double thrSum = 0;
            for (int i = 0; i < n; i++)
            {
                bool a;
                if (motors.Count > 0)
                {
                    double avg = 0, mx = double.MinValue;
                    foreach (float[] m in motors) { avg += m[i]; if (m[i] > mx) mx = m[i]; }
                    avg /= motors.Count;
                    a = avg > mMin + 0.02 * mRange;
                    if (a && mx >= mMax - 0.01 * mRange) nSat++;
                }
                else a = thr != null && thr[i] > 0.05;
                armed[i] = a;
                flying[i] = a && (thr == null || thr[i] > 0.12);
                if (a) { nArmed++; if (thr != null) thrSum += thr[i]; }
                if (flying[i]) nFly++;
            }
            r.ArmedSec = nArmed / fs;
            r.FlyingSec = nFly / fs;
            r.AvgThrottle = nArmed > 0 ? thrSum / nArmed * 100 : double.NaN;
            r.MotorSatPct = nArmed > 0 && motors.Count > 0 ? 100.0 * nSat / nArmed : double.NaN;
            if (nArmed < fs * 2) { r.Error = "Moins de 2 s de vol détecté (moteurs au ralenti)."; }

            // ---------- step response ----------
            int winLen = (int)Math.Round(fs * WinSec);
            int stride = Math.Max(1, winLen / 4);
            int respLen = (int)(RespMs * 1e-3 * fs) + 2;
            double[] window = TukeyAlpha < 0 ? Dsp.Hann(winLen) : Dsp.Tukey(winLen, TukeyAlpha);
            double expectedSpan = (winLen - 1) / fs;

            // ---------- PSD ----------
            int segLen = Dsp.NextPow2((int)(fs / 2));
            if (segLen < 256) segLen = 256;
            r.SegLen = segLen;
            List<int> segs = new List<int>();
            for (int s = 0; s + segLen <= n; s += segLen / 2)
            {
                if (Math.Abs(log.Time[s + segLen - 1] - log.Time[s] - (segLen - 1) / fs) > 0.1 * segLen / fs) continue;
                int ok = 0;
                for (int i = s; i < s + segLen; i++) if (flying[i]) ok++;
                if (ok == segLen) segs.Add(s);
            }

            for (int a = 0; a < 3; a++)
            {
                AxisResult ar = r.Axes[a];
                float[] sp = log.Col("setpoint[" + a + "]");
                float[] gy = log.Col("gyroADC[" + a + "]");
                if (sp == null || gy == null) continue;

                for (int s = 0; s + winLen <= n; s += stride)
                {
                    if (Math.Abs(log.Time[s + winLen - 1] - log.Time[s] - expectedSpan) > 0.05 * expectedSpan) continue;
                    int ok = 0; double mx = 0;
                    for (int i = s; i < s + winLen; i++)
                    {
                        if (armed[i]) ok++;
                        double v = Math.Abs(sp[i]);
                        if (v > mx) mx = v;
                    }
                    if (ok < winLen * 0.98 || mx < 20) continue;
                    double[] st = Dsp.StepResponseWindow(sp, gy, s, winLen, window, fs, respLen, CutHz);
                    double[] g = Dsp.ToMsGrid(st, 1.0 / fs, RespMs);
                    double ss = Mean(g, 200, RespMs);
                    if (ss > 0.5 && ss < 1.6 && !double.IsNaN(ss)) ar.Steps.Add(g);
                }

                // erreur de suivi, retard
                double se = 0; int ne = 0; double maxRate = 0;
                float[] err = new float[n];
                for (int i = 0; i < n; i++)
                {
                    err[i] = sp[i] - gy[i];
                    if (armed[i]) { se += err[i] * (double)err[i]; ne++; if (Math.Abs(sp[i]) > maxRate) maxRate = Math.Abs(sp[i]); }
                }
                ar.RmsErr = ne > 0 ? Math.Sqrt(se / ne) : double.NaN;
                ar.MaxRate = maxRate;
                ar.LagMs = Dsp.LagMs(sp, gy, armed, fs, -15, 60);

                if (segs.Count > 0)
                {
                    ar.PsdGyro = Dsp.Welch(gy, segs, segLen, fs);
                    float[] unf = log.Col("gyroUnfilt[" + a + "]");
                    if (unf != null) ar.PsdUnf = Dsp.Welch(unf, segs, segLen, fs);
                    float[] dt = log.Col("axisD[" + a + "]");
                    if (dt != null) ar.PsdDterm = Dsp.Welch(dt, segs, segLen, fs);
                    ar.PsdErr = Dsp.Welch(err, segs, segLen, fs);
                }
            }

            if (segs.Count > 0 && motors.Count > 0)
            {
                double[] acc = null;
                foreach (float[] m in motors)
                {
                    float[] pct = new float[n];
                    for (int i = 0; i < n; i++) pct[i] = (float)((m[i] - mMin) / mRange * 100.0);
                    double[] p = Dsp.Welch(pct, segs, segLen, fs);
                    if (p == null) continue;
                    if (acc == null) acc = new double[p.Length];
                    for (int k = 0; k < p.Length; k++) acc[k] += p[k] / motors.Count;
                }
                r.PsdMotor = acc;
            }

            Finalize(r);
            return r;
        }

        /// Combine plusieurs vols (mêmes réglages) pour une analyse plus robuste.
        public static FlightResult Combine(List<FlightResult> list)
        {
            List<FlightResult> ok = list.Where(x => x.Error == null || x.Axes.Any(a => a.Windows > 0)).ToList();
            if (ok.Count == 0) return list.Count > 0 ? list[0] : null;
            if (ok.Count == 1) return ok[0];
            FlightResult c = new FlightResult();
            FlightResult first = ok[0];
            c.Fs = first.Fs; c.SegLen = first.SegLen;
            double wsum = 0;
            double sat = 0, satW = 0, thr = 0, thrW = 0;
            foreach (FlightResult r in ok)
            {
                c.Logs.AddRange(r.Logs);
                c.ArmedSec += r.ArmedSec; c.FlyingSec += r.FlyingSec;
                if (!double.IsNaN(r.MotorSatPct)) { sat += r.MotorSatPct * r.ArmedSec; satW += r.ArmedSec; }
                if (!double.IsNaN(r.AvgThrottle)) { thr += r.AvgThrottle * r.ArmedSec; thrW += r.ArmedSec; }
                wsum += r.FlyingSec;
            }
            c.MotorSatPct = satW > 0 ? sat / satW : double.NaN;
            c.AvgThrottle = thrW > 0 ? thr / thrW : double.NaN;
            List<FlightResult> same = ok.Where(x => x.SegLen == first.SegLen && Math.Abs(x.Fs - first.Fs) < 1).ToList();
            c.PsdMotor = AvgPsd(same.Select(x => x.PsdMotor), same.Select(x => x.FlyingSec));

            for (int a = 0; a < 3; a++)
            {
                AxisResult ca = c.Axes[a];
                double lag = 0, lagW = 0, rms = 0, rmsW = 0;
                foreach (FlightResult r in ok)
                {
                    AxisResult ra = r.Axes[a];
                    ca.Steps.AddRange(ra.Steps);
                    ca.MaxRate = Math.Max(ca.MaxRate, ra.MaxRate);
                    if (!double.IsNaN(ra.LagMs)) { lag += ra.LagMs * r.ArmedSec; lagW += r.ArmedSec; }
                    if (!double.IsNaN(ra.RmsErr)) { rms += ra.RmsErr * r.ArmedSec; rmsW += r.ArmedSec; }
                }
                ca.LagMs = lagW > 0 ? lag / lagW : double.NaN;
                ca.RmsErr = rmsW > 0 ? rms / rmsW : double.NaN;
                IEnumerable<double> w = same.Select(x => x.FlyingSec);
                ca.PsdGyro = AvgPsd(same.Select(x => x.Axes[a].PsdGyro), w);
                ca.PsdUnf = AvgPsd(same.Select(x => x.Axes[a].PsdUnf), w);
                ca.PsdDterm = AvgPsd(same.Select(x => x.Axes[a].PsdDterm), w);
                ca.PsdErr = AvgPsd(same.Select(x => x.Axes[a].PsdErr), w);
            }
            Finalize(c);
            return c;
        }

        static double[] AvgPsd(IEnumerable<double[]> psds, IEnumerable<double> weights)
        {
            double[] acc = null; double ws = 0;
            using (IEnumerator<double[]> ep = psds.GetEnumerator())
            using (IEnumerator<double> ew = weights.GetEnumerator())
            {
                while (ep.MoveNext() && ew.MoveNext())
                {
                    if (ep.Current == null || ew.Current <= 0) continue;
                    if (acc == null) acc = new double[ep.Current.Length];
                    if (ep.Current.Length != acc.Length) continue;
                    for (int k = 0; k < acc.Length; k++) acc[k] += ep.Current[k] * ew.Current;
                    ws += ew.Current;
                }
            }
            if (acc == null || ws <= 0) return null;
            for (int k = 0; k < acc.Length; k++) acc[k] /= ws;
            return acc;
        }

        static double Mean(double[] g, int from, int to)
        {
            double s = 0; int c = 0;
            for (int i = from; i <= to && i < g.Length; i++) { s += g[i]; c++; }
            return c > 0 ? s / c : double.NaN;
        }

        public static void Finalize(FlightResult r)
        {
            double fs = r.Fs; int seg = r.SegLen;
            foreach (AxisResult a in r.Axes)
            {
                a.Step = null;
                if (a.Steps.Count > 0)
                {
                    // moyenne robuste : on écarte les fenêtres dont la valeur finale s'éloigne de la médiane
                    double[] sss = a.Steps.Select(s => Mean(s, 200, RespMs)).OrderBy(x => x).ToArray();
                    double med = sss[sss.Length / 2];
                    double[] avg = new double[RespMs + 1];
                    int cnt = 0;
                    foreach (double[] s in a.Steps)
                    {
                        if (Math.Abs(Mean(s, 200, RespMs) - med) > 0.25) continue;
                        for (int i = 0; i <= RespMs; i++) avg[i] += s[i];
                        cnt++;
                    }
                    if (cnt > 0)
                    {
                        for (int i = 0; i <= RespMs; i++) avg[i] /= cnt;
                        a.Step = avg;
                        ComputeStepMetrics(a);
                    }
                }
                if (seg > 0)
                {
                    double nyq = fs / 2;
                    a.GyroHF = Dsp.BandRms(a.PsdGyro, fs, seg, 100, nyq);
                    a.UnfHF = Dsp.BandRms(a.PsdUnf, fs, seg, 100, nyq);
                    a.DtermHF = Dsp.BandRms(a.PsdDterm, fs, seg, 100, nyq);
                    a.ErrBand = Dsp.BandRms(a.PsdErr, fs, seg, 20, 100);
                    double pv;
                    a.ErrBandPeakHz = Dsp.PeakFreq(a.PsdErr, fs, seg, 20, 100, out pv);
                    a.UnfPeakHz = Dsp.PeakFreq(a.PsdUnf, fs, seg, 80, nyq, out pv);
                }
            }
            if (seg > 0) r.MotorNoisePct = Dsp.BandRms(r.PsdMotor, fs, seg, 100, fs / 2);
        }

        static void ComputeStepMetrics(AxisResult a)
        {
            double[] s = a.Step;
            a.SteadyState = Mean(s, 200, RespMs);
            int ip = 0;
            for (int i = 0; i <= 250; i++) if (s[i] > s[ip]) ip = i;
            a.Peak = s[ip];
            a.TPeakMs = ip;
            a.Lat50Ms = CrossTime(s, 0.5);
            a.T90Ms = CrossTime(s, 0.9);
            double mn = double.MaxValue;
            for (int i = ip; i <= Math.Min(RespMs, ip + 120); i++) if (s[i] < mn) mn = s[i];
            a.RingAmp = a.SteadyState - mn;
        }

        static double CrossTime(double[] s, double level)
        {
            for (int i = 1; i < s.Length; i++)
                if (s[i] >= level && s[i - 1] < level)
                    return i - 1 + (level - s[i - 1]) / (s[i] - s[i - 1]);
            return double.NaN;
        }
    }

    // ===================================================================================
    public class PidSet
    {
        public int[] P = new int[3], I = new int[3], D = new int[3], Dmin = new int[3], F = new int[3];
        public bool HasDmin, HasF, Valid;
        public string DminKey = "d_min";

        public static PidSet FromHeaders(FlightLog log)
        {
            PidSet p = new PidSet();
            string[] keys = { "rollPID", "pitchPID", "yawPID" };
            for (int a = 0; a < 3; a++)
            {
                int[] v = log.HInts(keys[a]);
                if (v == null || v.Length < 3) continue;
                p.Valid = true;
                p.P[a] = v[0]; p.I[a] = v[1]; p.D[a] = v[2];
                if (v.Length >= 4) { p.F[a] = v[3]; p.HasF = true; }
            }
            int[] ff = log.HInts("ff_weight") ?? log.HInts("feedforward_weight");
            if (ff != null && ff.Length >= 3) { for (int a = 0; a < 3; a++) p.F[a] = ff[a]; p.HasF = true; }
            int[] dm = log.HInts("d_min");
            if (dm == null) { dm = log.HInts("d_max"); if (dm != null) p.DminKey = "d_max"; }
            if (dm != null && dm.Length >= 3) { for (int a = 0; a < 3; a++) p.Dmin[a] = dm[a]; p.HasDmin = true; }
            return p;
        }

        public PidSet Clone()
        {
            PidSet c = (PidSet)MemberwiseClone();
            c.P = (int[])P.Clone(); c.I = (int[])I.Clone(); c.D = (int[])D.Clone();
            c.Dmin = (int[])Dmin.Clone(); c.F = (int[])F.Clone();
            return c;
        }

        public bool SameAs(PidSet o)
        {
            return P.SequenceEqual(o.P) && I.SequenceEqual(o.I) && D.SequenceEqual(o.D)
                && Dmin.SequenceEqual(o.Dmin) && F.SequenceEqual(o.F);
        }

        public string AxisString(int a)
        {
            string s = P[a] + "/" + I[a] + "/" + D[a];
            if (HasDmin) s += " (" + Dmin[a] + ")";
            if (HasF) s += " F" + F[a];
            return s;
        }
    }

    public enum LineKind { Title, Ok, Info, Warn, Bad, Action }

    public class RecLine
    {
        public LineKind Kind; public string Text;
        public RecLine(LineKind k, string t) { Kind = k; Text = t; }
    }

    public class Recommendation
    {
        public PidSet Current, Proposed;
        public List<RecLine> Lines = new List<RecLine>();
        public string Cli = "";
        public int NoiseLevel; // 0 faible, 1 moyen, 2 élevé
    }

    public static class Recommender
    {
        static string F1(double v) { return double.IsNaN(v) ? "—" : v.ToString("0.0", CultureInfo.InvariantCulture); }
        static string F2(double v) { return double.IsNaN(v) ? "—" : v.ToString("0.00", CultureInfo.InvariantCulture); }
        static string Pct(double f) { int p = (int)Math.Round((f - 1) * 100); return (p >= 0 ? "+" : "") + p + " %"; }

        public static Recommendation Compute(FlightResult r, PidSet cur)
        {
            Recommendation rec = new Recommendation();
            rec.Current = cur;
            PidSet pro = cur.Clone();
            rec.Proposed = pro;
            List<RecLine> L = rec.Lines;

            // ---------------- diagnostic global ----------------
            L.Add(new RecLine(LineKind.Title, "Diagnostic général"));
            L.Add(new RecLine(LineKind.Info, string.Format(CultureInfo.InvariantCulture,
                "{0} vol(s) analysé(s) – {1:0} s armé, {2:0} s en vol (gaz > 12 %), gaz moyen {3:0} %, log à {4:0} Hz.",
                r.Logs.Count, r.ArmedSec, r.FlyingSec, r.AvgThrottle, r.Fs)));
            if (r.Fs < 1000)
                L.Add(new RecLine(LineKind.Warn, "Fréquence de log < 1 kHz : l'analyse du bruit est limitée. Conseillé : blackbox_sample_rate à 1/2 (2 kHz) au minimum."));
            if (r.FlyingSec < 15)
                L.Add(new RecLine(LineKind.Warn, "Peu de données de vol (< 15 s). Les propositions sont moins fiables : fais un vol de 1 à 2 min avec des flips, rolls et des coups de gaz."));

            int motorClass = Classify(r.MotorNoisePct, 1.5, 3.0);
            double gyroHF = r.Axes.Take(2).Select(a => a.GyroHF).Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Max();
            int gyroClass = Classify(gyroHF, 1.0, 2.5);
            int noise = Math.Max(motorClass, gyroClass);
            rec.NoiseLevel = noise;
            string[] lvl = { "faible", "moyen", "élevé" };
            if (!double.IsNaN(r.MotorNoisePct))
                L.Add(new RecLine(motorClass == 0 ? LineKind.Ok : motorClass == 1 ? LineKind.Warn : LineKind.Bad,
                    "Bruit moteurs (> 100 Hz) : " + F2(r.MotorNoisePct) + " % RMS → " + lvl[motorClass] +
                    (motorClass == 2 ? ". Risque de moteurs chauds : on ne monte pas la D, et il faut plus de filtrage ou vérifier les hélices et le châssis." : ".")));
            if (!double.IsNaN(gyroHF))
                L.Add(new RecLine(gyroClass == 0 ? LineKind.Ok : gyroClass == 1 ? LineKind.Warn : LineKind.Bad,
                    "Bruit gyro filtré (> 100 Hz) : " + F2(gyroHF) + " °/s RMS → " + lvl[gyroClass] + "."));
            if (!double.IsNaN(r.MotorSatPct))
            {
                if (r.MotorSatPct > 3)
                    L.Add(new RecLine(LineKind.Warn, "Moteurs en saturation " + F1(r.MotorSatPct) + " % du temps : le quad manque d'autorité, la step response est faussée pendant ces passages. Évite d'augmenter les gains."));
                else
                    L.Add(new RecLine(LineKind.Ok, "Saturation moteurs : " + F1(r.MotorSatPct) + " % (OK)."));
            }
            for (int a = 0; a < 3; a++)
            {
                AxisResult ax = r.Axes[a];
                if (!double.IsNaN(ax.UnfPeakHz) && ax.PsdUnf != null && a < 2)
                {
                    if (ax.UnfPeakHz >= 100 && ax.UnfPeakHz <= 250)
                        L.Add(new RecLine(LineKind.Info, Analyzer.AxisName(a) + " : pic de bruit gyro brut vers " + F1(ax.UnfPeakHz) +
                            " Hz, typique d'une résonance de châssis ou d'une pièce mal serrée. Le filtre notch dynamique doit le couvrir."));
                }
            }

            if (!cur.Valid)
            {
                L.Add(new RecLine(LineKind.Bad, "PID actuels introuvables dans l'en-tête du log (rollPID/pitchPID/yawPID). Impossible de proposer des valeurs."));
                return rec;
            }

            // ---------------- par axe ----------------
            for (int a = 0; a < 3; a++)
            {
                AxisResult ax = r.Axes[a];
                double fP = 1, fI = 1, fD = 1, fF = 1;
                L.Add(new RecLine(LineKind.Title, Analyzer.AxisName(a) + " – actuel " + cur.AxisString(a)));

                if (ax.Step == null || ax.Windows < 3)
                {
                    L.Add(new RecLine(LineKind.Warn, "Pas assez de mouvements francs sur cet axe (" + ax.Windows +
                        " fenêtres exploitables). Fais des flips, rolls et des allers-retours rapides du manche. Aucun changement proposé."));
                    continue;
                }
                double peak = ax.Peak, ss = ax.SteadyState;
                bool ringing = ax.RingAmp > 0.07 && peak > 1.03;
                L.Add(new RecLine(LineKind.Info, string.Format(CultureInfo.InvariantCulture,
                    "Step response ({0} fenêtres) : pic {1:0.00} à {2:0} ms, latence 50 % {3:0.0} ms, valeur finale {4:0.00}, rebond {5:0.00}. Retard gyro/consigne {6} ms, erreur RMS {7:0.0} °/s.",
                    ax.Windows, peak, ax.TPeakMs, ax.Lat50Ms, ss, ax.RingAmp, F1(ax.LagMs), ax.RmsErr)));

                bool yaw = a == 2;
                bool canRaiseD = noise < 2 && cur.D[a] > 0;
                if (!yaw)
                {
                    if (peak > 1.20)
                    {
                        if (canRaiseD && noise == 0) { fD *= 1.20; L.Add(new RecLine(LineKind.Bad, "Gros dépassement (" + F2(peak) + ") : pas assez d'amortissement → D +20 %.")); }
                        else if (canRaiseD) { fD *= 1.10; fP *= 0.93; L.Add(new RecLine(LineKind.Bad, "Gros dépassement (" + F2(peak) + "), bruit moyen → D +10 % et P −7 %.")); }
                        else { fP *= 0.85; L.Add(new RecLine(LineKind.Bad, "Gros dépassement (" + F2(peak) + "), bruit élevé → on baisse P de 15 % plutôt que de monter la D.")); }
                    }
                    else if (peak > 1.10)
                    {
                        if (canRaiseD) { fD *= 1.10; L.Add(new RecLine(LineKind.Warn, "Dépassement modéré (" + F2(peak) + ") → D +10 %.")); }
                        else { fP *= 0.93; L.Add(new RecLine(LineKind.Warn, "Dépassement modéré (" + F2(peak) + "), bruit élevé → P −7 %.")); }
                    }
                    else if (peak >= 0.98)
                    {
                        if (ringing)
                        {
                            if (canRaiseD) { fD *= 1.08; L.Add(new RecLine(LineKind.Warn, "Bonne amplitude mais oscillation après le pic (rebond " + F2(ax.RingAmp) + ") → D +8 %.")); }
                            else { fP *= 0.94; L.Add(new RecLine(LineKind.Warn, "Oscillation après le pic, bruit élevé → P −6 %.")); }
                        }
                        else L.Add(new RecLine(LineKind.Ok, "Forme de réponse propre (pic " + F2(peak) + ", cible 1.00 à 1.10)."));
                    }
                    else if (peak >= 0.92)
                    {
                        fD *= 0.92;
                        L.Add(new RecLine(LineKind.Warn, "Réponse un peu sur-amortie (pic " + F2(peak) + ") : trop de D par rapport à P → D −8 %."));
                    }
                    else
                    {
                        if (cur.P[a] > 0 && cur.D[a] > 0.75 * cur.P[a]) { fD *= 0.85; L.Add(new RecLine(LineKind.Warn, "Réponse molle (pic " + F2(peak) + "), D élevée par rapport à P → D −15 %.")); }
                        else { fP *= 1.10; L.Add(new RecLine(LineKind.Warn, "Réponse molle (pic " + F2(peak) + ") → P +10 %.")); }
                    }

                    // marge pour un tune plus vif si tout est propre
                    bool clean = peak >= 0.98 && peak <= 1.10 && !ringing && noise == 0 && (double.IsNaN(r.MotorSatPct) || r.MotorSatPct < 2);
                    if (clean && ax.Lat50Ms > 22)
                    {
                        fP *= 1.06; fD *= 1.06;
                        L.Add(new RecLine(LineKind.Info, "Réponse propre, bruit faible et latence " + F1(ax.Lat50Ms) + " ms : il y a de la marge, P et D +6 % (équivaut au Master Multiplier)."));
                    }
                }
                else
                {
                    if (peak > 1.15) { fP *= 0.92; L.Add(new RecLine(LineKind.Warn, "Yaw : dépassement (" + F2(peak) + ") → P yaw −8 %.")); }
                    else if (peak < 0.90) { fP *= 1.08; L.Add(new RecLine(LineKind.Warn, "Yaw : réponse molle (" + F2(peak) + ") → P yaw +8 %.")); }
                    else L.Add(new RecLine(LineKind.Ok, "Yaw : forme de réponse correcte (pic " + F2(peak) + ")."));
                }

                // terme I : valeur finale de la step response
                if (ss < 0.93) { fI *= 1.12; L.Add(new RecLine(LineKind.Warn, "Valeur finale basse (" + F2(ss) + ") : le quad ne tient pas l'attitude → I +12 %.")); }
                else if (ss < 0.96) { fI *= 1.06; L.Add(new RecLine(LineKind.Info, "Valeur finale un peu basse (" + F2(ss) + ") → I +6 %.")); }
                else if (ss > 1.08) { fI *= 0.90; L.Add(new RecLine(LineKind.Warn, "Valeur finale haute (" + F2(ss) + ") : rebond lent, I trop fort → I −10 %.")); }

                // bruit : plafond sur D
                if (noise == 2 && cur.D[a] > 0 && fD > 0.9)
                {
                    fD = Math.Min(fD, 0.92);
                    L.Add(new RecLine(LineKind.Bad, "Bruit élevé : D limitée à −8 % pour protéger les moteurs."));
                }
                else if (noise == 1 && fD > 1.0)
                {
                    fD = Math.Min(fD, 1.05);
                }

                // feedforward : retard gyro / consigne
                if (cur.F[a] > 0 && !double.IsNaN(ax.LagMs))
                {
                    double hi = yaw ? 20 : 14, lo = yaw ? 4 : 3;
                    if (ax.LagMs > hi + 6) { fF *= 1.15; L.Add(new RecLine(LineKind.Warn, "Gyro en retard de " + F1(ax.LagMs) + " ms sur la consigne → Feedforward +15 %.")); }
                    else if (ax.LagMs > hi) { fF *= 1.08; L.Add(new RecLine(LineKind.Info, "Gyro en retard de " + F1(ax.LagMs) + " ms → Feedforward +8 %.")); }
                    else if (ax.LagMs < lo) { fF *= 0.90; L.Add(new RecLine(LineKind.Warn, "Gyro quasi en avance sur la consigne (" + F1(ax.LagMs) + " ms) : trop de Feedforward → FF −10 %.")); }
                }

                // oscillations basses fréquences / propwash
                if (!double.IsNaN(ax.ErrBand) && ax.ErrBand > 12 && !yaw)
                    L.Add(new RecLine(LineKind.Info, "Activité notable entre 20 et 100 Hz dans l'erreur de suivi (" + F1(ax.ErrBand) + " °/s RMS, pic " + F1(ax.ErrBandPeakHz) +
                        " Hz) : propwash ou oscillation. Si tu le ressens en vol, un peu plus de D-min ou de D aide, si le bruit le permet."));

                fP = Clamp(fP); fI = Clamp(fI); fD = Clamp(fD); fF = Clamp(fF);
                pro.P[a] = Scale(cur.P[a], fP);
                pro.I[a] = Scale(cur.I[a], fI);
                pro.D[a] = Scale(cur.D[a], fD);
                if (cur.HasDmin && cur.Dmin[a] > 0)
                {
                    pro.Dmin[a] = Scale(cur.Dmin[a], fD);
                    if (cur.DminKey == "d_min" && pro.Dmin[a] > pro.D[a]) pro.Dmin[a] = pro.D[a];
                }
                pro.F[a] = Scale(cur.F[a], fF);

                if (fP == 1 && fI == 1 && fD == 1 && fF == 1)
                    L.Add(new RecLine(LineKind.Ok, "→ Aucun changement nécessaire sur cet axe."));
                else
                {
                    List<string> ch = new List<string>();
                    if (fP != 1) ch.Add("P " + cur.P[a] + "→" + pro.P[a] + " (" + Pct(fP) + ")");
                    if (fI != 1) ch.Add("I " + cur.I[a] + "→" + pro.I[a] + " (" + Pct(fI) + ")");
                    if (fD != 1 && cur.D[a] > 0) ch.Add("D " + cur.D[a] + "→" + pro.D[a] + " (" + Pct(fD) + ")");
                    if (fF != 1) ch.Add("FF " + cur.F[a] + "→" + pro.F[a] + " (" + Pct(fF) + ")");
                    L.Add(new RecLine(LineKind.Action, "→ " + string.Join(", ", ch)));
                }
            }

            L.Add(new RecLine(LineKind.Title, "Méthode"));
            L.Add(new RecLine(LineKind.Info, "Les changements sont volontairement limités (±25 % max) : applique-les, refais un vol avec les mêmes manœuvres, puis ré-analyse. Répète jusqu'à ce que tous les axes soient au vert."));
            L.Add(new RecLine(LineKind.Info, "Après chaque changement, touche les moteurs : si tu ne peux pas garder le doigt dessus quelques secondes, reviens en arrière sur la D ou augmente le filtrage."));

            rec.Cli = BuildCli(r, cur, pro);
            return rec;
        }

        static int Classify(double v, double t1, double t2)
        {
            if (double.IsNaN(v)) return 0;
            return v < t1 ? 0 : v < t2 ? 1 : 2;
        }

        static double Clamp(double f) { return Math.Max(0.75, Math.Min(1.25, f)); }
        static int Scale(int v, double f) { return (int)Math.Round(v * f); }

        static string BuildCli(FlightResult r, PidSet cur, PidSet pro)
        {
            StringBuilder sb = new StringBuilder();
            FlightLog log = r.Logs[r.Logs.Count - 1];
            sb.AppendLine("# BBL Tuner – propositions PID");
            sb.AppendLine("# Firmware : " + log.Firmware + (log.CraftName.Length > 0 ? "  –  " + log.CraftName : ""));
            sb.AppendLine("# Vol(s) : " + string.Join(", ", r.Logs.Select(l => "#" + l.Index)));
            sb.AppendLine("# À coller dans l'onglet CLI de Betaflight Configurator (profil PID actif).");
            sb.AppendLine("# Les curseurs de réglage simplifié sont désactivés pour ne pas écraser ces valeurs.");
            sb.AppendLine("set simplified_pids_mode = OFF");
            string[] ax = { "roll", "pitch", "yaw" };
            for (int a = 0; a < 3; a++)
            {
                sb.AppendLine("set p_" + ax[a] + " = " + pro.P[a]);
                sb.AppendLine("set i_" + ax[a] + " = " + pro.I[a]);
                sb.AppendLine("set d_" + ax[a] + " = " + pro.D[a]);
                if (cur.HasDmin) sb.AppendLine("set " + cur.DminKey + "_" + ax[a] + " = " + pro.Dmin[a]);
                if (cur.HasF) sb.AppendLine("set f_" + ax[a] + " = " + pro.F[a]);
            }
            sb.AppendLine("save");
            return sb.ToString();
        }
    }
}
