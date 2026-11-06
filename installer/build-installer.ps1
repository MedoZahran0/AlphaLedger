param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\Release'),
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$project = Join-Path $repositoryRoot 'src\AlphaLedger.App\AlphaLedger.App.csproj'
$versionNode = (Select-Xml -Path $project -XPath '/Project/PropertyGroup/Version' | Select-Object -First 1).Node
$appVersion = if ($null -ne $versionNode) { $versionNode.InnerText.Trim() } else { '' }
$compiler = (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue).Source

if ([string]::IsNullOrWhiteSpace($appVersion)) {
    throw 'Set the application Version in AlphaLedger.App.csproj before building a release installer.'
}

if ([string]::IsNullOrWhiteSpace($compiler)) {
    $compilerCandidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    $compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($compiler) -or -not (Test-Path -LiteralPath $compiler)) {
    throw 'Install Inno Setup 6 to build the standard Windows installer.'
}

$stage = Join-Path $env:TEMP ('AlphaLedgerPublish-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    & dotnet publish $project `
        --configuration $Configuration `
        --runtime $RuntimeIdentifier `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        --output $stage

    if ($LASTEXITCODE -ne 0) {
        throw 'Self-contained application publish failed.'
    }

    $app = Join-Path $stage 'AlphaLedger.App.exe'
    if (-not (Test-Path -LiteralPath $app)) {
        throw 'The self-contained application executable was not produced.'
    }

    New-Item -ItemType Directory -Force -Path $output | Out-Null
    Copy-Item -LiteralPath $app -Destination (Join-Path $output 'AlphaLedger-Update.exe') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AlphaLedger-Patcher.ps1') -Destination (Join-Path $output 'AlphaLedger-Patcher.ps1') -Force
    & $compiler "/DAppSource=$app" "/DAppVersion=$appVersion" "/O$output" '/FAlphaLedger-Setup' (Join-Path $PSScriptRoot 'AlphaLedger.iss')
    if ($LASTEXITCODE -ne 0) {
        throw 'Inno Setup could not create AlphaLedger-Setup.exe.'
    }

    Get-Item -LiteralPath (Join-Path $output 'AlphaLedger-Setup.exe') |
        Select-Object FullName, Length, LastWriteTime
}
finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
