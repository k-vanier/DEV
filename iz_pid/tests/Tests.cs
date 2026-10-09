// Banc de test console : génère un .bbl synthétique (encodage Betaflight), le relit et l'analyse.
//   BblTests.exe gen <out.bbl>      -> écrit un fichier de démo à 3 vols
//   BblTests.exe check              -> aller-retour encodage/décodage + analyse
//   BblTests.exe analyze <file.bbl> -> rapport texte d'un vrai fichier
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BblTuner
{
    class Enc
    {
        public List<byte> B = new List<byte>();
        public void Byte(int v) { B.Add((byte)v); }
        public void UVB(uint v) { while (v >= 0x80) { B.Add((byte)(v | 0x80)); v >>= 7; } B.Add((byte)v); }
        public void SVB(int v) { UVB((uint)((v << 1) ^ (v >> 31))); }
        static bool Fits(int v, int bits) { int lim = 1 << (bits - 1); return v >= -lim && v < lim; }

        public void Tag2_3S32(int[] v)
        {
            if (v.All(x => Fits(x, 2))) { Byte(((v[0] & 3) << 4) | ((v[1] & 3) << 2) | (v[2] & 3)); return; }
            if (v.All(x => Fits(x, 4))) { Byte(0x40 | (v[0] & 0xF)); Byte(((v[1] & 0xF) << 4) | (v[2] & 0xF)); return; }
            if (v.All(x => Fits(x, 6))) { Byte(0x80 | (v[0] & 0x3F)); Byte(v[1] & 0x3F); Byte(v[2] & 0x3F); return; }
            int sel = 0; int[] sz = new int[3];
            for (int i = 0; i < 3; i++)
            {
                sz[i] = Fits(v[i], 8) ? 0 : Fits(v[i], 16) ? 1 : Fits(v[i], 24) ? 2 : 3;
                sel |= sz[i] << (2 * i);
            }
            Byte(0xC0 | sel);
            for (int i = 0; i < 3; i++)
                for (int b = 0; b <= sz[i]; b++) Byte((v[i] >> (8 * b)) & 0xFF);
        }

        /// Copie de blackboxWriteTag8_4S16 du firmware Betaflight (data version 2).
        public void Tag8_4S16(int[] v)
        {
            int sel = 0;
            for (int i = 3; i >= 0; i--)
            {
                sel <<= 2;
                sel |= v[i] == 0 ? 0 : (v[i] >= -8 && v[i] < 8) ? 1 : (v[i] >= -128 && v[i] < 128) ? 2 : 3;
            }
            Byte(sel);
            int nibble = 0, buffer = 0;
            for (int i = 0; i < 4; i++, sel >>= 2)
            {
                switch (sel & 3)
                {
                    case 1:
                        if (nibble == 0) { buffer = v[i] << 4; nibble = 1; }
                        else { Byte((buffer | (v[i] & 0x0F)) & 0xFF); nibble = 0; }
                        break;
                    case 2:
                        if (nibble == 0) Byte(v[i] & 0xFF);
                        else { Byte((buffer | ((v[i] >> 4) & 0x0F)) & 0xFF); buffer = v[i] << 4; }
                        break;
                    case 3:
                        if (nibble == 0) { Byte((v[i] >> 8) & 0xFF); Byte(v[i] & 0xFF); }
                        else { Byte((buffer | ((v[i] >> 12) & 0x0F)) & 0xFF); Byte((v[i] >> 4) & 0xFF); buffer = v[i] << 4; }
                        break;
                }
            }
            if (nibble == 1) Byte(buffer & 0xFF);
        }

        public void Tag8_8SVB(int[] v, int count)
        {
            if (count == 1) { SVB(v[0]); return; }
            int h = 0;
            for (int i = 0; i < count; i++) if (v[i] != 0) h |= 1 << i;
            Byte(h);
            for (int i = 0; i < count; i++) if (v[i] != 0) SVB(v[i]);
        }
    }

    class SimFlight
    {
        public int N;
        public Dictionary<string, int[]> F = new Dictionary<string, int[]>();
    }

    static class TestProgram
    {
        static readonly string[] Names = {
            "loopIteration","time","axisP[0]","axisP[1]","axisP[2]","axisI[0]","axisI[1]","axisI[2]",
            "axisD[0]","axisD[1]","axisF[0]","axisF[1]","axisF[2]",
            "rcCommand[0]","rcCommand[1]","rcCommand[2]","rcCommand[3]",
            "setpoint[0]","setpoint[1]","setpoint[2]","setpoint[3]",
            "gyroADC[0]","gyroADC[1]","gyroADC[2]","gyroUnfilt[0]","gyroUnfilt[1]","gyroUnfilt[2]",
            "motor[0]","motor[1]","motor[2]","motor[3]" };
        // prédicteurs / encodages I et P (proches de Betaflight 4.4)
        static int[] IPred = { 0,0, 0,0,0, 0,0,0, 0,0, 0,0,0, 0,0,0,4, 0,0,0,0, 0,0,0, 0,0,0, 11,5,5,5 };
        static int[] IEnc  = { 1,1, 0,0,0, 0,0,0, 0,0, 0,0,0, 0,0,0,1, 0,0,0,0, 0,0,0, 0,0,0, 1,0,0,0 };
        static int[] PPred = { 6,2, 1,1,1, 1,1,1, 1,1, 1,1,1, 1,1,1,1, 1,1,1,1, 3,3,3, 3,3,3, 3,3,3,3 };
        static int[] PEnc  = { 9,0, 0,0,0, 7,7,7, 6,6, 6,6,6, 8,8,8,8, 8,8,8,8, 0,0,0, 0,0,0, 0,0,0,0 };
        static int[] Signed = { 0,0, 1,1,1, 1,1,1, 1,1, 1,1,1, 1,1,1,0, 1,1,1,0, 1,1,1, 1,1,1, 0,0,0,0 };

        static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length >= 2 && args[0] == "gen")
            {
                List<SimFlight> sims;
                byte[] data = BuildFile(out sims);
                File.WriteAllBytes(args[1], data);
                Console.WriteLine("écrit " + args[1] + " (" + data.Length + " octets)");
                return 0;
            }
            if (args.Length >= 2 && args[0] == "analyze") return AnalyzeFile(args[1]);
            if (args.Length >= 2 && args[0] == "config") return ConfigTest(args[1]);
            if (args.Length >= 3 && args[0] == "seg")
            {
                byte[] d = File.ReadAllBytes(args[1]);
                FlightLog lg = BblReader.Split(d)[int.Parse(args[2]) - 1];
                BblReader.Parse(d, lg);
                float[] sp = lg.Col("setpoint[0]"), gy = lg.Col("gyroADC[0]");
                double dur = lg.Duration;
                foreach (double t0 in new[] { 0.0, dur * 0.25, dur / 2 - 0.5, dur * 0.75 })
                {
                    int i0 = Array.FindIndex(lg.Time, x => x >= t0), i1 = Array.FindIndex(lg.Time, x => x >= t0 + 1);
                    if (i1 < 0) i1 = lg.Time.Length;
                    double msp = 0, mgy = 0, hf = 0;
                    for (int i = i0; i < i1; i++) { msp = Math.Max(msp, Math.Abs(sp[i])); mgy = Math.Max(mgy, Math.Abs(gy[i])); if (i > i0) hf += Math.Abs(gy[i] - gy[i - 1]); }
                    Console.WriteLine(string.Format("t0={0,6:0.0}s  consigne max={1,6:0} gyro max={2,6:0} variation gyro moy. d'un point à l'autre={3:0.0} °/s", t0, msp, mgy, hf / Math.Max(1, i1 - i0 - 1)));
                }
                return 0;
            }
            if (args.Length >= 3 && args[0] == "win")
            {
                Analyzer.WinSec = double.Parse(args[1]); Analyzer.TukeyAlpha = double.Parse(args[2]);
                List<SimFlight> sm; byte[] dd = BuildFile(out sm);
                foreach (FlightLog lg in BblReader.Split(dd)) { BblReader.Parse(dd, lg); FlightResult fr = Analyzer.Analyze(lg); if (fr.Error != null) continue;
                    Console.WriteLine(string.Format("win={0} tukey={1} vol{2}: " + string.Join(" | ", fr.Axes.Select(x => string.Format("pic {0:0.000} ss {1:0.000} n={2}", x.Peak, x.SteadyState, x.Windows))), args[1], args[2], lg.Index)); }
                return 0;
            }
            return Check();
        }

        // -------------------------------------------------------------------------
        static SimFlight Simulate(int seed, double seconds, double zeta, double wnHz, double noiseAmp)
        {
            Random rnd = new Random(seed);
            double fs = 2000;
            int n = (int)(seconds * fs);
            SimFlight s = new SimFlight { N = n };
            foreach (string nm in Names) s.F[nm] = new int[n];
            double wn = 2 * Math.PI * wnHz, dt = 1 / fs;
            double[] y = new double[3], yd = new double[3], target = new double[3], spv = new double[3];
            double nextChange = 0;
            double thr = 0.4;
            for (int i = 0; i < n; i++)
            {
                double t = i * dt;
                if (t >= nextChange)
                {
                    for (int a = 0; a < 3; a++)
                    {
                        double r = rnd.NextDouble();
                        target[a] = r < 0.35 ? 0 : (rnd.NextDouble() * 2 - 1) * (a == 2 ? 300 : 650);
                    }
                    thr = 0.25 + 0.5 * rnd.NextDouble();
                    nextChange = t + 0.15 + rnd.NextDouble() * 0.7;
                }
                for (int a = 0; a < 3; a++)
                {
                    spv[a] += (target[a] - spv[a]) * (1 - Math.Exp(-dt / 0.012)); // lissage RC
                    double z = a == 2 ? 0.8 : zeta;
                    double acc = wn * wn * (spv[a] - y[a]) - 2 * z * wn * yd[a];
                    yd[a] += acc * dt; y[a] += yd[a] * dt;
                    double motorNoise = noiseAmp * Math.Sin(2 * Math.PI * (180 + 250 * thr) * t + a) + noiseAmp * 0.5 * (rnd.NextDouble() - 0.5);
                    double frame = 0.3 * noiseAmp * Math.Sin(2 * Math.PI * 160 * t);
                    double unf = y[a] + motorNoise + frame + 2 * (rnd.NextDouble() - 0.5);
                    double filt = y[a] + 0.08 * motorNoise + 0.5 * (rnd.NextDouble() - 0.5);
                    s.F["setpoint[" + a + "]"][i] = (int)Math.Round(spv[a]);
                    s.F["gyroADC[" + a + "]"][i] = (int)Math.Round(filt);
                    s.F["gyroUnfilt[" + a + "]"][i] = (int)Math.Round(unf);
                    double err = spv[a] - filt;
                    s.F["axisP[" + a + "]"][i] = (int)(err * 0.4);
                    s.F["axisI[" + a + "]"][i] = (int)(Math.Sin(t) * 20);
                    if (a < 2) s.F["axisD[" + a + "]"][i] = (int)(-yd[a] * 0.003 + motorNoise * 0.4);
                    s.F["axisF[" + a + "]"][i] = (int)(spv[a] * 0.05);
                    s.F["rcCommand[" + a + "]"][i] = (int)(spv[a] / 1.4);
                }
                s.F["rcCommand[3]"][i] = 1000 + (int)(thr * 1000);
                s.F["setpoint[3]"][i] = (int)(thr * 1000);
                for (int m = 0; m < 4; m++)
                {
                    double mv = 48 + 2000 * (thr * 0.9 + 0.05) + 15 * Math.Sin(2 * Math.PI * (180 + 250 * thr) * t + m) * noiseAmp / 10 + rnd.Next(-3, 4);
                    s.F["motor[" + m + "]"][i] = (int)Math.Max(48, Math.Min(2047, mv));
                }
                s.F["loopIteration"][i] = i * 4;
                s.F["time"][i] = 5000000 + (int)(i * 500 + rnd.Next(-2, 3)); // µs
            }
            return s;
        }

        static void Header(StringBuilder sb, string k, string v) { sb.Append("H ").Append(k).Append(':').Append(v).Append('\n'); }

        static byte[] EncodeLog(SimFlight s, string pids)
        {
            StringBuilder sb = new StringBuilder();
            Header(sb, "Product", "Blackbox flight data recorder by Nicholas Sherlock");
            Header(sb, "Data version", "2");
            Header(sb, "I interval", "256");
            Header(sb, "P interval", "4");
            Header(sb, "P ratio", "64");
            Header(sb, "Firmware type", "Cleanflight");
            Header(sb, "Firmware revision", "Betaflight 4.4.2 (simulation)");
            Header(sb, "Craft name", "SIMU");
            Header(sb, "looptime", "125");
            Header(sb, "gyro_sync_denom", "1");
            Header(sb, "pid_process_denom", "1");
            Header(sb, "minthrottle", "1070");
            Header(sb, "maxthrottle", "2000");
            Header(sb, "gyro_scale", "0x3f800000");
            Header(sb, "motorOutput", "48,2047");
            Header(sb, "vbatref", "420");
            string[] p = pids.Split(';');
            Header(sb, "rollPID", p[0]); Header(sb, "pitchPID", p[1]); Header(sb, "yawPID", p[2]);
            Header(sb, "d_min", p[3]); Header(sb, "ff_weight", p[4]);
            Header(sb, "simplified_pids_mode", "2");
            Header(sb, "Field I name", string.Join(",", Names));
            Header(sb, "Field I signed", string.Join(",", Signed));
            Header(sb, "Field I predictor", string.Join(",", IPred));
            Header(sb, "Field I encoding", string.Join(",", IEnc));
            Header(sb, "Field P predictor", string.Join(",", PPred));
            Header(sb, "Field P encoding", string.Join(",", PEnc));
            Header(sb, "Field S name", "flightModeFlags,stateFlags,failsafePhase,rxSignalReceived,rxFlightChannelsValid");
            Header(sb, "Field S signed", "0,0,0,0,0");
            Header(sb, "Field S predictor", "0,0,0,0,0");
            Header(sb, "Field S encoding", "1,1,7,9,9");
            Enc e = new Enc();
            e.B.AddRange(Encoding.ASCII.GetBytes(sb.ToString()));
            // événement sync beep
            e.Byte('E'); e.Byte(0); e.UVB(5000000);

            int n = Names.Length;
            int[] prev = new int[n], prev2 = new int[n], cur = new int[n], vals = new int[8];
            int motor0 = Array.IndexOf(Names, "motor[0]");
            for (int i = 0; i < s.N; i++)
            {
                for (int f = 0; f < n; f++) cur[f] = s.F[Names[f]][i];
                bool isI = cur[0] % 256 == 0;
                if (i % 1000 == 500)
                {   // trame S
                    e.Byte('S'); e.UVB(1); e.UVB(0); e.Tag2_3S32(new[] { 0, 0, 0 });
                }
                if (isI)
                {
                    e.Byte('I');
                    for (int f = 0; f < n; f++)
                    {
                        int v = cur[f];
                        if (IPred[f] == 4) v -= 1070;
                        if (IPred[f] == 11) v -= 48;
                        if (IPred[f] == 5) v -= cur[motor0];
                        if (IEnc[f] == 1) e.UVB((uint)v); else e.SVB(v);
                    }
                    Array.Copy(cur, prev, n); Array.Copy(cur, prev2, n);
                }
                else
                {
                    e.Byte('P');
                    int[] res = new int[n];
                    for (int f = 0; f < n; f++)
                    {
                        switch (PPred[f])
                        {
                            case 1: res[f] = cur[f] - prev[f]; break;
                            case 2: res[f] = cur[f] - (2 * prev[f] - prev2[f]); break;
                            case 3: res[f] = cur[f] - (int)(((long)prev[f] + prev2[f]) / 2); break;
                            default: res[f] = cur[f]; break;
                        }
                    }
                    for (int f = 0; f < n; f++)
                    {
                        switch (PEnc[f])
                        {
                            case 9: break;
                            case 0: e.SVB(res[f]); break;
                            case 1: e.UVB((uint)res[f]); break;
                            case 7: e.Tag2_3S32(new[] { res[f], res[f + 1], res[f + 2] }); f += 2; break;
                            case 8: e.Tag8_4S16(new[] { res[f], res[f + 1], res[f + 2], res[f + 3] }); f += 3; break;
                            case 6:
                                {
                                    int j = f; while (j < n && j < f + 8 && PEnc[j] == 6) j++;
                                    for (int k = 0; k < j - f; k++) vals[k] = res[f + k];
                                    e.Tag8_8SVB(vals, j - f); f = j - 1;
                                }
                                break;
                        }
                    }
                    Array.Copy(prev, prev2, n); Array.Copy(cur, prev, n);
                }
            }
            e.Byte('E'); e.Byte(255); e.B.AddRange(Encoding.ASCII.GetBytes("End of log")); e.Byte(0);
            return e.B.ToArray();
        }

        static byte[] BuildFile(out List<SimFlight> sims)
        {
            sims = new List<SimFlight>();
            List<byte> all = new List<byte>();
            SimFlight a = Simulate(1, 60, 0.32, 14, 10);   // sous-amorti -> doit proposer plus de D
            SimFlight b = Simulate(2, 0.8, 0.5, 14, 10);   // vol trop court
            SimFlight c = Simulate(3, 60, 0.75, 9, 6);    // sur-amorti / mou
            sims.Add(a); sims.Add(b); sims.Add(c);
            all.AddRange(EncodeLog(a, "45,80,30;47,84,32;45,80,0;20,22,0;120,125,80"));
            all.AddRange(new byte[] { 0xFF, 0xFF, 0x00, 0x13 }); // déchets entre logs
            all.AddRange(EncodeLog(b, "45,80,30;47,84,32;45,80,0;20,22,0;120,125,80"));
            all.AddRange(EncodeLog(c, "45,80,40;47,84,42;45,80,0;30,32,0;120,125,80"));
            return all.ToArray();
        }

        static int Check()
        {
            List<SimFlight> sims;
            byte[] data = BuildFile(out sims);
            List<FlightLog> logs = BblReader.Split(data);
            Console.WriteLine("Logs trouvés : " + logs.Count + " (attendu 3)");
            int errors = logs.Count == 3 ? 0 : 1;
            for (int k = 0; k < logs.Count && k < sims.Count; k++)
            {
                FlightLog log = logs[k];
                BblReader.Parse(data, log);
                SimFlight s = sims[k];
                Console.WriteLine(string.Format("Vol #{0}: parsed={1} err={2} trames={3}/{4} corrompues={5} fs={6:0.0} Hz durée={7:0.00}s",
                    log.Index, log.Parsed, log.ParseError, log.FrameCount, s.N, log.CorruptCount, log.SampleRate, log.Duration));
                if (log.FrameCount != s.N) errors++;
                int mism = 0;
                foreach (string nm in Names)
                {
                    if (nm == "time" || nm == "loopIteration") continue;
                    float[] col = log.Col(nm);
                    if (col == null) { Console.WriteLine("  colonne manquante " + nm); errors++; continue; }
                    for (int i = 0; i < Math.Min(col.Length, s.N); i++)
                        if ((int)col[i] != s.F[nm][i]) { if (mism < 5) Console.WriteLine("  diff " + nm + "[" + i + "] = " + col[i] + " attendu " + s.F[nm][i]); mism++; }
                }
                if (mism > 0) { Console.WriteLine("  " + mism + " différences"); errors++; }
                else Console.WriteLine("  toutes les valeurs décodées sont identiques ✔");
            }
            Console.WriteLine();
            Console.WriteLine("---- Analyse ----");
            foreach (FlightLog log in logs)
            {
                FlightResult r = Analyzer.Analyze(log);
                Print(r, PidSet.FromHeaders(log));
            }
            // théorie 2e ordre : pic = 1 + exp(-pi*z/sqrt(1-z^2))
            Console.WriteLine(string.Format("Pic théorique vol 1 (ζ=0.32) : {0:0.00} ; vol 3 (ζ=0.75) : {1:0.00}",
                1 + Math.Exp(-Math.PI * 0.32 / Math.Sqrt(1 - 0.32 * 0.32)), 1 + Math.Exp(-Math.PI * 0.75 / Math.Sqrt(1 - 0.75 * 0.75))));
            Console.WriteLine(errors == 0 ? "\nRÉSULTAT : OK" : "\nRÉSULTAT : " + errors + " ERREUR(S)");
            return errors == 0 ? 0 : 1;
        }

        static void Print(FlightResult r, PidSet pid)
        {
            FlightLog log = r.Logs[0];
            Console.WriteLine("== Vol #" + log.Index + " " + (r.Error ?? ""));
            for (int a = 0; a < 3; a++)
            {
                AxisResult ax = r.Axes[a];
                Console.WriteLine(string.Format("  {0,-5} fenêtres={1,4} pic={2:0.000} tpic={3:0}ms lat50={4:0.0}ms ss={5:0.000} ring={6:0.00} lag={7:0.0}ms rms={8:0.0} gyroHF={9:0.00} unfPeak={10:0}Hz",
                    Analyzer.AxisName(a), ax.Windows, ax.Peak, ax.TPeakMs, ax.Lat50Ms, ax.SteadyState, ax.RingAmp, ax.LagMs, ax.RmsErr, ax.GyroHF, ax.UnfPeakHz));
            }
            Console.WriteLine(string.Format("  motorNoise={0:0.00}% sat={1:0.0}% vol={2:0}s", r.MotorNoisePct, r.MotorSatPct, r.FlyingSec));
            if (r.Error != null) return;
            Recommendation rec = Recommender.Compute(r, pid);
            for (int a = 0; a < 3; a++)
                Console.WriteLine("  " + Analyzer.AxisName(a) + ": " + rec.Current.AxisString(a) + "  ->  " + rec.Proposed.AxisString(a));
            foreach (RecLine l in rec.Lines) Console.WriteLine("    [" + l.Kind + "] " + l.Text);
            Console.WriteLine(rec.Cli);
        }

        static int ConfigTest(string path)
        {
            BfConfig c = BfConfig.Load(path);
            Console.WriteLine("Version: " + c.Version + " | craft: " + c.CraftName + " | profils: " + string.Join(",", c.Profiles) + " | actif: " + c.ActiveProfile + " | clé D dyn: " + c.DynDKey(null));
            PidSet cur = new PidSet { Valid = true, HasF = true, HasDmin = true, DminKey = "d_max" };
            cur.P = new[] { 81, 84, 81 }; cur.I = new[] { 144, 151, 144 }; cur.D = new[] { 58, 66, 0 }; cur.F = new[] { 0, 0, 0 }; cur.Dmin = new[] { 78, 89, 0 };
            foreach (int p in c.Profiles) { int n; Console.WriteLine("  profil " + p + " correspond au vol : " + c.MatchesFlight(p, cur, out n) + " (" + n + " valeurs comparées)"); }
            PidSet pro = cur.Clone();
            pro.D = new[] { 64, 73, 0 }; pro.Dmin = new[] { 86, 98, 0 }; pro.I = new[] { 144, 160, 152 }; pro.F = new[] { 0, 0, 0 };
            List<ConfigChange> ch; HashSet<int> lines;
            List<string> outp = c.Apply(2, cur, pro, false, "BBL Tuner TEST", out ch, out lines);
            Console.WriteLine("Changements :");
            foreach (ConfigChange x in ch) Console.WriteLine("  " + x.Key + " : " + (x.Added ? "(défaut)" : x.Old) + " -> " + x.New);
            Console.WriteLine("Lignes surlignées : " + string.Join(",", lines.OrderBy(i => i)));
            Console.WriteLine("---- extrait profil 2 ----");
            int s = outp.FindIndex(l => l.Trim() == "profile 2"), e = outp.FindIndex(s + 1, l => l.Trim().StartsWith("profile 3"));
            for (int i = s; i < e; i++) Console.WriteLine((lines.Contains(i) ? " * " : "   ") + outp[i]);
            Console.WriteLine("---- en-tête ----");
            for (int i = 0; i < 6; i++) Console.WriteLine((lines.Contains(i) ? " * " : "   ") + outp[i]);
            Console.WriteLine("Lignes : " + c.Lines.Count + " -> " + outp.Count);
            List<ConfigChange> ch2; HashSet<int> l2;
            c.Apply(0, cur, pro, true, null, out ch2, out l2);
            Console.WriteLine("Profil 0 (ne correspond pas, écriture complète) : " + ch2.Count + " changements");
            return 0;
        }

        static int AnalyzeFile(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            List<FlightLog> logs = BblReader.Split(data);
            Console.WriteLine(path + " : " + logs.Count + " log(s)");
            foreach (FlightLog log in logs)
            {
                BblReader.Parse(data, log);
                Console.WriteLine(string.Format("Vol #{0}: parsed={1} err={2} trames={3} corrompues={4} fs={5:0.0} Hz durée={6:0.0}s fw={7}",
                    log.Index, log.Parsed, log.ParseError, log.FrameCount, log.CorruptCount, log.SampleRate, log.Duration, log.Firmware));
                FlightResult r = Analyzer.Analyze(log);
                Print(r, PidSet.FromHeaders(log));
            }
            return 0;
        }
    }
}
