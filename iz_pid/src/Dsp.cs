using System;
using System.Collections.Generic;

namespace BblTuner
{
    public static class Dsp
    {
        public static int NextPow2(int n)
        {
            int p = 1;
            while (p < n) p <<= 1;
            return p;
        }

        public static void Fft(double[] re, double[] im, bool inverse)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    double t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = 2 * Math.PI / len * (inverse ? 1 : -1);
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                int half = len >> 1;
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < half; k++)
                    {
                        int a = i + k, b = a + half;
                        double xr = re[b] * cr - im[b] * ci;
                        double xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi;
                        re[a] += xr; im[a] += xi;
                        double ncr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = ncr;
                    }
                }
            }
            if (inverse)
                for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
        }

        public static double[] Hann(int n)
        {
            double[] w = new double[n];
            for (int i = 0; i < n; i++) w[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1));
            return w;
        }

        public static double[] Tukey(int n, double alpha)
        {
            double[] w = new double[n];
            double edge = alpha * (n - 1) / 2.0;
            for (int i = 0; i < n; i++)
            {
                double k = Math.Min(i, n - 1 - i);
                w[i] = k >= edge ? 1.0 : 0.5 - 0.5 * Math.Cos(Math.PI * k / edge);
            }
            return w;
        }

        /// Densité spectrale (Welch) sur les segments donnés. Retourne psd[segLen/2+1] en unité²/Hz.
        public static double[] Welch(float[] x, List<int> segStarts, int segLen, double fs)
        {
            double[] w = Hann(segLen);
            double wss = 0;
            for (int i = 0; i < segLen; i++) wss += w[i] * w[i];
            int nb = segLen / 2 + 1;
            double[] psd = new double[nb];
            double[] re = new double[segLen], im = new double[segLen];
            int used = 0;
            foreach (int s in segStarts)
            {
                if (s + segLen > x.Length) continue;
                double mean = 0;
                for (int i = 0; i < segLen; i++) mean += x[s + i];
                mean /= segLen;
                for (int i = 0; i < segLen; i++) { re[i] = (x[s + i] - mean) * w[i]; im[i] = 0; }
                Fft(re, im, false);
                for (int k = 0; k < nb; k++) psd[k] += re[k] * re[k] + im[k] * im[k];
                used++;
            }
            if (used == 0) return null;
            double norm = 1.0 / (used * fs * wss);
            for (int k = 0; k < nb; k++) psd[k] *= norm * ((k == 0 || k == nb - 1) ? 1 : 2);
            return psd;
        }

        /// Valeur efficace d'un signal dans une bande, à partir de sa PSD.
        public static double BandRms(double[] psd, double fs, int segLen, double f1, double f2)
        {
            if (psd == null) return double.NaN;
            double df = fs / segLen, sum = 0;
            for (int k = 0; k < psd.Length; k++)
            {
                double f = k * df;
                if (f >= f1 && f < f2) sum += psd[k] * df;
            }
            return Math.Sqrt(sum);
        }

        public static double PeakFreq(double[] psd, double fs, int segLen, double f1, double f2, out double peakVal)
        {
            peakVal = 0;
            if (psd == null) return double.NaN;
            double df = fs / segLen, best = -1, bf = double.NaN;
            for (int k = 0; k < psd.Length; k++)
            {
                double f = k * df;
                if (f >= f1 && f < f2 && psd[k] > best) { best = psd[k]; bf = f; }
            }
            peakVal = best;
            return bf;
        }

        /// Step response par déconvolution de Wiener (méthode PID-Analyzer / PIDToolbox).
        /// input = setpoint, output = gyro. Retourne la réponse indicielle sur respLen échantillons.
        public static double[] StepResponseWindow(float[] input, float[] output, int start, int winLen,
                                                  double[] window, double fs, int respLen, double cutHz)
        {
            int n = NextPow2(winLen * 2);
            double[] sr = new double[n], si = new double[n], gr = new double[n], gi = new double[n];
            for (int i = 0; i < winLen; i++)
            {
                sr[i] = input[start + i] * window[i];
                gr[i] = output[start + i] * window[i];
            }
            Fft(sr, si, false);
            Fft(gr, gi, false);
            double[] hr = new double[n], hi = new double[n];
            for (int k = 0; k < n; k++)
            {
                int kk = k <= n / 2 ? k : n - k;
                double f = kk * fs / n;
                double mask = 1.0 / (1.0 + Math.Pow(f / cutHz, 6)); // SNR élevé sous la coupure
                double sn = 10.0 * mask + 1e-9;
                double den = sr[k] * sr[k] + si[k] * si[k] + 1.0 / sn;
                // G * conj(S) / (|S|^2 + 1/SNR)
                hr[k] = (gr[k] * sr[k] + gi[k] * si[k]) / den;
                hi[k] = (gi[k] * sr[k] - gr[k] * si[k]) / den;
            }
            Fft(hr, hi, true);
            double[] step = new double[respLen];
            double acc = 0;
            for (int i = 0; i < respLen && i < n; i++) { acc += hr[i]; step[i] = acc; }
            return step;
        }

        /// Ré-échantillonne une réponse (pas dt) sur une grille de 1 ms de 0 à maxMs.
        public static double[] ToMsGrid(double[] y, double dt, int maxMs)
        {
            double[] r = new double[maxMs + 1];
            for (int m = 0; m <= maxMs; m++)
            {
                double x = m * 1e-3 / dt;
                int i = (int)x;
                if (i >= y.Length - 1) { r[m] = y[y.Length - 1]; continue; }
                double fr = x - i;
                r[m] = y[i] * (1 - fr) + y[i + 1] * fr;
            }
            return r;
        }

        /// Retard (ms) maximisant l'intercorrélation entre a (consigne) et b (gyro). Positif = b en retard.
        public static double LagMs(float[] a, float[] b, bool[] mask, double fs, double minMs, double maxMs)
        {
            int n = Math.Min(a.Length, b.Length);
            int lmin = (int)Math.Floor(minMs * 1e-3 * fs), lmax = (int)Math.Ceiling(maxMs * 1e-3 * fs);
            // dérivées (met en valeur les mouvements francs)
            double[] da = new double[n], db = new double[n];
            for (int i = 1; i < n; i++)
            {
                da[i] = mask[i] ? a[i] - a[i - 1] : 0;
                db[i] = mask[i] ? b[i] - b[i - 1] : 0;
            }
            // lissage léger de la dérivée gyro (bruit)
            int sm = Math.Max(1, (int)(fs / 500));
            da = MovingAvg(da, sm);
            db = MovingAvg(db, sm);
            double best = double.MinValue; int bestL = 0;
            double[] corr = new double[lmax - lmin + 1];
            for (int L = lmin; L <= lmax; L++)
            {
                double s = 0;
                int i0 = Math.Max(0, -L), i1 = Math.Min(n, n - L);
                for (int i = i0; i < i1; i++) s += da[i] * db[i + L];
                corr[L - lmin] = s;
                if (s > best) { best = s; bestL = L; }
            }
            if (best <= 0) return double.NaN;
            // interpolation parabolique
            double lag = bestL;
            int bi = bestL - lmin;
            if (bi > 0 && bi < corr.Length - 1)
            {
                double y0 = corr[bi - 1], y1 = corr[bi], y2 = corr[bi + 1];
                double den = y0 - 2 * y1 + y2;
                if (Math.Abs(den) > 1e-12) lag += 0.5 * (y0 - y2) / den;
            }
            return lag / fs * 1000.0;
        }

        public static double[] MovingAvg(double[] x, int w)
        {
            if (w <= 1) return x;
            double[] r = new double[x.Length];
            double s = 0;
            for (int i = 0; i < x.Length; i++)
            {
                s += x[i];
                if (i >= w) s -= x[i - w];
                r[i] = s / Math.Min(i + 1, w);
            }
            return r;
        }

        /// Décimation min/max pour l'affichage.
        public static void Decimate(double[] t, float[] y, int target, out double[] xo, out double[] yo)
        {
            int n = Math.Min(t.Length, y.Length);
            if (n <= target)
            {
                xo = new double[n]; yo = new double[n];
                for (int i = 0; i < n; i++) { xo[i] = t[i]; yo[i] = y[i]; }
                return;
            }
            int buckets = target / 2;
            double per = (double)n / buckets;
            List<double> xs = new List<double>(target), ys = new List<double>(target);
            for (int b = 0; b < buckets; b++)
            {
                int s = (int)(b * per), e = Math.Min(n, (int)((b + 1) * per));
                if (e <= s) continue;
                int imin = s, imax = s;
                for (int i = s; i < e; i++)
                {
                    if (y[i] < y[imin]) imin = i;
                    if (y[i] > y[imax]) imax = i;
                }
                int a = Math.Min(imin, imax), c = Math.Max(imin, imax);
                xs.Add(t[a]); ys.Add(y[a]);
                if (c != a) { xs.Add(t[c]); ys.Add(y[c]); }
            }
            xo = xs.ToArray(); yo = ys.ToArray();
        }
    }
}
