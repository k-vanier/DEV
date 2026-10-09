param($exe, $arg, $outDir)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class K {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static List<IntPtr> Find(IntPtr parent, string part) {
    var res = new List<IntPtr>();
    EnumChildWindows(parent, (h, l) => { var sb = new StringBuilder(256); GetClassName(h, sb, 256); if (sb.ToString().Contains(part)) res.Add(h); return true; }, IntPtr.Zero);
    return res;
  }
}
"@
$p = Start-Process $exe -ArgumentList "`"$arg`"" -PassThru
Start-Sleep 15; $p.Refresh(); $h = $p.MainWindowHandle
function Snap($name) {
  $r = New-Object K+RECT; [K]::GetWindowRect($h, [ref]$r) | Out-Null
  $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
  $g = [Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
  [K]::PrintWindow($h, $dc, 2) | Out-Null
  $g.ReleaseHdc($dc); $g.Dispose()
  $bmp.Save((Join-Path $outDir "$name.png")); $bmp.Dispose()
}
function Key($lv, $vk) { [K]::PostMessage($lv, 0x100, [IntPtr]$vk, [IntPtr]0) | Out-Null; [K]::PostMessage($lv, 0x101, [IntPtr]$vk, [IntPtr]0xC0000001) | Out-Null; Start-Sleep 2 }
$lvs = [K]::Find($h, 'SysListView32')
"listviews=" + $lvs.Count
$lv = $lvs[0]   # la liste des vols est créée en premier
Snap "k0_lancement"
Key $lv 0x24; Snap "k1_home_vol1"          # Début -> vol #1
Key $lv 0x20; Snap "k2_espace_decoche"     # Espace -> décoche #1
Key $lv 0x23; Snap "k3_fin_vol3"           # Fin -> vol #3
$p.Kill()
