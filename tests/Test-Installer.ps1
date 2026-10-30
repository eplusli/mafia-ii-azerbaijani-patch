param(
    [Parameter(Mandatory=$true)][string]$OriginalFiles,
    [Parameter(Mandatory=$true)][string]$TestWorkspace
)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$exe=(Resolve-Path (Join-Path $project 'release\Mafia-II-Azerbaijani-Patch-Setup-1.0.0.exe')).Path
$manifest=Get-Content -LiteralPath (Join-Path $project 'data\manifest.json') -Raw | ConvertFrom-Json
$source=(Resolve-Path -LiteralPath $OriginalFiles).Path
$workspace=[IO.Path]::GetFullPath($TestWorkspace)
$fixture=Join-Path $workspace ('Fixture-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$checks=New-Object 'System.Collections.Generic.List[string]'
function Assert([bool]$condition,[string]$message){if(-not $condition){throw $message}}
function Hash([string]$file){(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()}
function Run([string]$action,[bool]$success=$true,[string]$selection=$fixture){
    $report=Join-Path $workspace ('result-'+[Guid]::NewGuid().ToString('N')+'.json')
    $process=Start-Process -FilePath $exe -ArgumentList @($action,('"'+$selection+'"'),'--report',('"'+$report+'"')) -Wait -PassThru -WindowStyle Hidden
    Assert (Test-Path -LiteralPath $report) 'The installer did not write its test result.'
    $result=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    Assert ($result.ok -eq $success) ("Unexpected result for $action : "+$result.error)
    Assert (($process.ExitCode -eq 0) -eq $success) 'Unexpected installer process exit code.'
    return $result
}
function CheckFiles([string]$state,[string]$except=''){
    foreach($entry in $manifest.files){if($entry.path -eq $except){continue};$expected=if($state -eq 'original'){$entry.originalSha256}else{$entry.installedSha256};Assert ((Hash (Join-Path $fixture $entry.path)) -eq $expected) ("Unexpected file content: "+$entry.path)}
}
foreach($entry in $manifest.files){
    $original=Join-Path $source $entry.path
    Assert ((Hash $original) -eq $entry.originalSha256) ('Original test input differs: '+$entry.path)
    $target=Join-Path $fixture $entry.path
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $original -Destination $target
}
[IO.File]::WriteAllText((Join-Path $fixture 'pc\mafia2.exe'),'test fixture; never executed')
$first=$manifest.files[0]
$firstPath=Join-Path $fixture $first.path
[IO.File]::AppendAllText($firstPath,'modified')
$null=Run '--install' $false
CheckFiles 'original' $first.path
Assert (-not(Test-Path -LiteralPath (Join-Path $fixture 'MafiaII.Azerbaijani.Backup'))) 'An incompatible install wrote backups before completing preflight.'
Copy-Item -LiteralPath (Join-Path $source $first.path) -Destination $firstPath -Force
$checks.Add('Unsupported or modified archives rejected before game changes')
Move-Item -LiteralPath $firstPath -Destination ($firstPath+'.missing')
$null=Run '--install' $false
Move-Item -LiteralPath ($firstPath+'.missing') -Destination $firstPath
CheckFiles 'original'
$checks.Add('Missing archives rejected before game changes')
$null=Run '--verify'
$null=Run '--verify' $true (Join-Path $fixture 'pc\mafia2.exe')
$null=Run '--verify' $true (Join-Path $fixture 'pc')
$checks.Add('Game root, pc folder and mafia2.exe selections resolve correctly')
$null=Run '--install'
CheckFiles 'patched'
$backupRoot=Join-Path $fixture 'MafiaII.Azerbaijani.Backup\1.0.0\original'
foreach($entry in $manifest.files){Assert ((Hash (Join-Path $backupRoot $entry.path)) -eq $entry.originalSha256) ('Backup differs: '+$entry.path)}
$checks.Add('All 304 archives installed and all original backups verified')
$null=Run '--install'
CheckFiles 'patched'
$checks.Add('Repeated installation is idempotent')
Copy-Item -LiteralPath (Join-Path $backupRoot $first.path) -Destination $firstPath -Force
$null=Run '--install'
CheckFiles 'patched'
$checks.Add('Interrupted-install mixture of original and patched files recovered')
$patchedFirst=[IO.File]::ReadAllBytes($firstPath)
[IO.File]::AppendAllText($firstPath,'later mod')
$null=Run '--restore' $false
CheckFiles 'patched' $first.path
[IO.File]::WriteAllBytes($firstPath,$patchedFirst)
$checks.Add('Restore refuses to overwrite a later mod')
$backupFirst=Join-Path $backupRoot $first.path
[IO.File]::AppendAllText($backupFirst,'corrupt')
$null=Run '--restore' $false
CheckFiles 'patched'
Copy-Item -LiteralPath (Join-Path $source $first.path) -Destination $backupFirst -Force
$checks.Add('Corrupted backups rejected before restore changes')
$null=Run '--restore'
CheckFiles 'original'
$checks.Add('All 304 originals restored byte for byte')
$summary=[pscustomobject]@{passed=$true;archives=$manifest.files.Count;checks=@($checks);fixture=$fixture}
$summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $workspace 'test-summary.json') -Encoding utf8
$checks | ForEach-Object {Write-Host ('PASS: '+$_)}
