# Mesure A/B alternée dans la même séance : A, B, A, B... avec bench_level.ps1.
# Usage : ab.ps1 -EnvB @{...} -Rounds 2 -Tag nom [-Seconds 90]
param([hashtable]$EnvB, [int]$Rounds = 2, [string]$Tag = 'ab', [int]$Seconds = 90, [int]$Warmup = 25)
$bench = Join-Path $PSScriptRoot 'bench_level.ps1'
$base = @{ APS5_NO_WRITE_WATCH = '1' }
$setB = $base.Clone(); foreach ($k in $EnvB.Keys) { $setB[$k] = $EnvB[$k] }
$keysB = @($EnvB.Keys)
for ($i = 1; $i -le $Rounds; $i++) {
  foreach ($k in $keysB) { Remove-Item "env:$k" -ErrorAction SilentlyContinue }
  & $bench -Seconds $Seconds -Warmup $Warmup -Env $base -Tag "$Tag-A$i" | Select-Object -Last 1
  & $bench -Seconds $Seconds -Warmup $Warmup -Env $setB -Tag "$Tag-B$i" | Select-Object -Last 1
}
foreach ($k in $keysB) { Remove-Item "env:$k" -ErrorAction SilentlyContinue }
