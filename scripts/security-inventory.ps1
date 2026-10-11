param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$OutputDirectory,
      [ValidateSet('single-file', 'directory')][string]$Layout = 'single-file')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'tools/SlashDesk.ResourceInventory/SlashDesk.ResourceInventory.csproj'
dotnet build $project --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Metadata inventory tool failed to build.' }
$tool = Join-Path $root 'tools/SlashDesk.ResourceInventory/bin/Release/net10.0/SlashDesk.ResourceInventory.dll'
$dotnet = (Get-Command dotnet).Source
python (Join-Path $PSScriptRoot 'security/inventory.py') --executable $Executable --root $root --resource-tool $tool --dotnet $dotnet --output $OutputDirectory --layout $Layout
if ($LASTEXITCODE -ne 0) { throw 'Published inventory failed.' }
