# Builds the release zips into Deploy\Release.
#   .\build-release.ps1 -Version 2.0.0
#
# Windows zips are "portable": the files sit at the top of the zip, with no subfolder.
# Unzip one over the previous version's folder and the exe keeps the same path, so
# Windows Firewall keeps the "Allow" it already has and doesn't ask for admin again.
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string[]]$Runtimes = @("win-x64", "linux-x64")
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "Deploy\Release"
$tar = Join-Path $env:SystemRoot "System32\tar.exe"   # Windows bsdtar writes .zip with -a
New-Item -ItemType Directory -Force $out | Out-Null

$apps = @(
    @{ Project = "LogicReinc.BlendFarm";        Name = "BlendFarm" },
    @{ Project = "LogicReinc.BlendFarm.Server"; Name = "BlendFarm-Server" }
)

foreach ($rid in $Runtimes) {
    foreach ($app in $apps) {
        $stage = Join-Path $root "Deploy\_Stage\$($app.Name)-$rid"
        if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }

        dotnet publish (Join-Path $root $app.Project) -c Release -r $rid `
            -p:PublishSingleFile=true --self-contained true -o $stage
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($app.Project) $rid" }

        Get-ChildItem $stage -Filter *.pdb | Remove-Item -Force
        if ($rid -like "win-*" -and $app.Name -eq "BlendFarm-Server") {
            Copy-Item (Join-Path $root "packaging\windows\Allow BlendFarm through Firewall.bat") $stage
        }

        if ($rid -like "win-*") {
            # Portable: files at the top of the zip
            $zip = Join-Path $out "$($app.Name)-v$Version-$rid-portable.zip"
            if (Test-Path $zip) { Remove-Item -Force $zip }
            & $tar -a -c -f $zip -C $stage (Get-ChildItem $stage).Name
        }
        else {
            # Linux keeps one folder, so `unzip` doesn't spill into the current directory
            $folder = "$($app.Name)-v$Version-$rid"
            $parent = Split-Path $stage
            $named = Join-Path $parent $folder
            if (Test-Path $named) { Remove-Item -Recurse -Force $named }
            Rename-Item $stage $folder
            $zip = Join-Path $out "$folder.zip"
            if (Test-Path $zip) { Remove-Item -Force $zip }
            & $tar -a -c -f $zip -C $parent $folder
        }
        if ($LASTEXITCODE -ne 0) { throw "zip failed for $zip" }
        Write-Host "Built $zip"
    }
}
