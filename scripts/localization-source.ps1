function Resolve-InterfaceResourcesForInspection([string]$source) {
    $catalogPath = Join-Path $PSScriptRoot '../src/SlashText/Localization/pt-BR.json'
    $catalog = Get-Content $catalogPath -Raw | ConvertFrom-Json -AsHashtable
    return [regex]::Replace($source, '\{DynamicResource (Ui\.[A-F0-9]{16})\}', {
        param($match)
        $key = $match.Groups[1].Value
        if (-not $catalog.ContainsKey($key)) { throw "Interface resource missing: $key" }
        [System.Security.SecurityElement]::Escape($catalog[$key])
    })
}

function Resolve-InterfaceCallsForInspection([string]$source) {
    return $source -replace 'UiText.Get\(("(?:\\.|[^"\\])*")\)', '$1'
}
