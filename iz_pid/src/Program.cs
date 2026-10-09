using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BblTuner
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        /// Journal d'erreurs à côté de l'exécutable (BBLTuner_erreurs.log).
        public static void LogError(string what, Exception ex)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BBLTuner_erreurs.log");
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + what + Environment.NewLine +
                    (ex != null ? ex.ToString() + Environment.NewLine : "") + Environment.NewLine);
            }
            catch { }
        }

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                LogError("Erreur", e.Exception);
                MessageBox.Show("Erreur inattendue : " + e.Exception.Message + "\n\nDétails dans BBLTuner_erreurs.log", "BBL Tuner",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogError("Erreur fatale", e.ExceptionObject as Exception);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            MainForm f = new MainForm();
            foreach (string a in args)
            {
                if (a.StartsWith("--tab=")) { int t; if (int.TryParse(a.Substring(6), out t)) f.InitialTab = t; }
                else f.InitialFiles.Add(a);
            }
            if (Environment.GetEnvironmentVariable("BBLTUNER_DEBUG") == "1")
                f.FormClosing += (s, e) => LogError("Fermeture : " + e.CloseReason, null);
            Application.Run(f);
        }
    }
}
