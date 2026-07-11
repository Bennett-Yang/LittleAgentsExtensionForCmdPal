param(
    [string]$Configuration = "Release",
    [string]$Version = "0.1.0",
    [string[]]$Platforms = @("x64", "arm64")
)

$ErrorActionPreference = "Stop"

$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectFile = Join-Path $ProjectDir "LittleAgentsExtension.csproj"
$TemplateFile = Join-Path $ProjectDir "setup-template.iss"
$InnoSetupCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
)
$InnoSetupPath = $InnoSetupCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $InnoSetupPath) {
    throw "Inno Setup 6 was not found. Install JRSoftware.InnoSetup and rerun this script."
}

if (-not (Test-Path -LiteralPath $TemplateFile)) {
    throw "Missing setup-template.iss next to build-exe.ps1."
}

dotnet restore $ProjectFile

foreach ($Platform in $Platforms) {
    $NormalizedPlatform = $Platform.ToLowerInvariant()
    $RuntimeIdentifier = "win-$NormalizedPlatform"
    $PublishDir = Join-Path $ProjectDir "bin\$Configuration\$RuntimeIdentifier\publish"

    if (Test-Path -LiteralPath $PublishDir) {
        Remove-Item -LiteralPath $PublishDir -Recurse -Force
    }

    dotnet publish $ProjectFile `
        --configuration $Configuration `
        --runtime $RuntimeIdentifier `
        --self-contained true `
        --output $PublishDir `
        -p:WindowsPackageType=None `
        -p:Version=$Version `
        -p:AssemblyVersion=$Version `
        -p:FileVersion=$Version

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $Platform."
    }

    @("build-exe.ps1", "setup-template.iss", "setup-x64.iss", "setup-arm64.iss") | ForEach-Object {
        $PublishedHelper = Join-Path $PublishDir $_
        if (Test-Path -LiteralPath $PublishedHelper) {
            Remove-Item -LiteralPath $PublishedHelper -Force
        }
    }

    $PublishedProperties = Join-Path $PublishDir "Properties"
    if (Test-Path -LiteralPath $PublishedProperties) {
        Remove-Item -LiteralPath $PublishedProperties -Recurse -Force
    }

    $SetupScript = Get-Content -LiteralPath $TemplateFile -Raw
    $SetupScript = $SetupScript -replace '#define AppVersion ".*"', "#define AppVersion `"$Version`""
    $SetupScript = $SetupScript -replace 'OutputBaseFilename=.*', "OutputBaseFilename=LittleAgentsExtension-Setup-{#AppVersion}-$NormalizedPlatform"
    $SetupScript = $SetupScript -replace 'Source: "bin\\Release\\win-x64\\publish\\\*"', "Source: `"bin\$Configuration\$RuntimeIdentifier\publish\*`""

    if ($NormalizedPlatform -eq "arm64") {
        $SetupScript = $SetupScript -replace 'ArchitecturesAllowed=x64compatible', 'ArchitecturesAllowed=arm64'
        $SetupScript = $SetupScript -replace 'ArchitecturesInstallIn64BitMode=x64compatible', 'ArchitecturesInstallIn64BitMode=arm64'
    }

    $SetupPath = Join-Path $ProjectDir "setup-$NormalizedPlatform.iss"
    Set-Content -LiteralPath $SetupPath -Value $SetupScript -Encoding UTF8

    & $InnoSetupPath $SetupPath

    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup failed for $Platform."
    }
}

Get-ChildItem -LiteralPath (Join-Path $ProjectDir "bin\$Configuration\installer") -Filter "*.exe" | ForEach-Object {
    $_.FullName
}
