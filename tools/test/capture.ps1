# Abre Velox (compilación de prueba sin elevación) en cada página y guarda una captura de la ventana.
param([string]$Exe, [string]$OutDir, [string[]]$Pages = @('summary'), [int]$Wait = 6, [string]$Lang)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class CapWin { [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
  public static bool GetWindowRect(IntPtr h, out RECT r) { return DwmGetWindowAttribute(h, 9, out r, 16) == 0; } // DWMWA_EXTENDED_FRAME_BOUNDS
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v); }
"@
[void][CapWin]::SetProcessDpiAwarenessContext([IntPtr](-4))
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
if ($Lang) {
    $settings = Join-Path $env:APPDATA 'Velox\settings.xml'
    New-Item -ItemType Directory -Force -Path (Split-Path $settings) | Out-Null
    Set-Content -LiteralPath $settings -Encoding UTF8 -Value "<Settings xmlns:i=`"http://www.w3.org/2001/XMLSchema-instance`"><CreateRestorePoint>true</CreateRestorePoint><ExpandAllCategories>false</ExpandAllCategories><Language>$Lang</Language></Settings>"
}
foreach ($page in $Pages) {
    $p = Start-Process -FilePath $Exe -ArgumentList "--page=$page" -PassThru
    $h = [IntPtr]::Zero
    for ($i = 0; $i -lt 50 -and $h -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 200; $p.Refresh(); $h = $p.MainWindowHandle }
    Start-Sleep -Seconds $Wait
    [void][CapWin]::SetForegroundWindow($h)
    Start-Sleep -Milliseconds 400
    $r = New-Object CapWin+RECT
    [void][CapWin]::GetWindowRect($h, [ref]$r)
    $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path $OutDir "$page.png"), [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    try { $p.Kill(); [void]$p.WaitForExit(5000) } catch {}
    Start-Sleep -Milliseconds 800
    "$page -> $($r.R - $r.L)x$($r.B - $r.T)"
}
