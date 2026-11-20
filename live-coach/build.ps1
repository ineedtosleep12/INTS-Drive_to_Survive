param([switch]$Test)
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
$taskSources = @('Contracts.cs','LmuReader.cs','AssettoReader.cs','AcrReader.cs','Ams2Reader.cs','WrcReader.cs','CoachEngine.cs','PrincipleCoach.cs','ReferenceLap.cs','ReferenceComparison.cs','CapturedLap.cs','ReferenceLiveSettings.cs','ReferenceLibrary.cs','LiveReferenceCoach.cs') | ForEach-Object { Join-Path $taskRoot $_ }
& $taskCompiler @taskCommon '/target:winexe' '/main:IntsLiveCoach.Program' '/r:System.Drawing.dll' '/r:System.Windows.Forms.dll' ("/r:" + $taskSpeech) ("/out:" + (Join-Path $taskBuild 'INTS-Live-Coach.exe')) @taskSources (Join-Path $taskRoot 'Program.cs') (Join-Path $taskRoot 'ReferencePanel.cs')
if ($LASTEXITCODE -ne 0) { throw 'Live Coach build failed.' }
$taskTests = Get-ChildItem -LiteralPath $taskRoot -Filter '*Tests.cs' | Select-Object -ExpandProperty FullName
& $taskCompiler @taskCommon '/target:exe' '/main:IntsLiveCoach.SelfTestMain' ("/out:" + (Join-Path $taskBuild 'INTS-Coach-Checks.exe')) @taskSources (Join-Path $taskRoot 'SelfTestMain.cs') @taskTests
if ($LASTEXITCODE -ne 0) { throw 'Live Coach checks build failed.' }
if ($Test) { & (Join-Path $taskBuild 'INTS-Coach-Checks.exe'); if ($LASTEXITCODE -ne 0) { throw 'Live Coach checks failed.' } }
Write-Output ("Built: " + (Join-Path $taskBuild 'INTS-Live-Coach.exe'))
