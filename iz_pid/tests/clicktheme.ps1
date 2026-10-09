param($exe, $arg, $outDir)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class B {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public static IntPtr FindVisible(IntPtr parent, string text) {
    IntPtr found = IntPtr.Zero;
    EnumChildWindows(parent, (h, l) => { var sb = new StringBuilder(256); GetWindowText(h, sb, 256); if (sb.ToString() == text && IsWindowVisible(h)) { found = h; return false; } return true; }, IntPtr.Zero);
    return found;
  }
}
"@
$p = Start-Process $exe -ArgumentList "`"$arg`"","--tab=2" -PassThru
Start-Sleep 15; $p.Refresh(); $h = $p.MainWindowHandle
function Snap($name) {
  $r = New-Object B+RECT; [B]::GetWindowRect($h, [ref]$r) | Out-Null
  $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
  $g = [Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
  [B]::PrintWindow($h, $dc, 2) | Out-Null
  $g.ReleaseHdc($dc); $g.Dispose()
  $bmp.Save((Join-Path $outDir "$name.png")); $bmp.Dispose()
}
function Click($text) { $b = [B]::FindVisible($h, $text); "clic '$text' -> $b"; if ($b -ne [IntPtr]::Zero) { [B]::SendMessage($b, 0xF5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }; Start-Sleep 2 }
Click "Mode sombre"
Click "▶"
Click "▶"
Click "Termes PID"
Click "Pitch"
Snap "runtime_dark"
Click "Mode clair"
Snap "runtime_light"
$p.Kill()
