$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $root 'src/SlashText/Localization'
$original = Get-Content "$folder/pt-BR.json" -Raw | ConvertFrom-Json -AsHashtable
foreach ($language in @('pt-BR', 'en', 'es')) {
    $catalog = Get-Content "$folder/$language.json" -Raw | ConvertFrom-Json -AsHashtable
    if (@(Compare-Object @($original.Keys | Sort-Object) @($catalog.Keys | Sort-Object)).Count) { throw "Incomplete language catalog: $language" }
    foreach ($key in $original.Keys) {
        if ([string]::IsNullOrWhiteSpace($catalog[$key])) { throw "Empty translation: $language $key" }
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($original[$key]))).Substring(0,16)
        if ($key -cne "Ui.$hash") { throw "Unstable source key: $key" }
    }
}
foreach ($file in Get-ChildItem "$root/src/SlashText" -Recurse -Include '*.xaml','*.cs' | Where-Object { $_.FullName -notmatch '[\\/](bin|obj|Design)[\\/]' }) {
    $source = Get-Content $file.FullName -Raw
    if ($file.Extension -eq '.xaml') {
        [xml]$document = $source
        foreach ($element in $document.SelectNodes('//*[local-name()="TextBlock" or local-name()="Run"]')) {
            foreach ($node in $element.ChildNodes) {
                if ($node.NodeType -eq [System.Xml.XmlNodeType]::Text -and -not [string]::IsNullOrWhiteSpace($node.Value)) {
                    throw "Uncatalogued inline interface text: $($file.Name) $($node.Value)"
                }
            }
        }
    }
    foreach ($match in [regex]::Matches($source, '\{DynamicResource (Ui\.[A-F0-9]{16})\}')) {
        if (-not $original.ContainsKey($match.Groups[1].Value)) { throw "Missing XAML interface resource: $($file.Name) $($match.Value)" }
    }
    foreach ($match in [regex]::Matches($source, 'UiText.Get\("((?:\\.|[^"\\])*)"\)')) {
        # Decode C# ordinary string escapes; interface strings here contain no raw/verbatim literals.
        $value = '"' + $match.Groups[1].Value + '"' | ConvertFrom-Json
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($value))).Substring(0,16)
        if (-not $original.ContainsKey("Ui.$hash")) { throw "Uncatalogued interface string: $($file.Name) $value" }
    }
}
Write-Host "PASS: three complete catalogs, stable source keys, all explicit XAML/C# string references."
