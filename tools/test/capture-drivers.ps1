# Abre la página de drivers de la compilación de prueba, pulsa «Buscar» por UI Automation y captura la ventana.
param([string]$Exe, [string]$OutDir, [string]$ButtonName = 'Buscar actualizaciones', [int]$SearchWait = 120)
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class CapWin2 { [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
  public static bool GetWindowRect(IntPtr h, out RECT r) { return DwmGetWindowAttribute(h, 9, out r, 16) == 0; }
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v); }
"@
[void][CapWin2]::SetProcessDpiAwarenessContext([IntPtr](-4))
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Shot($h, $name) {
    [void][CapWin2]::SetForegroundWindow($h); Start-Sleep -Milliseconds 400
    $r = New-Object CapWin2+RECT; [void][CapWin2]::GetWindowRect($h, [ref]$r)
    $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path $OutDir "$name.png"), [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
}

$p = Start-Process -FilePath $Exe -ArgumentList '--page=drivers' -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 50 -and $h -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 200; $p.Refresh(); $h = $p.MainWindowHandle }
Start-Sleep -Seconds 9   # escaneo inicial
Shot $h 'drivers-before'

$root = [Windows.Automation.AutomationElement]::FromHandle($h)
$cond = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $ButtonName)
$btn = $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
if (-not $btn) {
    # El contenido del botón es un StackPanel: busca el texto y sube al botón
    $txt = $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
}
$buttons = $root.FindAll([Windows.Automation.TreeScope]::Descendants, (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Button)))
$target = $null
foreach ($b in $buttons) {
    $inner = $b.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
    if ($inner -or $b.Current.Name -eq $ButtonName) { $target = $b; break }
}
if (-not $target) { "No encontré el botón '$ButtonName'"; $p.Kill(); exit 1 }
$sw = [Diagnostics.Stopwatch]::StartNew()
([Windows.Automation.InvokePattern]$target.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
Start-Sleep -Seconds 3
Shot $h 'drivers-searching'
# Espera a que el botón vuelva a estar activo (fin de la búsqueda)
while ($sw.Elapsed.TotalSeconds -lt $SearchWait) {
    Start-Sleep -Seconds 2
    if ($target.Current.IsEnabled) { break }
}
"Búsqueda: {0:N0} s" -f $sw.Elapsed.TotalSeconds
Start-Sleep -Seconds 2
Shot $h 'drivers-after'
try { $p.Kill(); [void]$p.WaitForExit(5000) } catch {}
