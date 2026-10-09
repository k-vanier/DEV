// Décodeur natif des logs Blackbox Betaflight (.bbl / .bfl).
// Format : en-têtes texte "H clé:valeur" puis trames binaires (I, P, E, S, G, H).
// Un fichier peut contenir plusieurs logs (un par armement) : chacun commence par
// la ligne "H Product:Blackbox flight data recorder by Nicholas Sherlock".
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BblTuner
{
    public class FrameDef
    {
        public string[] Names = new string[0];
        public int[] Signed = new int[0];
        public int[] Predictor = new int[0];
        public int[] Encoding = new int[0];
        public int Count { get { return Names.Length; } }
        public int IndexOf(string name) { return Array.IndexOf(Names, name); }
    }

    public class FlightLog
    {
        public int Index;                 // 1-based
        public int ByteStart, ByteEnd, DataStart;
        public Dictionary<string, string> Headers = new Dictionary<string, string>();

        public bool Parsed;
        public string ParseError;
        public int FrameCount, CorruptCount, EventCount;
        public double[] Time;             // secondes depuis le début du log
        public Dictionary<string, float[]> Cols = new Dictionary<string, float[]>();
        public double SampleRate;

        public double Duration
        {
            get { return (Time == null || Time.Length < 2) ? 0 : Time[Time.Length - 1] - Time[0]; }
        }

        public float[] Col(string name)
        {
            float[] a;
            return Cols.TryGetValue(name, out a) ? a : null;
        }

        public string H(string key, string def)
        {
            string v;
            return Headers.TryGetValue(key, out v) ? v.Trim() : def;
        }

        public int[] HInts(string key)
        {
            string v;
            if (!Headers.TryGetValue(key, out v)) return null;
            return BblReader.ParseIntList(v);
        }

        public int HInt(string key, int def)
        {
            int[] a = HInts(key);
            return (a != null && a.Length > 0) ? a[0] : def;
        }

        public string Firmware
        {
            get
            {
                string fw = H("Firmware revision", null);
                if (fw == null) fw = H("Firmware version", "?");
                return fw;
            }
        }

        public string CraftName { get { return H("Craft name", ""); } }
    }

    public static class BblReader
    {
        static readonly byte[] Marker = Encoding.ASCII.GetBytes("H Product:Blackbox flight data recorder by Nicholas Sherlock");

        public static int[] ParseIntList(string v)
        {
            string[] parts = v.Split(',');
            List<int> res = new List<int>();
            foreach (string p in parts)
            {
                string s = p.Trim();
                if (s.Length == 0) continue;
                int x;
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out x)) res.Add(x);
                else
                {
                    double d;
                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) res.Add((int)Math.Round(d));
                    else res.Add(0);
                }
            }
            return res.ToArray();
        }

        /// Découpe le fichier en logs et lit leurs en-têtes.
        public static List<FlightLog> Split(byte[] data)
        {
            List<int> starts = new List<int>();
            for (int i = 0; i <= data.Length - Marker.Length; i++)
            {
                if (data[i] != (byte)'H') continue;
                bool ok = true;
                for (int j = 1; j < Marker.Length; j++)
                    if (data[i + j] != Marker[j]) { ok = false; break; }
                if (ok) { starts.Add(i); i += Marker.Length - 1; }
            }
            List<FlightLog> logs = new List<FlightLog>();
            for (int k = 0; k < starts.Count; k++)
            {
                FlightLog log = new FlightLog();
                log.Index = k + 1;
                log.ByteStart = starts[k];
                log.ByteEnd = (k + 1 < starts.Count) ? starts[k + 1] : data.Length;
                ReadHeaders(data, log);
                logs.Add(log);
            }
            return logs;
        }

        static void ReadHeaders(byte[] d, FlightLog log)
        {
            int pos = log.ByteStart;
            int end = log.ByteEnd;
            while (pos + 1 < end && d[pos] == (byte)'H' && d[pos + 1] == (byte)' ')
            {
                int nl = pos;
                while (nl < end && d[nl] != (byte)'\n') nl++;
                string line = Encoding.ASCII.GetString(d, pos + 2, nl - pos - 2).TrimEnd('\r');
                int colon = line.IndexOf(':');
                if (colon > 0)
                {
                    string key = line.Substring(0, colon);
                    log.Headers[key] = line.Substring(colon + 1);
                }
                pos = nl + 1;
            }
            log.DataStart = pos;
        }

        public static void Parse(byte[] data, FlightLog log)
        {
            try
            {
                LogParser p = new LogParser(data, log);
                p.Run();
                log.Parsed = true;
            }
            catch (Exception ex)
            {
                log.ParseError = ex.Message;
                log.Parsed = false;
            }
        }
    }

    /// Lecteur de trames pour un log.
    class LogParser
    {
        readonly byte[] d;
        readonly FlightLog log;
        int pos, end;
        bool eof, corrupt, logEnd;

        FrameDef defI, defP, defS, defG, defH;
        int minThrottle, vbatRef, motorMin;
        int iInterval, pNum, pDenom;
        int idxTime, idxIter, idxMotor0;
        long lastMainTime;
        readonly int[] vals = new int[8];

        // colonnes conservées
        static readonly string[] KeepPrefixes = {
            "setpoint[", "gyroADC[", "gyroUnfilt[", "axisP[", "axisI[", "axisD[", "axisF[",
            "motor[", "rcCommand[", "debug[", "eRPM[" };

        public LogParser(byte[] data, FlightLog log)
        {
            d = data;
            this.log = log;
            pos = log.DataStart;
            end = log.ByteEnd;

            defI = MakeDef('I', null);
            defP = MakeDef('P', defI);
            defS = MakeDef('S', null);
            defG = MakeDef('G', null);
            defH = MakeDef('H', null);
            if (defI.Count == 0) throw new Exception("En-tête 'Field I name' absent : fichier non reconnu.");

            minThrottle = log.HInt("minthrottle", 1150);
            vbatRef = log.HInt("vbatref", 0);
            int[] mo = log.HInts("motorOutput");
            motorMin = (mo != null && mo.Length > 0) ? mo[0] : minThrottle;

            iInterval = Math.Max(1, log.HInt("I interval", 32));
            pNum = 1; pDenom = 1;
            string pi = log.H("P interval", null);
            if (pi != null)
            {
                int slash = pi.IndexOf('/');
                if (slash >= 0)
                {
                    int.TryParse(pi.Substring(0, slash), out pNum);
                    int.TryParse(pi.Substring(slash + 1), out pDenom);
                }
                else
                {
                    pNum = 1;
                    int.TryParse(pi, out pDenom);
                }
                if (pNum < 1) pNum = 1;
                if (pDenom < 1) pDenom = 1;
            }

            idxTime = defI.IndexOf("time");
            idxIter = defI.IndexOf("loopIteration");
            idxMotor0 = defI.IndexOf("motor[0]");
            if (idxTime < 0) throw new Exception("Champ 'time' absent du log.");
        }

        FrameDef MakeDef(char t, FrameDef namesFrom)
        {
            FrameDef def = new FrameDef();
            string names = log.H("Field " + t + " name", null);
            if (names == null && namesFrom != null)
            {
                def.Names = namesFrom.Names;
                def.Signed = namesFrom.Signed;
            }
            else if (names != null)
            {
                def.Names = names.Split(',');
                int[] s = log.HInts("Field " + t + " signed");
                def.Signed = Fit(s, def.Count);
            }
            else return def;
            def.Predictor = Fit(log.HInts("Field " + t + " predictor"), def.Count);
            def.Encoding = Fit(log.HInts("Field " + t + " encoding"), def.Count);
            return def;
        }

        static int[] Fit(int[] a, int n)
        {
            int[] r = new int[n];
            if (a != null) Array.Copy(a, r, Math.Min(n, a.Length));
            return r;
        }

        // ---------- lecture bas niveau ----------
        int RB()
        {
            if (pos >= end) { eof = true; return 0; }
            return d[pos++];
        }

        uint ReadUVB()
        {
            uint result = 0;
            for (int i = 0; i < 5; i++)
            {
                int b = RB();
                if (eof) return 0;
                result |= (uint)(b & 0x7F) << (7 * i);
                if (b < 128) return result;
            }
            corrupt = true;
            return 0;
        }

        int ReadSVB()
        {
            uint u = ReadUVB();
            return (int)(u >> 1) ^ -(int)(u & 1);
        }

        static int SX(int v, int bits)
        {
            int shift = 32 - bits;
            return (v << shift) >> shift;
        }

        void ReadTag8_8SVB(int count)
        {
            if (count == 1) { vals[0] = ReadSVB(); return; }
            int header = RB();
            for (int i = 0; i < 8; i++, header >>= 1)
                vals[i] = (header & 1) != 0 ? ReadSVB() : 0;
        }

        void ReadTag2_3S32()
        {
            int lead = RB();
            switch (lead >> 6)
            {
                case 0:
                    vals[0] = SX((lead >> 4) & 3, 2);
                    vals[1] = SX((lead >> 2) & 3, 2);
                    vals[2] = SX(lead & 3, 2);
                    break;
                case 1:
                    vals[0] = SX(lead & 0x0F, 4);
                    lead = RB();
                    vals[1] = SX(lead >> 4, 4);
                    vals[2] = SX(lead & 0x0F, 4);
                    break;
                case 2:
                    vals[0] = SX(lead & 0x3F, 6);
                    vals[1] = SX(RB() & 0x3F, 6);
                    vals[2] = SX(RB() & 0x3F, 6);
                    break;
                default:
                    ReadVariable3(lead);
                    break;
            }
        }

        void ReadVariable3(int lead)
        {
            for (int i = 0; i < 3; i++)
            {
                switch (lead & 3)
                {
                    case 0: vals[i] = (sbyte)RB(); break;
                    case 1: { int b1 = RB(), b2 = RB(); vals[i] = (short)(b1 | (b2 << 8)); } break;
                    case 2: { int b1 = RB(), b2 = RB(), b3 = RB(); vals[i] = SX(b1 | (b2 << 8) | (b3 << 16), 24); } break;
                    default: { int b1 = RB(), b2 = RB(), b3 = RB(), b4 = RB(); vals[i] = b1 | (b2 << 8) | (b3 << 16) | (b4 << 24); } break;
                }
                lead >>= 2;
            }
        }

        void ReadTag2_3SVariable()
        {
            int lead = RB();
            switch (lead >> 6)
            {
                case 0:
                    vals[0] = SX((lead >> 4) & 3, 2);
                    vals[1] = SX((lead >> 2) & 3, 2);
                    vals[2] = SX(lead & 3, 2);
                    break;
                case 1:
                    {
                        vals[0] = SX((lead & 0x3E) >> 1, 5);
                        int b1 = RB();
                        vals[1] = SX(((lead & 1) << 4) | ((b1 & 0xF0) >> 4), 5);
                        vals[2] = SX(b1 & 0x0F, 4);
                    }
                    break;
                case 2:
                    {
                        int b1 = RB();
                        vals[0] = SX(((lead & 0x3F) << 2) | ((b1 & 0xC0) >> 6), 8);
                        int b2 = RB();
                        vals[1] = SX(((b1 & 0x3F) << 1) | ((b2 & 0x80) >> 7), 7);
                        vals[2] = SX(b2 & 0x7F, 7);
                    }
                    break;
                default:
                    ReadVariable3(lead);
                    break;
            }
        }

        /// Data version 2 : les champs sont empaquetés par demi-octets, poids fort d'abord
        /// (miroir de blackboxWriteTag8_4S16 dans le firmware Betaflight).
        void ReadTag8_4S16()
        {
            int selector = RB();
            int nibble = 0, buffer = 0;
            for (int i = 0; i < 4; i++, selector >>= 2)
            {
                switch (selector & 3)
                {
                    case 0: vals[i] = 0; break;
                    case 1: // 4 bits
                        if (nibble == 0) { buffer = RB(); vals[i] = SX(buffer >> 4, 4); nibble = 1; }
                        else { vals[i] = SX(buffer & 0x0F, 4); nibble = 0; }
                        break;
                    case 2: // 8 bits
                        if (nibble == 0) vals[i] = (sbyte)RB();
                        else
                        {
                            int c1 = (buffer & 0x0F) << 4;
                            buffer = RB();
                            vals[i] = SX(c1 | (buffer >> 4), 8);
                        }
                        break;
                    default: // 16 bits, octet de poids fort d'abord
                        if (nibble == 0) { int c1 = RB(), c2 = RB(); vals[i] = SX((c1 << 8) | c2, 16); }
                        else
                        {
                            int c1 = RB(), c2 = RB();
                            vals[i] = SX(((buffer & 0x0F) << 12) | (c1 << 4) | (c2 >> 4), 16);
                            buffer = c2;
                        }
                        break;
                }
            }
        }

        // ---------- prédiction ----------
        int Predict(FrameDef def, int i, int pred, int v, int[] frame, int[] prev, int[] prev2)
        {
            unchecked
            {
                switch (pred)
                {
                    case 0: return v;
                    case 1: return prev == null ? v : v + prev[i];
                    case 2: return prev == null ? v : v + 2 * prev[i] - prev2[i];
                    case 3:
                        if (prev == null) return v;
                        if (def.Signed[i] != 0) return v + (int)(((long)prev[i] + prev2[i]) / 2);
                        return v + (int)(((ulong)(uint)prev[i] + (uint)prev2[i]) / 2);
                    case 4: return v + minThrottle;
                    case 5: return (def == defI || def == defP) && idxMotor0 >= 0 ? v + frame[idxMotor0] : v;
                    case 8: return v + 1500;
                    case 9: return v + vbatRef;
                    case 10: return v + (int)lastMainTime;
                    case 11: return v + motorMin;
                    default: return v; // 7 = HOME_COORD (GPS, inutile ici)
                }
            }
        }

        void ParseFrame(FrameDef def, int[] frame, int[] prev, int[] prev2, int skipped)
        {
            int n = def.Count;
            for (int i = 0; i < n; i++)
            {
                if (eof || corrupt) return;
                if (def.Predictor[i] == 6)
                {
                    frame[i] = skipped + 1 + (prev != null ? prev[i] : 0);
                    continue;
                }
                int v;
                switch (def.Encoding[i])
                {
                    case 0: v = ReadSVB(); break;
                    case 1: v = (int)ReadUVB(); break;
                    case 3: v = -SX((int)ReadUVB(), 14); break;
                    case 6:
                        {
                            int j = i + 1;
                            while (j < n && j < i + 8 && def.Encoding[j] == 6) j++;
                            int cnt = j - i;
                            ReadTag8_8SVB(cnt);
                            for (int k = 0; k < cnt; k++)
                                frame[i + k] = Predict(def, i + k, def.Predictor[i + k], vals[k], frame, prev, prev2);
                            i += cnt - 1;
                            continue;
                        }
                    case 7:
                        ReadTag2_3S32();
                        for (int k = 0; k < 3 && i + k < n; k++)
                            frame[i + k] = Predict(def, i + k, def.Predictor[i + k], vals[k], frame, prev, prev2);
                        i += 2;
                        continue;
                    case 8:
                        ReadTag8_4S16();
                        for (int k = 0; k < 4 && i + k < n; k++)
                            frame[i + k] = Predict(def, i + k, def.Predictor[i + k], vals[k], frame, prev, prev2);
                        i += 3;
                        continue;
                    case 10:
                        ReadTag2_3SVariable();
                        for (int k = 0; k < 3 && i + k < n; k++)
                            frame[i + k] = Predict(def, i + k, def.Predictor[i + k], vals[k], frame, prev, prev2);
                        i += 2;
                        continue;
                    case 9: v = 0; break;
                    default:
                        corrupt = true; // encodage Elias (non utilisé par Betaflight)
                        return;
                }
                frame[i] = Predict(def, i, def.Predictor[i], v, frame, prev, prev2);
            }
        }

        bool ShouldHaveFrame(long idx)
        {
            return ((idx % iInterval) + pNum - 1) % pDenom < pNum;
        }

        int CountSkipped(long lastIter)
        {
            if (pNum == 1 && pDenom == 1) return 0;
            int count = 0;
            for (long idx = lastIter + 1; !ShouldHaveFrame(idx) && count < 100000; idx++) count++;
            return count;
        }

        bool ParseEvent()
        {
            int type = RB();
            switch (type)
            {
                case 0: ReadUVB(); return true;                   // SYNC_BEEP
                case 13:                                          // INFLIGHT_ADJUSTMENT
                    {
                        int f = RB();
                        if ((f & 128) != 0) { RB(); RB(); RB(); RB(); }
                        else ReadSVB();
                        return true;
                    }
                case 14: ReadUVB(); ReadUVB(); return true;       // LOGGING_RESUME
                case 15: ReadUVB(); return true;                  // DISARM
                case 30: ReadUVB(); ReadUVB(); return true;       // FLIGHT_MODE
                case 255:                                         // LOG_END
                    {
                        const string msg = "End of log";
                        for (int i = 0; i < msg.Length; i++)
                            if (RB() != msg[i]) return false;
                        if (RB() != 0) return false;
                        logEnd = true;
                        return true;
                    }
                default: return false;
            }
        }

        bool IsMarker(byte b)
        {
            switch ((char)b)
            {
                case 'I': case 'P': case 'E': case 'S': return true;
                case 'G': return defG.Count > 0;
                case 'H': return defH.Count > 0;
                default: return false;
            }
        }

        public void Run()
        {
            int n = defI.Count;
            int[] tmp = new int[n], prev = new int[n], prev2 = new int[n];
            int[] sFrame = new int[Math.Max(1, defS.Count)];
            int[] gFrame = new int[Math.Max(1, defG.Count)];
            int[] hFrame = new int[Math.Max(1, defH.Count)];
            bool mainValid = false;
            lastMainTime = 0;

            // colonnes à conserver
            List<int> keepIdx = new List<int>();
            for (int i = 0; i < n; i++)
                foreach (string pfx in KeepPrefixes)
                    if (defI.Names[i].StartsWith(pfx, StringComparison.Ordinal)) { keepIdx.Add(i); break; }
            List<int>[] store = new List<int>[keepIdx.Count];
            for (int k = 0; k < store.Length; k++) store[k] = new List<int>(1 << 16);
            List<long> times = new List<long>(1 << 16);
            long timeHigh = 0; uint lastRawTime = 0; bool haveTime = false;

            int frames = 0, bad = 0, events = 0;

            while (!logEnd && pos < end)
            {
                int frameStart = pos;
                char c = (char)d[pos++];
                eof = false; corrupt = false;
                bool known = true;
                switch (c)
                {
                    case 'I':
                        ParseFrame(defI, tmp, mainValid ? prev : null, mainValid ? prev2 : null, 0);
                        break;
                    case 'P':
                        {
                            if (defP.Count == 0) { known = false; break; }
                            int skipped = (mainValid && idxIter >= 0) ? CountSkipped(prev[idxIter]) : 0;
                            ParseFrame(defP, tmp, prev, prev2, skipped);
                        }
                        break;
                    case 'E':
                        known = ParseEvent();
                        break;
                    case 'S':
                        if (defS.Count == 0) { known = false; break; }
                        ParseFrame(defS, sFrame, null, null, 0);
                        break;
                    case 'G':
                        if (defG.Count == 0) { known = false; break; }
                        ParseFrame(defG, gFrame, null, null, 0);
                        break;
                    case 'H':
                        if (defH.Count == 0) { known = false; break; }
                        ParseFrame(defH, hFrame, null, null, 0);
                        break;
                    default:
                        known = false;
                        break;
                }
                if (eof) break;
                bool complete = known && !corrupt && (logEnd || pos >= end || IsMarker(d[pos]));
                if (!complete)
                {
                    bad++;
                    mainValid = false;
                    pos = frameStart + 1;
                    continue;
                }
                if (c == 'E') { events++; continue; }
                if (c != 'I' && c != 'P') continue;

                if (c == 'P')
                {
                    if (!mainValid) continue;
                    uint dt = unchecked((uint)tmp[idxTime] - (uint)prev[idxTime]);
                    if (dt == 0 || dt > 10000000u) { bad++; mainValid = false; continue; }
                }

                // validation OK : on enregistre la trame
                Array.Copy(prev, prev2, n);
                Array.Copy(tmp, prev, n);
                if (c == 'I') Array.Copy(tmp, prev2, n);
                mainValid = true;
                lastMainTime = tmp[idxTime];

                uint raw = (uint)tmp[idxTime];
                if (haveTime && raw < lastRawTime && lastRawTime - raw > 0x80000000u) timeHigh += 0x100000000L;
                lastRawTime = raw; haveTime = true;
                long t = timeHigh + raw;
                if (times.Count > 0 && t <= times[times.Count - 1]) continue; // doublon / retour arrière
                times.Add(t);
                for (int k = 0; k < keepIdx.Count; k++) store[k].Add(tmp[keepIdx[k]]);
                frames++;
            }

            log.FrameCount = frames;
            log.CorruptCount = bad;
            log.EventCount = events;

            // conversion
            double t0 = times.Count > 0 ? times[0] : 0;
            log.Time = new double[times.Count];
            for (int i = 0; i < times.Count; i++) log.Time[i] = (times[i] - t0) * 1e-6;

            double gyroScale = 1.0;
            string gs = log.H("gyro_scale", null);
            if (gs != null && gs.StartsWith("0x"))
            {
                uint bits;
                if (uint.TryParse(gs.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bits))
                {
                    float f = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
                    if (f > 0 && !float.IsInfinity(f) && !float.IsNaN(f)) gyroScale = f;
                }
            }

            // Betaflight 4.5+ : avec blackbox_high_resolution, gyro et consignes sont enregistrés x10
            double highRes = log.HInt("blackbox_high_resolution", 0) != 0 ? 0.1 : 1.0;

            for (int k = 0; k < keepIdx.Count; k++)
            {
                string name = defI.Names[keepIdx[k]];
                List<int> src = store[k];
                float[] arr = new float[src.Count];
                double scale = 1.0;
                if (name.StartsWith("gyroADC[") || name.StartsWith("gyroUnfilt[")) scale = gyroScale * highRes;
                else if (name == "setpoint[0]" || name == "setpoint[1]" || name == "setpoint[2]") scale = highRes;
                for (int i = 0; i < arr.Length; i++) arr[i] = (float)(src[i] * scale);
                log.Cols[name] = arr;
            }

            // fréquence d'échantillonnage (médiane des dt)
            if (log.Time.Length > 10)
            {
                int m = Math.Min(log.Time.Length - 1, 20000);
                double[] dts = new double[m];
                for (int i = 0; i < m; i++) dts[i] = log.Time[i + 1] - log.Time[i];
                Array.Sort(dts);
                double med = dts[m / 2];
                log.SampleRate = med > 0 ? 1.0 / med : 0;
            }
        }
    }
}
