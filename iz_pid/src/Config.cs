// Lecture / correction d'un fichier de configuration Betaflight (sortie CLI "diff all" ou "dump").
// Le fichier est modifié ligne à ligne : tout ce qui n'est pas un réglage PID reste identique.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BblTuner
{
    public class ConfigChange
    {
        public string Key, Old, New;
        public bool Added;
    }

    public class BfConfig
    {
        public string Path;
        public List<string> Lines = new List<string>();
        public string Version = "";
        public string CraftName = "";
        public int ActiveProfile;
        public List<int> Profiles = new List<int>();

        static readonly Regex SetRe = new Regex(@"^\s*set\s+([A-Za-z0-9_]+)\s*=\s*(.*?)\s*$");
        static readonly Regex ProfileRe = new Regex(@"^\s*profile\s+(\d+)\s*$");
        static readonly string[] AxisNames = { "roll", "pitch", "yaw" };

        /// Vrai si le texte ressemble à une config Betaflight (et pas à un log blackbox).
        public static bool LooksLikeConfig(string path)
        {
            try
            {
                byte[] head = new byte[4096];
                int n;
                using (FileStream fs = File.OpenRead(path)) n = fs.Read(head, 0, head.Length);
                string s = Encoding.ASCII.GetString(head, 0, n);
                if (s.Contains("H Product:Blackbox")) return false;
                return s.Contains("# version") || s.Contains("batch start") || Regex.IsMatch(s, @"(?m)^\s*set\s+\w+\s*=");
            }
            catch { return false; }
        }

        public static BfConfig Load(string path)
        {
            BfConfig c = new BfConfig { Path = path };
            c.Lines = File.ReadAllLines(path).ToList();
            for (int i = 0; i < c.Lines.Count; i++)
            {
                string l = c.Lines[i].Trim();
                if (l.StartsWith("# Betaflight") || l.StartsWith("# INAV") || l.StartsWith("# Emuflight")) c.Version = l.TrimStart('#', ' ');
                Match m = SetRe.Match(l);
                if (m.Success && m.Groups[1].Value == "craft_name") c.CraftName = m.Groups[2].Value;
                if (c.CraftName.Length == 0 && l.StartsWith("# name:")) c.CraftName = l.Substring(7).Trim();
                Match p = ProfileRe.Match(l);
                if (p.Success)
                {
                    int idx = int.Parse(p.Groups[1].Value);
                    if (!c.Profiles.Contains(idx)) c.Profiles.Add(idx);
                }
            }
            c.Profiles.Sort();
            // profil actif : celui sélectionné après "# restore original profile selection" (diff all), sinon le dernier "profile N"
            c.ActiveProfile = c.Profiles.Count > 0 ? c.Profiles[0] : 0;
            int restore = c.Lines.FindIndex(x => x.Trim().StartsWith("# restore original profile selection"));
            if (restore >= 0)
            {
                for (int i = restore + 1; i < c.Lines.Count; i++)
                {
                    Match p = ProfileRe.Match(c.Lines[i]);
                    if (p.Success) { c.ActiveProfile = int.Parse(p.Groups[1].Value); break; }
                    if (c.Lines[i].Trim().Length > 0 && !c.Lines[i].Trim().StartsWith("#")) break;
                }
            }
            return c;
        }

        /// Lignes [start, end) du bloc d'un profil. Sans commande "profile", le fichier entier.
        public void ProfileRange(int profile, out int start, out int end)
        {
            start = -1; end = Lines.Count;
            if (Profiles.Count == 0) { start = 0; return; }
            for (int i = 0; i < Lines.Count; i++)
            {
                Match p = ProfileRe.Match(Lines[i]);
                if (p.Success && int.Parse(p.Groups[1].Value) == profile) { start = i; break; }
            }
            if (start < 0) return;
            for (int i = start + 1; i < Lines.Count; i++)
            {
                string l = Lines[i].Trim();
                if (ProfileRe.IsMatch(l) || l.StartsWith("rateprofile") || l.StartsWith("# rateprofile") ||
                    l.StartsWith("# restore original") || l == "save" || l == "batch end" || l.StartsWith("# save"))
                { end = i; break; }
            }
        }

        public Dictionary<string, string> ProfileValues(int profile)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            int s, e;
            ProfileRange(profile, out s, out e);
            if (s < 0) return d;
            for (int i = s; i < e; i++)
            {
                Match m = SetRe.Match(Lines[i]);
                if (m.Success) d[m.Groups[1].Value] = m.Groups[2].Value;
            }
            return d;
        }

        /// Clé du D dynamique utilisée par ce firmware ("d_max" en 4.6, "d_min" en 4.3–4.5).
        public string DynDKey(PidSet fromLog)
        {
            foreach (string l in Lines)
            {
                if (l.Contains("set d_max_")) return "d_max";
                if (l.Contains("set d_min_")) return "d_min";
            }
            if (Version.Contains(" 4.6") || Version.Contains(" 2025") || Version.Contains(" 2026")) return "d_max";
            return fromLog != null ? fromLog.DminKey : "d_min";
        }

        /// Vrai si les PID écrits dans ce profil correspondent à ceux du vol (les valeurs absentes = valeurs par défaut, ignorées).
        public bool MatchesFlight(int profile, PidSet cur, out int compared)
        {
            compared = 0;
            Dictionary<string, string> v = ProfileValues(profile);
            for (int a = 0; a < 3; a++)
            {
                if (!Check(v, "p_" + AxisNames[a], cur.P[a], ref compared)) return false;
                if (!Check(v, "i_" + AxisNames[a], cur.I[a], ref compared)) return false;
                if (!Check(v, "d_" + AxisNames[a], cur.D[a], ref compared)) return false;
                if (cur.HasF && !Check(v, "f_" + AxisNames[a], cur.F[a], ref compared)) return false;
            }
            return true;
        }

        static bool Check(Dictionary<string, string> v, string key, int expected, ref int compared)
        {
            string s;
            if (!v.TryGetValue(key, out s)) return true;
            int x;
            if (!int.TryParse(s, out x)) return true;
            compared++;
            return x == expected;
        }

        /// Écrit les PID proposés dans le profil choisi. Retourne le nouveau texte et la liste des changements.
        /// fullWrite = true : toutes les valeurs sont écrites (profil différent du vol analysé).
        public List<string> Apply(int profile, PidSet cur, PidSet pro, bool fullWrite, string note,
                                  out List<ConfigChange> changes, out HashSet<int> changedLines)
        {
            changes = new List<ConfigChange>();
            changedLines = new HashSet<int>();
            List<string> outLines = new List<string>(Lines);
            int s, e;
            ProfileRange(profile, out s, out e);
            if (s < 0) return outLines;
            Dictionary<string, string> existing = ProfileValues(profile);
            string dk = DynDKey(cur);

            // valeurs cibles, dans l'ordre d'écriture
            List<KeyValuePair<string, string>> targets = new List<KeyValuePair<string, string>>();
            List<string> skipIfMissing = new List<string>();
            for (int a = 0; a < 3; a++)
            {
                string ax = AxisNames[a];
                AddTarget(targets, skipIfMissing, "p_" + ax, pro.P[a], cur.P[a], fullWrite);
                AddTarget(targets, skipIfMissing, "i_" + ax, pro.I[a], cur.I[a], fullWrite);
                AddTarget(targets, skipIfMissing, "d_" + ax, pro.D[a], cur.D[a], fullWrite);
                if (pro.HasF) AddTarget(targets, skipIfMissing, "f_" + ax, pro.F[a], cur.F[a], fullWrite);
                // D dynamique : valeur du log si connue, sinon on garde le même rapport avec la D
                string dkey = dk + "_" + ax;
                string ex;
                if (pro.HasDmin && pro.DminKey == dk) AddTarget(targets, skipIfMissing, dkey, pro.Dmin[a], cur.Dmin[a], fullWrite);
                else if (existing.TryGetValue(dkey, out ex))
                {
                    int v;
                    if (int.TryParse(ex, out v) && cur.D[a] > 0 && pro.D[a] != cur.D[a])
                        targets.Add(new KeyValuePair<string, string>(dkey, ((int)Math.Round(v * (double)pro.D[a] / cur.D[a])).ToString()));
                }
            }
            targets.Add(new KeyValuePair<string, string>("simplified_pids_mode", "OFF"));

            // remplacement des lignes existantes
            List<KeyValuePair<string, string>> toAdd = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, string> t in targets)
            {
                int idx = -1;
                for (int i = s; i < e; i++)
                {
                    Match m = SetRe.Match(outLines[i]);
                    if (m.Success && m.Groups[1].Value == t.Key) { idx = i; break; }
                }
                if (idx >= 0)
                {
                    string old = SetRe.Match(outLines[idx]).Groups[2].Value;
                    if (string.Equals(old, t.Value, StringComparison.OrdinalIgnoreCase)) continue;
                    outLines[idx] = "set " + t.Key + " = " + t.Value;
                    changedLines.Add(idx);
                    changes.Add(new ConfigChange { Key = t.Key, Old = old, New = t.Value });
                }
                else
                {
                    if (skipIfMissing.Contains(t.Key)) continue; // valeur par défaut déjà égale à la cible
                    toAdd.Add(t);
                }
            }

            // ajout des réglages absents, à la fin du bloc du profil
            if (toAdd.Count > 0)
            {
                int insertAt = s + 1;
                bool hasSet = false;
                for (int i = s; i < e; i++) if (SetRe.IsMatch(outLines[i])) { insertAt = i + 1; hasSet = true; }
                List<string> block = new List<string>();
                if (!hasSet && Profiles.Count > 0) { block.Add(""); block.Add("# profile " + profile); }
                block.Add("# ajouté par BBL Tuner");
                foreach (KeyValuePair<string, string> t in toAdd)
                {
                    block.Add("set " + t.Key + " = " + t.Value);
                    changes.Add(new ConfigChange { Key = t.Key, Old = null, New = t.Value, Added = true });
                }
                // décale les numéros de lignes déjà modifiées situées après le point d'insertion
                HashSet<int> shifted = new HashSet<int>();
                foreach (int i in changedLines) shifted.Add(i >= insertAt ? i + block.Count : i);
                changedLines = shifted;
                outLines.InsertRange(insertAt, block);
                for (int k = 0; k < block.Count; k++) if (block[k].StartsWith("set ") || block[k].StartsWith("# ajouté")) changedLines.Add(insertAt + k);
            }

            // note en tête de fichier
            if (changes.Count > 0 && note != null)
            {
                int at = outLines.FindIndex(x => x.Trim().StartsWith("# config rev"));
                at = at >= 0 ? at + 1 : 0;
                outLines.Insert(at, "# " + note);
                HashSet<int> shifted = new HashSet<int>();
                foreach (int i in changedLines) shifted.Add(i >= at ? i + 1 : i);
                changedLines = shifted;
                changedLines.Add(at);
            }
            return outLines;
        }

        static void AddTarget(List<KeyValuePair<string, string>> targets, List<string> skipIfMissing, string key, int pro, int cur, bool full)
        {
            targets.Add(new KeyValuePair<string, string>(key, pro.ToString()));
            if (!full && pro == cur) skipIfMissing.Add(key);
        }
    }
}
