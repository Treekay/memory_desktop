param([switch]$Test)
$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Requires the built-in .NET Framework 4.8 x64 compiler on Windows 10/11.' }
$out = Join-Path $repo 'artifacts\MemoryDesktop-win-x64'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$source = Join-Path $repo 'src\MemoryDesktop'
$refs = @('System.dll','System.Core.dll','System.Xml.dll','System.Xaml.dll','System.Drawing.dll','System.Windows.Forms.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$refs += @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF\' + $_)) }
$files = Get-ChildItem -LiteralPath $source -Filter '*.cs' | ForEach-Object FullName
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warn:4 /warnaserror+ "/out:$out\MemoryDesktop.exe" "/win32manifest:$source\app.manifest" "/win32icon:$source\memory.ico" $refs $files
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $repo 'README.md') -Destination $out
Copy-Item -LiteralPath (Join-Path $repo '使用说明.txt') -Destination $out
Copy-Item -LiteralPath (Join-Path $repo 'docs\VERIFICATION.md') -Destination $out
if ($Test) {
    $testOut = Join-Path $repo 'test-results'
    $run = Start-Process -FilePath (Join-Path $out 'MemoryDesktop.exe') -ArgumentList @('--self-test', ('"' + $testOut + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($run.ExitCode -ne 0) { Get-Content -LiteralPath (Join-Path $testOut 'test-report.txt') -Tail 12; throw 'Self-tests failed.' }
    $report = Get-Content -LiteralPath (Join-Path $testOut 'test-report.txt')
    Write-Output ('Tests passed: ' + ($report | Where-Object { $_ -like 'PASS:*' }).Count)
}
Compress-Archive -Path (Join-Path $out '*') -DestinationPath (Join-Path $repo 'artifacts\MemoryDesktop-win-x64.zip') -Force
Write-Output ('Build: ' + (Join-Path $out 'MemoryDesktop.exe'))
