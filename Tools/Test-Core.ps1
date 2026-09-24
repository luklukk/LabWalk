$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Add-Type -Path @((Join-Path $projectRoot 'Assets/LabWalk/Core/AlignmentMath.cs'),(Join-Path $PSScriptRoot 'CoreTests.cs'))
$results=[CoreTests]::Run()
$results | ForEach-Object { Write-Output "PASS: $_" }
Write-Output "$($results.Count) test groups passed. These test pure math, not Unity or headset behavior."
