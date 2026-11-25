# Read-only validation of the repository's Markdown vault. No extra modules.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$vaultRoot = Join-Path $repoRoot 'Development~/Knowledge'
$issues = New-Object 'System.Collections.Generic.List[string]'
$templates = Get-Content -LiteralPath (Join-Path $vaultRoot '.obsidian/templates.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$templateRoot = [IO.Path]::GetFullPath((Join-Path $vaultRoot $templates.folder))

function Test-WithinRoot([string]$Path, [string]$Root) {
    $prefix = $Root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    return $Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function Check-Target([string]$Base, [string]$Target, [string]$AllowedRoot, [string]$Context, [switch]$FileOnly) {
    try {
        if ([IO.Path]::IsPathRooted($Target)) {
            $issues.Add("${Context}: absolute path is not portable: $Target")
            return
        }
        $full = [IO.Path]::GetFullPath((Join-Path $Base $Target))
        if (-not (Test-WithinRoot $full $AllowedRoot)) {
            $issues.Add("${Context}: target escapes the allowed root: $Target")
        } elseif (-not (Test-Path -LiteralPath $full)) {
            $issues.Add("${Context}: missing target: $Target")
        } elseif ($FileOnly -and -not (Test-Path -LiteralPath $full -PathType Leaf)) {
            $issues.Add("${Context}: expected a file: $Target")
        }
    } catch {
        $issues.Add("${Context}: invalid target '$Target': $_")
    }
}

$statusByType = @{
    map = @('current'); guide = @('current'); verification = @('current')
    system = @('planned', 'in-progress', 'implemented', 'retired')
    decision = @('proposed', 'accepted', 'superseded')
    idea = @('draft', 'accepted', 'rejected')
}
$notes = @(Get-ChildItem -LiteralPath $vaultRoot -Recurse -File -Filter '*.md' |
    Where-Object { $_.FullName -notmatch '[\\/]\.(obsidian|trash)[\\/]' })
$linkCount = 0
$sourceCount = 0
foreach ($note in $notes) {
    $text = Get-Content -LiteralPath $note.FullName -Raw -Encoding UTF8
    $label = $note.FullName.Substring($repoRoot.Length + 1)
    if (-not (Test-WithinRoot $note.FullName $templateRoot)) {
        $front = [regex]::Match($text, '\A---\r?\n(?<yaml>.*?)\r?\n---(?:\r?\n|$)', 'Singleline')
        if (-not $front.Success) {
            $issues.Add("${label}: missing front matter")
        } else {
            $yaml = $front.Groups['yaml'].Value
            $type = [regex]::Match($yaml, '(?m)^type:\s*([a-z-]+)\s*$').Groups[1].Value
            $status = [regex]::Match($yaml, '(?m)^status:\s*([a-z-]+)\s*$').Groups[1].Value
            $dateText = [regex]::Match($yaml, '(?m)^updated:\s*(\d{4}-\d{2}-\d{2})\s*$').Groups[1].Value
            if (-not $statusByType.ContainsKey($type)) { $issues.Add("${label}: invalid or missing type") }
            elseif ($status -notin $statusByType[$type]) { $issues.Add("${label}: invalid status '$status' for '$type'") }
            $dateValue = [datetime]::MinValue
            if (-not [datetime]::TryParseExact($dateText, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$dateValue)) {
                $issues.Add("${label}: updated must be a valid YYYY-MM-DD date")
            }
            foreach ($source in [regex]::Matches($yaml, '(?m)^\s*-\s*["'']?repo:(?<path>[^\r\n"'']+)["'']?\s*$')) {
                $sourceCount++
                Check-Target $repoRoot $source.Groups['path'].Value.Trim() $repoRoot $label -FileOnly
            }
        }
    }
    # Inline Markdown links only. Ignore examples inside fenced code blocks.
    $body = [regex]::Replace($text, '(?ms)^```[^\r\n]*\r?\n.*?^```[^\r\n]*', '')
    foreach ($link in [regex]::Matches($body, '!?\[[^\]\r\n]*\]\((?<url><[^>]+>|[^)\r\n]+)\)')) {
        $target = $link.Groups['url'].Value.Trim().Trim('<', '>')
        if ($target -match '^(https?://|mailto:|obsidian:|#)') { continue }
        $target = [Uri]::UnescapeDataString(($target -split '#', 2)[0])
        if (-not $target) { continue }
        $linkCount++
        Check-Target $note.DirectoryName $target $vaultRoot $label -FileOnly
    }
}

$canvases = @(Get-ChildItem -LiteralPath $vaultRoot -Recurse -File -Filter '*.canvas' |
    Where-Object { $_.FullName -notmatch '[\\/]\.(obsidian|trash)[\\/]' })
foreach ($file in $canvases) {
    $label = $file.FullName.Substring($repoRoot.Length + 1)
    try {
        $canvas = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        $nodeIds = @{}
        $allIds = @{}
        foreach ($node in $canvas.nodes) {
            if (-not $node.id -or $allIds.ContainsKey([string]$node.id)) { $issues.Add("${label}: missing/duplicate node id") }
            else { $allIds[$node.id] = $true; $nodeIds[$node.id] = $true }
            if ($node.type -notin @('file', 'text', 'link', 'group')) { $issues.Add("${label}: invalid node type") }
            foreach ($field in @('x', 'y', 'width', 'height')) {
                $value = $node.$field
                if ($null -eq $value -or $value -is [string] -or [double]$value -ne [math]::Truncate([double]$value)) {
                    $issues.Add("${label}: node $($node.id) needs integer $field")
                }
            }
            if ($node.width -le 0 -or $node.height -le 0) { $issues.Add("${label}: non-positive node size") }
            if ($node.type -eq 'file') { Check-Target $vaultRoot $node.file $vaultRoot $label -FileOnly }
            if ($node.type -eq 'text' -and $null -eq $node.text) { $issues.Add("${label}: text node has no text") }
            if ($node.type -eq 'link' -and $node.url -notmatch '^https?://') { $issues.Add("${label}: link node needs an HTTP(S) URL") }
        }
        foreach ($edge in $canvas.edges) {
            if (-not $edge.id -or $allIds.ContainsKey([string]$edge.id)) { $issues.Add("${label}: missing/duplicate edge id") }
            else { $allIds[$edge.id] = $true }
            if (-not $nodeIds.ContainsKey([string]$edge.fromNode) -or -not $nodeIds.ContainsKey([string]$edge.toNode)) {
                $issues.Add("${label}: edge references a missing node")
            }
        }
    } catch { $issues.Add("${label}: invalid canvas: $_") }
}

foreach ($name in @('app.json', 'core-plugins.json', 'graph.json', 'templates.json')) {
    try { $null = Get-Content -LiteralPath (Join-Path $vaultRoot ".obsidian/$name") -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { $issues.Add("Obsidian config ${name}: $_") }
}

if ($issues.Count) {
    $issues | ForEach-Object { Write-Output "ERROR: $_" }
    exit 1
}
Write-Output "OK: $($notes.Count) notes, $linkCount local links, $sourceCount source paths, $($canvases.Count) canvases."
Write-Output 'Scope: file existence, note metadata, Canvas structure and config JSON. Not Unity behavior, URLs or heading anchors.'
