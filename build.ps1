# Compila Velox en Release y deja el ejecutable en dist\Velox.exe
# Uso: .\build.ps1 [-Version 1.0.0]
param([string]$Version)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$props = @('-c', 'Release', '-nologo', '-v', 'minimal')
if ($Version) { $props += "-p:Version=$Version" }

dotnet build src\Velox\Velox.csproj @props
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

New-Item -ItemType Directory -Force -Path dist | Out-Null
Copy-Item src\Velox\bin\Release\net48\Velox.exe dist\Velox.exe -Force
$exe = Get-Item dist\Velox.exe
$hash = (Get-FileHash $exe.FullName -Algorithm SHA256).Hash
"$hash  Velox.exe" | Set-Content dist\Velox.exe.sha256 -Encoding ASCII
"Built {0} ({1:N0} KB)  SHA256 {2}" -f $exe.FullName, ($exe.Length / 1KB), $hash
