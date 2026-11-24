param(
    [Parameter(Mandatory=$true)][string]$ItemsPath,
    [Parameter(Mandatory=$true)][string]$SourceRoot,
    [Parameter(Mandatory=$true)][string]$GameDir,
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [switch]$SelfTest
)
$ErrorActionPreference='Stop'
$sourcePrefix=[System.IO.Path]::GetFullPath($SourceRoot).TrimEnd('\','/')+[System.IO.Path]::DirectorySeparatorChar
$gameRoot=[System.IO.Path]::GetFullPath($GameDir)
$helperHash='676A48514D13F5490463A3DA3DB3024F92AAC56A6AFA6CFC71D9E1B2C30993F1'
$nativeNames=@('Assembly-CSharp','UnityEngine','Newtonsoft.Json','UniTask','RuntimeTransformHandle','Mirage','Unity.Mathematics','UnityEngine.CoreModule','UnityEngine.PhysicsModule','UnityEngine.InputLegacyModule','UnityEngine.IMGUIModule','UnityEngine.AudioModule','UnityEngine.ParticleSystemModule','UnityEngine.JSONSerializeModule','UnityEngine.UIModule','UnityEngine.UI','Unity.TextMeshPro','UnityEngine.TextRenderingModule','UnityEngine.ScreenCaptureModule','UnityEngine.ImageConversionModule')
$frameworkNames=@('mscorlib','System','System.Data','System.Drawing','System.Xml','System.Core','System.Runtime.Serialization','System.Xml.Linq','System.Numerics','System.IO.Compression','System.IO.Compression.FileSystem')
function Check-Inputs($items) {
    if(@($items.ProjectReference).Count -gt 0){throw 'External projects are not approved compiler inputs'}
    $resources=@($items.EmbeddedResource)
    $metadataPath=Join-Path $sourcePrefix 'ReplayMod.doorman.json'
    if($resources.Count -ne 1 -or $resources[0].Identity -ne 'ReplayMod.doorman.json' -or
        !$resources[0].FullPath -or [System.IO.Path]::GetFullPath($resources[0].FullPath) -ne $metadataPath -or
        $resources[0].LogicalName -ne 'doorman.json') {throw 'Only the local DOORMAN metadata resource is allowed'}
    $metadata=Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    if($metadata.plugin -ne 'com.chiester3105.replayMod'){throw 'DOORMAN metadata identifies the wrong plugin'}
    $packages=@($items.PackageReference)
    if($packages.Count -ne 1 -or $packages[0].Identity -ne 'BepInEx.AssemblyPublicizer.MSBuild' -or $packages[0].Version -ne '0.4.3' -or $packages[0].PrivateAssets -ne 'all'){throw 'Unreviewed compiler package dependency'}
    $sources=@();$helpers=0
    foreach($item in $items.Compile) {
        $path=[System.IO.Path]::GetFullPath($item.FullPath)
        if($path.StartsWith($sourcePrefix,[System.StringComparison]::OrdinalIgnoreCase)) {
            $relative=$path.Substring($sourcePrefix.Length).Replace('\','/')
            if($relative -match '(^|/)(bin|obj|vendor|sourcevendor|reference[^/]*)/' -or [System.IO.Path]::GetExtension($path) -ne '.cs'){throw 'Unapproved local compiler source location'}
            $sources+=@{path='src/'+$relative;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
        } else {
            if($item.NuGetPackageId -ne 'BepInEx.AssemblyPublicizer.MSBuild' -or $item.NuGetPackageVersion -ne '0.4.3' -or $path.Replace('\','/') -notmatch '/bepinex\.assemblypublicizer\.msbuild/0\.4\.3/contentFiles/cs/any/IgnoresAccessChecksToAttribute\.cs$' -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $helperHash){throw 'External compiler source is not the reviewed publicizer helper'}
            $helpers++
        }
    }
    if($helpers -ne 1 -or $sources.Count -eq 0){throw 'Compiler source set is incomplete'}
    foreach($item in $items.Reference) {
        if($frameworkNames -contains $item.Identity) {if($item.HintPath){throw 'Framework reference replaced by a file'};continue}
        if($item.Identity -in @('BepInEx','0Harmony')){$expected=Join-Path $gameRoot ('BepInEx/core/'+$item.Identity+'.dll')}
        elseif($nativeNames -contains $item.Identity){$expected=Join-Path $gameRoot ('NuclearOption_Data/Managed/'+$item.Identity+'.dll')}
        else{throw 'Unreviewed assembly reference'}
        if(!$item.HintPath -or [System.IO.Path]::GetFullPath($item.HintPath) -ne [System.IO.Path]::GetFullPath($expected) -or $item.Private -ne 'false'){throw 'Native reference location or distribution flag changed'}
    }
    return [ordered]@{schema=2;modLocalSourceCount=$sources.Count;disclosedHelperCount=$helpers;helper=@{package='BepInEx.AssemblyPublicizer.MSBuild';version='0.4.3';file='IgnoresAccessChecksToAttribute.cs';sha256=$helperHash};referenceNames=@($items.Reference.Identity);sourceFiles=@($sources | Sort-Object path);projectReferences=0;embeddedResources=1;resources=@(@{path='src/ReplayMod.doorman.json';logicalName='doorman.json';sha256=(Get-FileHash -LiteralPath $metadataPath -Algorithm SHA256).Hash});violations=0}
}
$raw=Get-Content -LiteralPath $ItemsPath -Raw
$items=($raw | ConvertFrom-Json).Items
$result=Check-Inputs $items
# Bind the project/policy and complete local source closure as well as evaluated
# Compile items. Added files or changed imports cannot silently enter packaging.
$modRoot=Split-Path -Parent $sourcePrefix.TrimEnd('\','/')
$projectPath=Join-Path $modRoot 'src/ReplayMod.csproj'
[xml]$projectXml=Get-Content -LiteralPath $projectPath -Raw
if(@($projectXml.SelectNodes('//*[local-name()="Import"]')).Count -ne 0){throw 'Explicit project imports require a reviewed build-input policy'}
$ancestor=New-Object System.IO.DirectoryInfo ($sourcePrefix.TrimEnd('\','/'))
while($null -ne $ancestor){
    foreach($overrideName in @('Directory.Build.props','Directory.Build.targets','Directory.Packages.props')){
        if(Test-Path -LiteralPath (Join-Path $ancestor.FullName $overrideName)){throw 'Ancestor build overrides require a reviewed build-input policy'}
    }
    $ancestor=$ancestor.Parent
}
$result.buildFiles=@(@('src/ReplayMod.csproj','build-local.ps1','tools/Audit-BuildInputs.ps1','THIRD-PARTY-NOTICES.txt') | ForEach-Object {@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $modRoot $_) -Algorithm SHA256).Hash}})
$result.allLocalSourceFiles=@(Get-ChildItem -LiteralPath $sourcePrefix -Recurse -File | Where-Object {
    $_.Extension -in @('.cs','.csproj') -and $_.FullName -notmatch '[\\/](bin|obj|vendor|sourcevendor)[\\/]'
} | Sort-Object FullName | ForEach-Object {@{path='src/'+$_.FullName.Substring($sourcePrefix.Length).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
$result.evidence='Evaluated project inputs plus local source/project closure; not a trace of arbitrary target-injected compiler items.'
if($SelfTest) {
    $cases=0
    foreach($mutation in @('external','embedded','missing-resource','resource-name','resource-path','project','package','reference')) {
        $copy=($raw | ConvertFrom-Json).Items
        switch($mutation) {
            external {$copy.Compile+=@{FullPath=(Join-Path (Split-Path -Parent $sourcePrefix.TrimEnd('\','/')) 'audit/reference-NOReplay/stolen.cs')}}
            embedded {$copy.EmbeddedResource+=@(@{Identity='external-code'})}
            'missing-resource' {$copy.EmbeddedResource=@()}
            'resource-name' {$copy.EmbeddedResource[0].LogicalName='hidden-code'}
            'resource-path' {$copy.EmbeddedResource[0].FullPath=Join-Path $modRoot 'unreviewed/ReplayMod.doorman.json'}
            project {$copy | Add-Member -NotePropertyName ProjectReference -NotePropertyValue @(@{Identity='external-project'}) -Force}
            package {$copy.PackageReference[0].Version='0.0.0'}
            reference {$copy.Reference+=@{Identity='AnotherReplayMod';HintPath='external.dll';Private='false'}}
        }
        $rejected=$false
        try{$null=Check-Inputs $copy}catch{$rejected=$true}
        if(!$rejected){throw ('Build-input rejection check failed: '+$mutation)}
        $cases++
    }
    Write-Output ('Source input rejection checks: '+$cases+' passed')
}
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($OutputPath),($result | ConvertTo-Json -Depth 6))
Write-Output ('Source input audit: '+$result.modLocalSourceCount+' mod-local sources, 1 disclosed helper, 1 DOORMAN resource, 0 violations')
