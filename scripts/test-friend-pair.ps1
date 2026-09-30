$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'friend-pair.ps1'
$parseErrors = $null
$tokens = $null
$syntax = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw 'The pairing script has syntax errors.' }
Add-Type -AssemblyName System.Drawing
foreach ($name in 'Resolve-AreaMenuRow', 'Get-AreaMenuRowCount') {
    $resolver = $syntax.Find({ param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    if ($null -eq $resolver) { throw "The menu helper $name is missing." }
    . ([ScriptBlock]::Create($resolver.Extent.Text))
}
if ((Resolve-AreaMenuRow 1 2) -ne 1) { throw 'Sparse area 1 must use the first menu row.' }
if ((Resolve-AreaMenuRow 11 2) -ne 2) { throw 'Sparse area 11 must use the second menu row.' }
foreach ($number in 1..11) {
    if ((Resolve-AreaMenuRow $number 11) -ne $number) { throw 'Contiguous areas must retain their row number.' }
}
foreach ($number in 0, 2, 5, 10, 12) {
    $rejected = $false
    try { $null = Resolve-AreaMenuRow $number 2 } catch { $rejected = $true }
    if (-not $rejected) { throw 'An unsupported sparse area number was accepted.' }
}
foreach ($rowCount in 0, 1, 2, 3, 11) {
    $image = [System.Drawing.Bitmap]::new(950, 596)
    $graphics = [System.Drawing.Graphics]::FromImage($image)
    $brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(240, 190, 30))
    try {
        $graphics.Clear([System.Drawing.Color]::Black)
        for ($index = 0; $index -lt $rowCount; $index++) { $graphics.FillRectangle($brush, 683, (79 + 25 * $index), 11, 11) }
        $rejected = $false
        try {
            $observed = Get-AreaMenuRowCount $image
            if ($observed -ne $rowCount) { throw 'Incorrect row count.' }
        } catch { $rejected = $true }
        if ($rejected -ne ($rowCount -ne 2 -and $rowCount -ne 11)) { throw 'Unexpected menu-layout acceptance.' }
    }
    finally { $brush.Dispose(); $graphics.Dispose(); $image.Dispose() }
}
Write-Output 'Friend pairing sparse and contiguous area-menu regressions passed.'
