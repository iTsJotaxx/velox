# Prueba del actualizador real: abre una copia antigua de Velox, pulsa «Actualizar» y comprueba que se reemplaza
# por la última release de GitHub y se reinicia. Debe ejecutarse elevado (así la versión nueva hereda los permisos).
param([string]$BuiltExe, [string]$OutFile, [string]$ExpectVersion = '1.1.0', [string]$ButtonText = 'Actualizar a la v1.1.0')
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
function W([string]$m) { Add-Content -LiteralPath $OutFile -Value ("{0:HH:mm:ss}  {1}" -f (Get-Date), $m) -Encoding UTF8 }
Remove-Item $OutFile -ErrorAction SilentlyContinue
$dir = Join-Path $env:TEMP 'velox-updtest'
try {
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $exe = Join-Path $dir 'Velox.exe'
    Copy-Item $BuiltExe $exe
    W ("Copia de prueba: " + [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion + " en $dir")

    $p = Start-Process $exe -ArgumentList '--page=settings' -PassThru
    $h = [IntPtr]::Zero
    for ($i = 0; $i -lt 50 -and $h -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 200; $p.Refresh(); $h = $p.MainWindowHandle }
    $root = [Windows.Automation.AutomationElement]::FromHandle($h)
    $txtType = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Text)
    $btnType = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Button)

    function Find-Button([string]$name) {
        foreach ($b in $root.FindAll([Windows.Automation.TreeScope]::Descendants, $btnType)) { if ($b.Current.Name -eq $name) { return $b } }
        return $null
    }

    # 1) El estado de la comprobación aparece solo al abrir Ajustes
    $status = $null
    for ($i = 0; $i -lt 40 -and -not $status; $i++) {
        Start-Sleep -Milliseconds 750
        foreach ($t in $root.FindAll([Windows.Automation.TreeScope]::Descendants, $txtType)) {
            $n = $t.Current.Name
            if ($n -like 'Hay una versión nueva*' -or $n -like 'Tienes la última*' -or $n -like 'No se pudo comprobar*') { $status = $n; break }
        }
    }
    W "Estado en Ajustes: $status"
    $btn = Find-Button $ButtonText
    if (-not $btn) { W "FALLO: no aparece el botón '$ButtonText'"; return }
    W "Botón encontrado: '$ButtonText'. Pulsando..."
    ([Windows.Automation.InvokePattern]$btn.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()

    # 2) Diálogo de confirmación
    $ok = $null
    for ($i = 0; $i -lt 20 -and -not $ok; $i++) { Start-Sleep -Milliseconds 500; $ok = Find-Button 'Actualizar' }
    if (-not $ok) { W "FALLO: no apareció el diálogo de confirmación"; return }
    W "Diálogo de confirmación visible. Aceptando..."
    ([Windows.Automation.InvokePattern]$ok.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()

    # 3) La copia antigua se cierra y arranca la nueva desde el mismo archivo
    $oldPid = $p.Id
    $newProc = $null
    for ($i = 0; $i -lt 90 -and -not $newProc; $i++) {
        Start-Sleep -Seconds 1
        $newProc = Get-Process Velox -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$dir\*" -and $_.Id -ne $oldPid -and $_.StartTime -gt $p.StartTime } | Select-Object -First 1
    }
    W ("Proceso antiguo ({0}) sigue vivo: {1}" -f $oldPid, [bool](Get-Process -Id $oldPid -ErrorAction SilentlyContinue))
    if ($newProc) { W ("Proceso nuevo: PID {0}" -f $newProc.Id) } else { W "FALLO: no arrancó la versión nueva" }
    Start-Sleep -Seconds 8
    $v = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
    W "Versión del archivo Velox.exe ahora: $v (esperada $ExpectVersion.x)"
    W ("Archivos en la carpeta: " + ((Get-ChildItem $dir | ForEach-Object { "$($_.Name) [$([int]($_.Length/1KB)) KB]" }) -join ', '))
    W ("Proceso nuevo responde: " + [bool](Get-Process Velox -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$dir\*" -and $_.Responding }))
    W "--- Registro de Velox"
    Get-Content "$env:ProgramData\Velox\velox.log" -Encoding UTF8 -Tail 14 | Where-Object { $_ -match 'pdate|=== Velox' } | ForEach-Object { W $_ }
    W ("RESULTADO: " + $(if ($v -like "$ExpectVersion*" -and $newProc) { 'OK' } else { 'FALLO' }))
} catch { W "EXCEPCION: $($_.Exception.Message)" }
finally {
    Get-Process Velox -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$dir\*" } | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    W "DONE"
}
