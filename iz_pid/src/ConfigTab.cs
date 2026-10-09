// Onglet "Config Betaflight" : ouvre une config (diff all), y écrit les PID proposés et enregistre une copie corrigée.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace BblTuner
{
    public partial class MainForm
    {
        TabPage tpConfigPage;
        BfConfig config;
        FlowLayoutPanel cfgProfiles;
        RadioButton[] rbCfgProfiles = new RadioButton[0];
        Label lblCfgFile, lblCfgInfo;
        DataGridView gridCfg;
        RichTextBox rtbCfg;
        Button btnCfgSave, btnCfgCopy;
        List<string> cfgOutput;
        bool cfgUpdating, cfgMismatch;

        TabPage BuildConfigTab()
        {
            TabPage tp = new TabPage("Config");
            tpConfigPage = tp;

            FlowLayoutPanel bar = NewBar();
            Button btnOpenCfg = Btn("Ouvrir une config…", 150);
            btnOpenCfg.Click += (s, e) => OpenConfigDialog();
            lblCfgFile = new Label { Tag = "title", AutoSize = true, Font = new Font("Segoe UI Semibold", 10f), Margin = new Padding(S(6), S(6), S(16), 0) };
            cfgProfiles = NewGroup();
            FlowLayoutPanel gAct = NewGroup();
            btnCfgSave = Btn("Enregistrer la config corrigée…", 220);
            btnCfgCopy = Btn("Copier", 80);
            btnCfgSave.Click += (s, e) => SaveConfig();
            btnCfgCopy.Click += (s, e) =>
            {
                if (cfgOutput == null || !ConfirmMismatch()) return;
                Clipboard.SetText(string.Join("\r\n", cfgOutput) + "\r\n");
                lblStatus.Text = "Config corrigée copiée : colle-la dans l'onglet CLI de Betaflight Configurator.";
            };
            gAct.Controls.Add(btnCfgSave); gAct.Controls.Add(btnCfgCopy);
            bar.Controls.Add(btnOpenCfg); bar.Controls.Add(lblCfgFile); bar.Controls.Add(cfgProfiles); bar.Controls.Add(gAct);

            lblCfgInfo = new Label { Tag = "value", Dock = DockStyle.Top, AutoSize = false, Height = S(44), Padding = new Padding(S(12), S(4), S(12), 0), Font = new Font("Segoe UI", 9.5f) };

            Panel body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(10), 0, S(10), S(10)) };
            gridCfg = new DataGridView
            {
                Dock = DockStyle.Left, Width = S(330), ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BorderStyle = BorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
            };
            gridCfg.RowTemplate.Height = S(26);
            gridCfg.DefaultCellStyle.Font = new Font("Consolas", 9.5f);
            gridCfg.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9f);
            gridCfg.Columns.Add("k", "Réglage"); gridCfg.Columns.Add("o", "Actuel"); gridCfg.Columns.Add("n", "Corrigé");
            gridCfg.Columns[0].FillWeight = 150;
            Panel gap = new Panel { Dock = DockStyle.Left, Width = S(10) };
            Panel prevCard = new Panel { Dock = DockStyle.Fill, Tag = "console-card", Padding = new Padding(S(10), S(8), S(4), S(4)) };
            rtbCfg = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10f), WordWrap = false, Tag = "console", DetectUrls = false };
            prevCard.Controls.Add(rtbCfg);
            body.Controls.Add(prevCard); body.Controls.Add(gap); body.Controls.Add(gridCfg);

            tp.Controls.Add(body); tp.Controls.Add(lblCfgInfo); tp.Controls.Add(bar);
            Theme.Current.StyleControls(tp);
            RefreshConfig();
            return tp;
        }

        void OpenConfigDialog()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "Config Betaflight (*.txt)|*.txt|Tous les fichiers (*.*)|*.*";
                dlg.Title = "Ouvrir une configuration Betaflight (sortie CLI « diff all »)";
                if (dlg.ShowDialog(this) == DialogResult.OK) LoadConfig(dlg.FileName);
            }
        }

        void LoadConfig(string path)
        {
            try { config = BfConfig.Load(path); }
            catch (Exception ex) { ShowMessage("Lecture impossible : " + ex.Message, true); return; }
            BuildProfileButtons();
            tabs.SelectedTab = tpConfigPage;
            RefreshConfig();
        }

        void BuildProfileButtons()
        {
            cfgProfiles.Controls.Clear();
            if (config == null) { rbCfgProfiles = new RadioButton[0]; return; }
            List<int> profs = config.Profiles.Count > 0 ? config.Profiles : new List<int> { 0 };
            rbCfgProfiles = new RadioButton[profs.Count];
            cfgProfiles.Controls.Add(SubLabel("Profil à corriger :", 0));
            for (int i = 0; i < profs.Count; i++)
            {
                int p = profs[i];
                RadioButton rb = Seg("Profil " + (p + 1), 74, null);
                rb.Tag = null;
                rb.Name = p.ToString();
                rb.CheckedChanged += (s, e) => { if (((RadioButton)s).Checked && !cfgUpdating) RefreshConfig(); };
                rbCfgProfiles[i] = rb;
                cfgProfiles.Controls.Add(rb);
            }
            // présélection : le profil dont les PID correspondent au vol, sinon le profil actif
            int pick = config.ActiveProfile;
            if (lastRec != null)
            {
                foreach (int p in profs)
                {
                    int n;
                    if (config.MatchesFlight(p, lastRec.Current, out n) && n >= 3) { pick = p; break; }
                }
            }
            cfgUpdating = true;
            foreach (RadioButton rb in rbCfgProfiles) rb.Checked = rb.Name == pick.ToString();
            cfgUpdating = false;
            Theme.Current.StyleControls(cfgProfiles);
        }

        int SelectedCfgProfile()
        {
            foreach (RadioButton rb in rbCfgProfiles) if (rb.Checked) return int.Parse(rb.Name);
            return config != null ? config.ActiveProfile : 0;
        }

        void RefreshConfig()
        {
            if (gridCfg == null) return;
            Theme th = Theme.Current;
            gridCfg.Rows.Clear();
            rtbCfg.Clear();
            cfgOutput = null;
            btnCfgSave.Enabled = btnCfgCopy.Enabled = false;
            if (config == null)
            {
                lblCfgFile.Text = "Aucune config ouverte";
                SetCfgInfo("Dans Betaflight Configurator, onglet CLI : tape « diff all », copie le résultat dans un fichier .txt (ou « Enregistrer dans un fichier »), puis ouvre-le ici ou glisse-le sur la fenêtre.", th.SubText);
                return;
            }
            lblCfgFile.Text = Path.GetFileName(config.Path) + (config.CraftName.Length > 0 ? "  ·  " + config.CraftName : "");
            if (lastRec == null || !lastRec.Current.Valid)
            {
                SetCfgInfo("Ouvre un log .bbl et sélectionne un vol : les PID proposés pour ce vol seront écrits dans la config.", th.Warn);
                rtbCfg.Text = string.Join("\n", config.Lines);
                return;
            }
            int prof = SelectedCfgProfile();
            int compared;
            bool match = config.MatchesFlight(prof, lastRec.Current, out compared) && compared >= 3;
            cfgMismatch = !match;
            FlightLog refLog = lastCombined != null && lastCombined.Logs.Count > 0 ? lastCombined.Logs[lastCombined.Logs.Count - 1] : null;
            string flight = refLog != null ? "vol #" + refLog.Index : "vol analysé";
            string note = "BBL Tuner " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " : PID du profil " + (prof + 1) + " ajustés d'après le " + flight;

            List<ConfigChange> changes;
            HashSet<int> changedLines;
            cfgOutput = config.Apply(prof, lastRec.Current, lastRec.Proposed, !match, note, out changes, out changedLines);

            string active = prof == config.ActiveProfile ? " (profil actif de la config)" : " (attention : ce n'est pas le profil actif de la config, qui est le profil " + (config.ActiveProfile + 1) + ")";
            if (match)
                SetCfgInfo("Profil " + (prof + 1) + active + " : ses PID correspondent au " + flight + ". " +
                    (changes.Count > 0 ? changes.Count + " réglage(s) modifié(s), surlignés dans l'aperçu." : "Aucun changement nécessaire."), th.Good);
            else
                SetCfgInfo("Profil " + (prof + 1) + active + " : ses PID ne correspondent PAS au " + flight +
                    ". Les valeurs proposées y seront quand même écrites. Vérifie que c'est le bon profil.", th.Warn);

            foreach (ConfigChange c in changes)
            {
                int r = gridCfg.Rows.Add(c.Key, c.Added ? "(défaut)" : c.Old, c.New);
                gridCfg.Rows[r].Cells[2].Style.ForeColor = th.Good;
                gridCfg.Rows[r].Cells[2].Style.BackColor = Theme.Blend(th.Card, th.Good, 0.14);
                if (c.Added) gridCfg.Rows[r].Cells[1].Style.ForeColor = th.SubText;
            }
            gridCfg.ClearSelection();

            // aperçu du fichier corrigé, lignes modifiées surlignées
            rtbCfg.Text = string.Join("\n", cfgOutput);
            Color hl = Color.FromArgb(40, 90, 55), hlFore = Color.FromArgb(170, 240, 190);
            int firstChanged = int.MaxValue;
            foreach (int i in changedLines)
            {
                if (i < 0 || i >= cfgOutput.Count) continue;
                int st = rtbCfg.GetFirstCharIndexFromLine(i);
                if (st < 0) continue;
                rtbCfg.Select(st, cfgOutput[i].Length);
                rtbCfg.SelectionBackColor = hl;
                rtbCfg.SelectionColor = hlFore;
                firstChanged = Math.Min(firstChanged, i);
            }
            if (firstChanged != int.MaxValue)
            {
                int idx = rtbCfg.GetFirstCharIndexFromLine(Math.Max(0, firstChanged - 3));
                rtbCfg.Select(Math.Max(0, idx), 0);
                rtbCfg.ScrollToCaret();
            }
            btnCfgSave.Enabled = btnCfgCopy.Enabled = changes.Count > 0;
        }

        bool ConfirmMismatch()
        {
            if (!cfgMismatch) return true;
            return AskYesNo("Les PID de ce profil ne correspondent pas au vol analysé : ce vol a peut-être été fait avec un autre profil ou une autre config.\n\nÉcrire quand même les valeurs proposées dans ce profil ?");
        }

        void SetCfgInfo(string text, Color c) { lblCfgInfo.Text = text; lblCfgInfo.ForeColor = c; }

        void SaveConfig()
        {
            if (config == null || cfgOutput == null || !ConfirmMismatch()) return;
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "Config Betaflight (*.txt)|*.txt";
                dlg.InitialDirectory = Path.GetDirectoryName(config.Path);
                dlg.FileName = Path.GetFileNameWithoutExtension(config.Path) + "_corrige.txt";
                dlg.Title = "Enregistrer la config corrigée";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(config.Path), StringComparison.OrdinalIgnoreCase))
                {
                    ShowMessage("Choisis un autre nom : le fichier d'origine est conservé tel quel, pour pouvoir revenir en arrière.", false);
                    return;
                }
                File.WriteAllText(dlg.FileName, string.Join("\r\n", cfgOutput) + "\r\n", new UTF8Encoding(false));
                lblStatus.Text = "Config corrigée enregistrée : " + dlg.FileName + "  →  à coller dans l'onglet CLI de Betaflight Configurator.";
            }
        }
    }
}
