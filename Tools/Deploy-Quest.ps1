param(
    [Parameter(Mandatory=$true)][string]$Adb,
    [string]$Serial,
    [string]$ModelFolder,
    [switch]$SkipInstall
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$adbArgs=@()
if ($Serial) { $adbArgs+=@('-s',$Serial) }
function Invoke-Adb {
    param([string[]]$Arguments)
    & $Adb @adbArgs @Arguments
    if ($LASTEXITCODE -ne 0) { throw "ADB failed: $Arguments" }
}
if (-not $SkipInstall) {
    $apk=Join-Path $projectRoot 'Builds/LabWalk-Quest3S.apk'
    if (-not (Test-Path -LiteralPath $apk)) { throw 'Build the Quest APK first.' }
    Invoke-Adb @('install','-r',$apk)
}
if ($ModelFolder) {
    $folder=(Resolve-Path -LiteralPath $ModelFolder).Path
    $manifest=Get-Content -LiteralPath (Join-Path $folder 'model.json') -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($manifest.file) -or [IO.Path]::GetFileName($manifest.file) -ne $manifest.file -or $manifest.file -match '[/\\:]' -or $manifest.file -notmatch '\.(glb|3dm)$') { throw 'Invalid model filename in manifest; use .glb or .3dm.' }
    $modelPath=Join-Path $folder $manifest.file
    if (-not (Test-Path -LiteralPath $modelPath)) { throw 'Model listed in model.json was not found.' }
    $remote='/sdcard/Android/data/com.architecturelab.labwalk/files/Models'
    Invoke-Adb @('shell','am','force-stop','com.architecturelab.labwalk')
    Invoke-Adb @('shell','mkdir','-p',$remote)
    Invoke-Adb @('push',$modelPath,"$remote/$($manifest.file)")
    Invoke-Adb @('push',(Join-Path $folder 'model.json'),"$remote/model.json")
}
Invoke-Adb @('shell','monkey','-p','com.architecturelab.labwalk','-c','android.intent.category.LAUNCHER','1')
