#!/usr/bin/env pwsh
# Снять SHM-дампы ACC (physics/graphics/static) + валидация на месте.
# Разовая операция разработчика — см. tests/SimCoach.Adapters.ACC.Tests/Fixtures/README.md.
#
# КАК СНИМАТЬ:
#   1. ACC -> практика -> выезд на трассу.
#   2. НЕ ставь паузу (Esc): на паузе ACC обнуляет ВСЮ physics-страницу
#      (остаётся тикать только packetId). Снимай в статусе LIVE — стоя на
#      трассе с заведённым двигателем (стабильно, torn read не грозит) либо
#      на ходу (чтение 800 байт почти атомарно, риск torn read крошечный).
#   3. Из нужной папки:  ./scripts/snap-acc-shm.ps1
#
# Годный дамп: status = 2 (LIVE) и fuel > 0.

$ErrorActionPreference = 'Stop'
$sizes = @{ physics = 800; graphics = 1588; static = 820 }

function Read-Page($name, $size) {
    $mmf = [System.IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting("Local\acpmf_$name")
    try {
        $vs = $mmf.CreateViewStream(0, $size)
        try {
            $bytes = New-Object byte[] $size
            $null = $vs.Read($bytes, 0, $size)
            return ,$bytes
        } finally { $vs.Dispose() }
    } finally { $mmf.Dispose() }
}

try {
    foreach ($n in 'physics','graphics','static') {
        [System.IO.File]::WriteAllBytes("$PWD\acc_$n.bin", (Read-Page $n $sizes[$n]))
    }
} catch {
    Write-Host "ОШИБКА: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "ACC не запущена или ты не в сессии (страницы создаются при входе в сессию)." -ForegroundColor Yellow
    exit 1
}

$p = [System.IO.File]::ReadAllBytes("$PWD\acc_physics.bin")
$g = [System.IO.File]::ReadAllBytes("$PWD\acc_graphics.bin")
$s = [System.IO.File]::ReadAllBytes("$PWD\acc_static.bin")

$status   = [BitConverter]::ToInt32($g, 4)
$session  = [BitConverter]::ToInt32($g, 8)
$gearRaw  = [BitConverter]::ToInt32($p, 16)
$rpm      = [BitConverter]::ToInt32($p, 20)
$speed    = [BitConverter]::ToSingle($p, 28)
$fuel     = [BitConverter]::ToSingle($p, 12)
$pidPhys  = [BitConverter]::ToInt32($p, 0)
$smVer    = [System.Text.Encoding]::Unicode.GetString($s, 0, 30).TrimEnd([char]0)
$carModel = [System.Text.Encoding]::Unicode.GetString($s, 68, 66).TrimEnd([char]0)
$track    = [System.Text.Encoding]::Unicode.GetString($s, 134, 66).TrimEnd([char]0)

$statusName  = @{0='OFF';1='REPLAY';2='LIVE';3='PAUSE'}[$status]
$sessionName = @{-1='UNKNOWN';0='PRACTICE';1='QUALIFY';2='RACE';3='HOTLAP';4='TIME_ATTACK'}[$session]

Write-Host ""
Write-Host "=== STATIC ==="  -ForegroundColor Cyan
Write-Host "  smVersion = $smVer   car = $carModel   track = $track"
Write-Host "=== GRAPHICS ===" -ForegroundColor Cyan
Write-Host "  status  = $status ($statusName)"
Write-Host "  session = $session ($sessionName)"
Write-Host "=== PHYSICS ===" -ForegroundColor Cyan
Write-Host "  packetId = $pidPhys"
Write-Host ("  gear(raw) = {0}  (display = {1};  4 == 3rd)" -f $gearRaw, ($gearRaw - 1))
Write-Host "  rpm = $rpm"
Write-Host ("  speedKmh = {0:N2}" -f $speed)
Write-Host ("  fuel = {0:N2} L" -f $fuel)
Write-Host ""

if ($status -eq 2 -and $fuel -gt 0) {
    Write-Host "OK: дамп годный (LIVE, fuel > 0)." -ForegroundColor Green
} elseif ($status -eq 3) {
    Write-Host "БРАК: status = PAUSE -> physics обнулён. Сними паузу (Esc) и сними заново." -ForegroundColor Red
} else {
    Write-Host "ВНИМАНИЕ: status != LIVE или fuel = 0. Проверь, что ты на трассе с заведённым двигателем." -ForegroundColor Yellow
}
