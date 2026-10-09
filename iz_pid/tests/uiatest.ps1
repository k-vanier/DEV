param([string]$exe, [string]$arg, [string]$outDir)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W3 {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
$p = Start-Process $exe -ArgumentList "`"$arg`"" -PassThru
Start-Sleep -Seconds 18
$p.Refresh(); $h = $p.MainWindowHandle
function Snap($name) {
  $r = New-Object W3+RECT; [W3]::GetWindowRect($h, [ref]$r) | Out-Null
  $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
  $g = [Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
  [W3]::PrintWindow($h, $dc, 2) | Out-Null
  $g.ReleaseHdc($dc); $g.Dispose()
  $bmp.Save((Join-Path $outDir "$name.png")); $bmp.Dispose()
}
$root = [Windows.Automation.AutomationElement]::FromHandle($h)
function Item($name) {
  $c = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $name)
  $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $c)
}
function State() {
  foreach ($n in '#1','#2','#3') {
    $it = Item $n
    $tp = $null
    $ok = $it.TryGetCurrentPattern([Windows.Automation.TogglePattern]::Pattern, [ref]$tp)
    $sel = $it.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected
    "$n coché=" + ($(if ($ok) { $tp.Current.ToggleState } else { '?' })) + " sélectionné=$sel"
  }
}
"--- au lancement"; State
"--- clic (sélection) sur #1"
(Item '#1').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 3; State
Snap "single_apres"
"--- case cochée sur #3"
$tp = $null; if ((Item '#3').TryGetCurrentPattern([Windows.Automation.TogglePattern]::Pattern, [ref]$tp)) { $tp.Toggle() } else { "pas de TogglePattern" }
Start-Sleep -Seconds 3; State
$p.Kill()
