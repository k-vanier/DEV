# Compile BBL Tuner avec le compilateur C# intégré à Windows (.NET Framework 4.x) - aucune installation requise.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
          (Join-Path $fw 'System.Windows.Forms.DataVisualization.dll')) | ForEach-Object { "/r:$_" }
$common = @(Join-Path $root 'src\Bbl.cs'), (Join-Path $root 'src\Dsp.cs'), (Join-Path $root 'src\Analysis.cs'), (Join-Path $root 'src\Config.cs')
New-Item -ItemType Directory -Force (Join-Path $root 'bin') | Out-Null

& $csc /nologo /target:winexe /optimize+ /codepage:65001 /out:"$root\bin\BBLTuner.exe" $refs $common `
    (Join-Path $root 'src\MainForm.cs') (Join-Path $root 'src\RespTab.cs') (Join-Path $root 'src\TraceTab.cs') (Join-Path $root 'src\Theme.cs') (Join-Path $root 'src\UiKit.cs') (Join-Path $root 'src\DarkUi.cs') (Join-Path $root 'src\ConfigTab.cs') (Join-Path $root 'src\Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Echec compilation application' }

& $csc /nologo /target:exe /optimize+ /codepage:65001 /out:"$root\bin\BblTests.exe" $refs $common (Join-Path $root 'tests\Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Echec compilation tests' }
Write-Host "OK -> $root\bin\BBLTuner.exe"
