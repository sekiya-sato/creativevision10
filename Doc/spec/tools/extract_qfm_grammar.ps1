# Extract qfm XML grammar from one or more corpora (Shift_JIS/cp932).
# Usage: powershell -File extract_qfm_grammar.ps1 <dir1> [<dir2> ...]
param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Dirs)

$enc = [System.Text.Encoding]::GetEncoding(932)
$files = @()
foreach ($d in $Dirs) { $files += Get-ChildItem -Path $d -Recurse -Filter *.qfm -File -ErrorAction SilentlyContinue }
Write-Host "Files: $($files.Count)"

$elemCount   = @{}                       # element -> count
$attrByElem  = @{}                       # element -> @{ attr -> count }
$valByElemAttr = @{}                     # "element|attr" -> @{ value -> count }
# element/attr pairs whose value enums we care about (small cardinality)
$enumWanted = @{
  'printstream|version'=1;'page|orientation'=1;'page|cpi'=1;'page|lpi'=1;'page|compatibility'=1;'page|tree'=1;
  'path|datatype'=1;'data|calctype'=1;'data|groupcontrol'=1;'data|suppress'=1;
  'decode|datatype'=1;'font|style'=1;'font|face'=1;'font|size'=1;
  'text|valign'=1;'text|halign'=1;'text|wrap'=1;'barcode|type'=1;'barcode|check'=1;'barcode|sschar'=1;
  'group|level'=1;'group|pagechange'=1;'image|relative'=1;'color|transparent'=1;'region|direction'=1;
  'record|type'=1;'record|recordtype'=1;'cceffect|type'=1
}

$tagRe  = [regex]'<([a-zA-Z][a-zA-Z0-9_]*)((?:\s+[a-zA-Z_][a-zA-Z0-9_]*="[^"]*")*)\s*/?>'
$attrRe = [regex]'([a-zA-Z_][a-zA-Z0-9_]*)="([^"]*)"'

foreach ($f in $files) {
  $text = [System.IO.File]::ReadAllText($f.FullName, $enc)
  foreach ($m in $tagRe.Matches($text)) {
    $el = $m.Groups[1].Value
    if ($elemCount.ContainsKey($el)) { $elemCount[$el]++ } else { $elemCount[$el]=1 }
    if (-not $attrByElem.ContainsKey($el)) { $attrByElem[$el] = @{} }
    foreach ($am in $attrRe.Matches($m.Groups[2].Value)) {
      $a = $am.Groups[1].Value; $v = $am.Groups[2].Value
      if ($attrByElem[$el].ContainsKey($a)) { $attrByElem[$el][$a]++ } else { $attrByElem[$el][$a]=1 }
      $key = "$el|$a"
      if ($enumWanted.ContainsKey($key)) {
        if (-not $valByElemAttr.ContainsKey($key)) { $valByElemAttr[$key] = @{} }
        if ($valByElemAttr[$key].ContainsKey($v)) { $valByElemAttr[$key][$v]++ } else { $valByElemAttr[$key][$v]=1 }
      }
    }
  }
}

Write-Host "`n=== ELEMENTS (count) ==="
$elemCount.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { "{0,8}  {1}" -f $_.Value,$_.Key }

Write-Host "`n=== ATTRIBUTES per element (count) ==="
foreach ($el in ($attrByElem.Keys | Sort-Object)) {
  "<$el>"
  $attrByElem[$el].GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { "    {0,8}  {1}" -f $_.Value,$_.Key }
}

Write-Host "`n=== ENUM VALUES (selected element|attr) ==="
foreach ($k in ($valByElemAttr.Keys | Sort-Object)) {
  "[$k]"
  $vals = $valByElemAttr[$k]
  if ($vals.Count -le 60) {
    $vals.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { "    {0,8}  {1}" -f $_.Value,$_.Key }
  } else {
    "    (distinct=$($vals.Count), top 30:)"
    $vals.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 30 | ForEach-Object { "    {0,8}  {1}" -f $_.Value,$_.Key }
  }
}
