param($exe, $arg)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$p = Start-Process $exe -ArgumentList "`"$arg`"" -PassThru
Start-Sleep 15; $p.Refresh()
$r = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
$c = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem)
$items = $r.FindAll([Windows.Automation.TreeScope]::Descendants, $c)
"nb=" + $items.Count
foreach ($it in $items | Select -First 5) { "name=[" + $it.Current.Name + "] patterns=" + (($it.GetSupportedPatterns() | % { $_.ProgrammaticName }) -join ',') }
$p.Kill()
