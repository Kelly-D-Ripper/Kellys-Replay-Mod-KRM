# Builds the player and source archives from the current Release build and release-docs.
# Run build-local.ps1 first. Existing archives in the output folder are kept with -Backup.
param(
    [string]$OutputDir=(Join-Path (Split-Path -Parent $PSScriptRoot) 'READY-TO-RELEASE'),
    [string]$Backup
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
$root=$PSScriptRoot
$dll=Join-Path $root 'src/bin/Release/net472/ReplayMod.dll'
$docs=Join-Path $root 'release-docs'
if(!(Test-Path -LiteralPath $dll)){throw 'Release DLL missing; run build-local.ps1 first'}
foreach($doc in 'README.md','RELEASE-NOTES.md','VALIDATION.md','THIRD-PARTY-NOTICES.txt'){if(!(Test-Path -LiteralPath (Join-Path $docs $doc))){throw "release-docs/$doc missing"}}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$player=Join-Path $OutputDir 'Kellys-Replay-Mod-1.0.zip'
$source=Join-Path $OutputDir 'Kellys-Replay-Mod-1.0-source.zip'
$loose=Join-Path $OutputDir 'Kellysreplaymod.dll'
if($Backup){foreach($file in $player,$source,$loose){if(Test-Path -LiteralPath $file){
    $target=[IO.Path]::Combine([IO.Path]::GetDirectoryName($file),[IO.Path]::GetFileNameWithoutExtension($file)+'-'+$Backup+[IO.Path]::GetExtension($file))
    if(Test-Path -LiteralPath $target){throw "Backup already exists: $target"}
    Copy-Item -LiteralPath $file -Destination $target}}}
function Write-Archive([string]$path,[object[]]$entries)
{
    $temp=$path+'.tmp';if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Force}
    $zip=[IO.Compression.ZipFile]::Open($temp,'Create')
    try{foreach($entry in $entries){[void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$entry.Source,$entry.Name,'Optimal')}}
    finally{$zip.Dispose()}
    Move-Item -LiteralPath $temp -Destination $path -Force
}
Write-Archive $player @(
    @{Source=$dll;Name='BepInEx/plugins/ReplayMod/Kellysreplaymod.dll'},
    @{Source=(Join-Path $docs 'README.md');Name='README.md'},
    @{Source=(Join-Path $docs 'RELEASE-NOTES.md');Name='RELEASE-NOTES.md'})
$entries=@()
foreach($doc in 'README.md','RELEASE-NOTES.md','THIRD-PARTY-NOTICES.txt','VALIDATION.md'){$entries+=@{Source=(Join-Path $docs $doc);Name=$doc}}
$entries+=@{Source=(Join-Path $root 'build-local.ps1');Name='build-local.ps1'}
foreach($folder in 'probe','src','tests','tools')
{
    Get-ChildItem -LiteralPath (Join-Path $root $folder) -Recurse -File |
        Where-Object {$_.Extension -in '.cs','.csproj','.json','.ps1' -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]'} |
        Sort-Object FullName | ForEach-Object {$entries+=@{Source=$_.FullName;Name=$_.FullName.Substring($root.Length+1).Replace('\','/')}}
}
Write-Archive $source $entries
Copy-Item -LiteralPath $dll -Destination $loose -Force
foreach($file in $player,$source,$loose){'{0}  {1}' -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash,(Split-Path -Leaf $file)}
"Source entries: $($entries.Count)"
