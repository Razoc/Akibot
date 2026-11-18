# Mesure de FPS dans un niveau, sans jouer : lance le jeu installé par AkiBot directement dans le niveau
# (-lvl), lit chaque seconde le FPS du titre de la fenêtre, ne garde que les secondes passées dans ce niveau
# après une période de chauffe, puis ferme le jeu. Résultat : CSV + résumé (moyenne, médiane, min, 1 % bas).
# Usage : bench_level.ps1 -Level underwater_aerial_garden -Seconds 120 -Warmup 25 -Env @{...} -Tag nom
param(
    [string]$InstallDir = 'C:\AkiBot',
    [string]$Level = 'underwater_aerial_garden',
    [int]$Seconds = 120,
    [int]$Warmup = 25,
    [int]$Width = 2560,
    [int]$Height = 1440,
    [hashtable]$Env = @{},
    [string]$Tag = 'bench'
)
$ErrorActionPreference = 'Stop'
$out = Join-Path $InstallDir 'Logs\hybrid-run.out.log'
$err = Join-Path $InstallDir 'Logs\hybrid-run.err.log'
Get-Process AstroBot* -ErrorAction SilentlyContinue | Stop-Process -Force
# Attendre la vraie fermeture du jeu précédent, puis effacer ses journaux (sinon on lit l'ancien « Level has started »)
$t = Get-Date
while ((Get-Process AstroBot* -ErrorAction SilentlyContinue) -and ((Get-Date) - $t).TotalSeconds -lt 30) { Start-Sleep -Milliseconds 300 }
Start-Sleep -Seconds 3
Remove-Item $out, $err -ErrorAction SilentlyContinue
$base = @{ AKIBOT_FPS_LIMIT = 'unlimited'; KYTY_GPU_WIDTH = "$Width"; KYTY_GPU_HEIGHT = "$Height"; AKIBOT_SKIP_INTRO = '1' }
foreach ($k in $Env.Keys) { $base[$k] = $Env[$k] }
foreach ($k in $base.Keys) { Set-Item -Path "env:$k" -Value $base[$k] }
Start-Process (Join-Path $InstallDir 'AkiBot.exe') -ArgumentList '--run', '-lvl', $Level -WorkingDirectory $InstallDir | Out-Null

# Attendre l'entrée dans le niveau
$t0 = Get-Date; $inLevel = $null
while (-not $inLevel -and ((Get-Date) - $t0).TotalSeconds -lt 240) {
    Start-Sleep 1
    if ((Test-Path $out) -and (Select-String -Path $out -Pattern "Level has started: $Level" -SimpleMatch -Quiet)) { $inLevel = Get-Date }
    if ((Test-Path $err) -and (Select-String -Path $err -Pattern 'FATAL' -SimpleMatch -Quiet)) { break }
}
if (-not $inLevel) { Get-Process AstroBot* -ErrorAction SilentlyContinue | Stop-Process -Force; "[$Tag] niveau jamais atteint"; exit 1 }
$load = [int]($inLevel - $t0).TotalSeconds
Start-Sleep -Seconds $Warmup

$rows = New-Object System.Collections.Generic.List[object]
$end = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $end) {
    $p = Get-Process AstroBot* -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $p) { break }
    if ($p.MainWindowTitle -match 'frame:\s*(\d+),\s*fps:\s*([\d.]+)') {
        $rows.Add([pscustomobject]@{ time = (Get-Date).ToString('HH:mm:ss'); fps = [double]$Matches[2]; frame = [long]$Matches[1] })
    }
    Start-Sleep -Seconds 1
}
$crashed = (Test-Path $err) -and (Select-String -Path $err -Pattern 'FATAL' -SimpleMatch -Quiet)
Get-Process AstroBot* -ErrorAction SilentlyContinue | Stop-Process -Force
$csv = Join-Path $PSScriptRoot "bench-$Tag.csv"
$rows | Export-Csv -NoTypeInformation -Encoding UTF8 $csv
if ($rows.Count -eq 0) { "[$Tag] aucune mesure (titre sans fps ?)"; exit 1 }
$f = @($rows | ForEach-Object { $_.fps } | Sort-Object)
$n = $f.Count
$avg = ($f | Measure-Object -Average).Average
$med = $f[[int][Math]::Floor($n / 2)]
$low1 = $f[0..([Math]::Max(0, [int][Math]::Ceiling($n * 0.01) - 1))] | Measure-Object -Average | ForEach-Object Average
# FPS réel moyen d'après le compteur d'images (plus fiable que la moyenne des valeurs affichées)
$frames = $rows[$rows.Count - 1].frame - $rows[0].frame
$secs = ([datetime]::ParseExact($rows[$rows.Count - 1].time, 'HH:mm:ss', $null) - [datetime]::ParseExact($rows[0].time, 'HH:mm:ss', $null)).TotalSeconds
$real = if ($secs -gt 0) { $frames / $secs } else { 0 }
"[{0}] {1} {2}x{3} env={4} | chargement {5} s | {6} échantillons | moyenne {7:N1} | réel (compteur) {8:N1} | médiane {9:N0} | min {10:N0} | 1 % bas {11:N1} | max {12:N0}{13}" -f `
    $Tag, $Level, $Width, $Height, (($Env.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ','), $load, $n, $avg, $real, $med, $f[0], $low1, $f[$n - 1], $(if ($crashed) { ' | PLANTAGE' } else { '' })
