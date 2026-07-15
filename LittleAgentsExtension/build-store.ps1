param(
    [string]$Configuration = "Release",
    [string]$Version = "0.1.1.0"
)

$ErrorActionPreference = "Stop"

$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectFile = Join-Path $ProjectDir "LittleAgentsExtension.csproj"
$PackageRoot = Join-Path $ProjectDir "AppPackages"

foreach ($Architecture in @("x64", "ARM64")) {
    $RuntimeIdentifier = if ($Architecture -eq "ARM64") { "win-arm64" } else { "win-x64" }

    dotnet build $ProjectFile `
        --configuration $Configuration `
        -p:Platform=$Architecture `
        -p:RuntimeIdentifier=$RuntimeIdentifier `
        -p:GenerateAppxPackageOnBuild=true `
        -p:AppxPackageDir="AppPackages\$Architecture\" `
        -p:AppxPackageVersion=$Version `
        -p:PublishSingleFile=false `
        -p:AppxPackageSigningEnabled=false

    if ($LASTEXITCODE -ne 0) {
        throw "$Architecture MSIX build failed with exit code $LASTEXITCODE."
    }
}

$X64Package = Get-ChildItem (Join-Path $PackageRoot "x64") -Recurse -Filter "*.msix" |
    Where-Object Name -notlike "*.msixbundle" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
$Arm64Package = Get-ChildItem (Join-Path $PackageRoot "ARM64") -Recurse -Filter "*.msix" |
    Where-Object Name -notlike "*.msixbundle" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $X64Package -or -not $Arm64Package) {
    throw "Both x64 and ARM64 MSIX packages are required to create the Store bundle."
}

$MakeAppx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if (-not $MakeAppx) {
    throw "makeappx.exe was not found. Install the Windows 10/11 SDK packaging tools."
}

foreach ($Package in @($X64Package, $Arm64Package)) {
    $InspectionDirectory = Join-Path $env:TEMP ("LittleAgentsExtension-" + [Guid]::NewGuid().ToString("N"))

    try {
        & $MakeAppx.FullName unpack /o /p $Package.FullName /d $InspectionDirectory | Out-Null

        if ($LASTEXITCODE -ne 0) {
            throw "Failed to inspect $($Package.FullName)."
        }

        foreach ($RequiredFile in @(
            "LittleAgentsExtension.exe",
            "LittleAgentsExtension.dll",
            "LittleAgentsExtension.deps.json",
            "LittleAgentsExtension.runtimeconfig.json"
        )) {
            if (-not (Test-Path -LiteralPath (Join-Path $InspectionDirectory $RequiredFile))) {
                throw "$($Package.Name) is missing required runtime file $RequiredFile."
            }
        }
    }
    finally {
        if (Test-Path -LiteralPath $InspectionDirectory) {
            Remove-Item -LiteralPath $InspectionDirectory -Recurse -Force
        }
    }
}

$MappingFile = Join-Path $ProjectDir "bundle_mapping.txt"
$BundleFile = Join-Path $ProjectDir "LittleAgentsExtension_${Version}_Bundle.msixbundle"

@(
    "[Files]"
    "`"$($X64Package.FullName)`" `"LittleAgentsExtension_${Version}_x64.msix`""
    "`"$($Arm64Package.FullName)`" `"LittleAgentsExtension_${Version}_arm64.msix`""
) | Set-Content -LiteralPath $MappingFile -Encoding utf8

& $MakeAppx.FullName bundle /v /o /f $MappingFile /p $BundleFile

if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx bundle failed with exit code $LASTEXITCODE."
}

Get-Item -LiteralPath $BundleFile
