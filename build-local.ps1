param(
    [string]$GameDir = $(if($env:NUCLEAR_OPTION_DIR){$env:NUCLEAR_OPTION_DIR}else{'C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option'}),
    [string]$DotnetExe,
    [string[]]$AuditReplayPaths = @()
)
$ErrorActionPreference='Stop'
if(!$DotnetExe){
    $DotnetExe=Join-Path (Split-Path -Parent $PSScriptRoot) 'build-tools/dotnet-sdk/dotnet.exe'
    if(!(Test-Path -LiteralPath $DotnetExe)){$DotnetExe=(Get-Command dotnet -ErrorAction Stop).Source}
}
$GameDir=[IO.Path]::GetFullPath($GameDir)
$project=Join-Path $PSScriptRoot 'src/ReplayMod.csproj'
$auditDir=Join-Path $PSScriptRoot 'audit'
New-Item -ItemType Directory -Path $auditDir -Force | Out-Null
& $DotnetExe build $project -c Release "-p:GameDir=$GameDir"
if($LASTEXITCODE -ne 0){throw 'Plugin build failed'}
$inputs=Join-Path $auditDir 'evaluated-build-inputs.json'
& $DotnetExe msbuild $project '-getItem:Compile,ProjectReference,EmbeddedResource,PackageReference,Reference' "-p:GameDir=$GameDir" | Set-Content -LiteralPath $inputs -Encoding UTF8
if($LASTEXITCODE -ne 0){throw 'Compiler input evaluation failed'}
& (Join-Path $PSScriptRoot 'tools/Audit-BuildInputs.ps1') -ItemsPath $inputs -SourceRoot (Join-Path $PSScriptRoot 'src') -GameDir $GameDir -OutputPath (Join-Path $auditDir 'source-inputs.json') -SelfTest
if(!$?){throw 'Compiler input audit failed'}
& $DotnetExe run --project (Join-Path $PSScriptRoot 'tests/Regression/Regression.csproj') -c Release -- @AuditReplayPaths
if($LASTEXITCODE -ne 0){throw 'Regression tests failed'}
$dll=Join-Path $PSScriptRoot 'src/bin/Release/net472/ReplayMod.dll'
Write-Output ('Built plugin SHA256: '+(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash)
Write-Output 'Build, source-input audit and regressions passed. Native runtime and visual acceptance are separate checks.'
