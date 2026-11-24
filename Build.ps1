param([string] $OutputName = 'WindowLabels-v2.exe')

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $framework)) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $framework)) {
    throw 'The .NET Framework C# compiler was not found.'
}

$output = Join-Path $PSScriptRoot $OutputName
$manifest = Join-Path $PSScriptRoot 'WindowLabels.manifest'
$sources = @('WindowLabels.cs', 'LabelsPanel.cs', 'BadgeForm.cs', 'NativeWindows.cs', 'WindowPickerButton.cs') |
    ForEach-Object { Join-Path $PSScriptRoot $_ }
$options = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/warn:4', '/warnaserror', '/codepage:65001',
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', "/out:$output", "/win32manifest:$manifest")
& $framework @options @sources
if ($LASTEXITCODE -ne 0) {
    throw "WindowLabels compilation failed ($LASTEXITCODE)."
}
Write-Output $output
