param([string]$Version='1.0.0')
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not(Test-Path -LiteralPath $compiler)){$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$build=Join-Path $root 'build'
$release=Join-Path $root 'release'
New-Item -ItemType Directory -Path $build,$release -Force | Out-Null
$zip=Join-Path $build 'payload.zip'
if(Test-Path -LiteralPath $zip){Remove-Item -LiteralPath $zip}
[System.IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $root 'data'),$zip,[System.IO.Compression.CompressionLevel]::Optimal,$false)
$exe=Join-Path $release "Mafia-II-Azerbaijani-Patch-Setup-$Version.exe"
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/out:$exe" "/win32manifest:$(Join-Path $root 'src\app.manifest')" "/resource:$zip,MafiaAz.Payload" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll (Join-Path $root 'src\PatchEngine.cs') (Join-Path $root 'src\Program.cs')
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed.'}
$hash=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([System.IO.Path]::GetFileName($exe))" | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Built $exe"
Get-Item -LiteralPath $exe | Select-Object Name,Length
