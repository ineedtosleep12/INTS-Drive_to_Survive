param([switch]$Test, [switch]$Package)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskBuild = Join-Path (Split-Path $taskRoot -Parent) 'build/live-coach'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw 'Windows x64 .NET Framework 4.x C# compiler not found.' }
$taskSpeechRoot = Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_MSIL/System.Speech'
$taskSpeech = Get-ChildItem -LiteralPath $taskSpeechRoot -Filter System.Speech.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName
if (-not $taskSpeech) { throw 'Windows System.Speech assembly not found.' }
New-Item -ItemType Directory -Path $taskBuild -Force | Out-Null
$taskCommon = @('/nologo','/utf8output','/optimize+','/platform:x64','/unsafe','/r:System.Core.dll','/r:System.Web.Extensions.dll')
$taskSources = @('Contracts.cs','LmuReader.cs','LmuCircuitGuide.cs','LmuCoursePractice.cs','AssettoReader.cs','AccTrackLengthProvider.cs','AcrReader.cs','Ams2Reader.cs','WrcReader.cs','WrcPracticeSetup.cs','CoachEngine.cs','PrincipleCoach.cs','DriftPracticeCoach.cs','DriftAttemptEvidence.cs','ContinuousDriftCoach.cs','TougeRunCoach.cs','RallySkillCoach.cs','FullStageCoach.cs','RallyStageComparison.cs','ReferenceLap.cs','ReferenceComparison.cs','CapturedLap.cs','ReferenceLiveSettings.cs','ReferenceLibrary.cs','AccReplayAlignment.cs','LiveReferenceCoach.cs','RaceCornerDiagnosis.cs') | ForEach-Object { Join-Path $taskRoot $_ }
& $taskCompiler @taskCommon '/target:winexe' '/main:IntsLiveCoach.Program' '/r:System.Drawing.dll' '/r:System.Windows.Forms.dll' ("/r:" + $taskSpeech) ("/out:" + (Join-Path $taskBuild 'INTS-Live-Coach.exe')) @taskSources (Join-Path $taskRoot 'Program.cs') (Join-Path $taskRoot 'PracticePanel.cs') (Join-Path $taskRoot 'ReferencePanel.cs') (Join-Path $taskRoot 'CornerReviewPanel.cs') (Join-Path $taskRoot 'CircuitPanel.cs')
if ($LASTEXITCODE -ne 0) { throw 'Live Coach build failed.' }
$taskPublicLaps = Join-Path $taskRoot 'public-references'
$taskLibrary = Join-Path $taskBuild 'references'
New-Item -ItemType Directory -Path $taskLibrary -Force | Out-Null
Get-ChildItem -LiteralPath $taskPublicLaps -Filter '*.reference.json' -File | Copy-Item -Destination $taskLibrary -Force
$taskExample = Join-Path $taskRoot 'review-example/imola-comparison.json'
$taskExampleFolder = Join-Path $taskBuild 'review-example'
New-Item -ItemType Directory -Path $taskExampleFolder -Force | Out-Null
Copy-Item -LiteralPath $taskExample -Destination $taskExampleFolder -Force
$taskTests = Get-ChildItem -LiteralPath $taskRoot -Filter '*Tests.cs' | Select-Object -ExpandProperty FullName
& $taskCompiler @taskCommon '/target:exe' '/main:IntsLiveCoach.SelfTestMain' ("/out:" + (Join-Path $taskBuild 'INTS-Coach-Checks.exe')) @taskSources (Join-Path $taskRoot 'SelfTestMain.cs') @taskTests
if ($LASTEXITCODE -ne 0) { throw 'Live Coach checks build failed.' }
if ($Test) { & (Join-Path $taskBuild 'INTS-Coach-Checks.exe'); if ($LASTEXITCODE -ne 0) { throw 'Live Coach checks failed.' } }
Write-Output ("Built: " + (Join-Path $taskBuild 'INTS-Live-Coach.exe'))
if ($Package) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $publicZip = Join-Path $taskBuild 'INTS-Live-Coach-0.9.0-public.zip'
    $zipTemp = $publicZip + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $zip = [IO.Compression.ZipFile]::Open($zipTemp, 'Create')
        try {
            foreach ($program in @('INTS-Live-Coach.exe', 'INTS-Coach-Checks.exe')) {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $taskBuild $program), $program) | Out-Null
            }
            $documents = @('README.md','LMU-COMMUNITY-SOURCES.md','WRC-SETUP.md','START-HERE.txt',
                'ACR-NOTICES.txt','ADAPTER-NOTICES.txt','Assetto-NOTICES.txt','REFERENCE-NOTICES.txt','THIRD_PARTY_NOTICES.txt',
                'import-motec.mjs','import-acc-motec.mjs','import-accreplay.mjs','import-lmu-community.py','lmu-community-sessions.json')
            $documents += @(Get-ChildItem -LiteralPath $taskRoot -Filter 'reference-*.example.json' -File | Select-Object -ExpandProperty Name)
            foreach ($document in $documents) {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $taskRoot $document), $document) | Out-Null
            }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $taskExample, 'review-example/imola-comparison.json') | Out-Null
            # Ship only the redistributable source directory, never the driver's runtime library.
            foreach ($lap in (Get-ChildItem -LiteralPath $taskPublicLaps -Filter '*.reference.json' -File)) {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $lap.FullName, ('references/' + $lap.Name)) | Out-Null
            }
        } finally { $zip.Dispose() }
        if ([IO.File]::Exists($publicZip)) { [IO.File]::Replace($zipTemp, $publicZip, [System.Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($zipTemp, $publicZip) }
        Write-Output ("Packaged: " + $publicZip)
    } finally { if ([IO.File]::Exists($zipTemp)) { [IO.File]::Delete($zipTemp) } }
}
