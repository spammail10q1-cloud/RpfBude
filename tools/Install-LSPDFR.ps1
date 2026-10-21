# Install-LSPDFR.ps1
# Installiert LSPDFR samt Plugins und Konfiguration aus dem Payload-Ordner.
# Scripts (SHVDN), Trainer und Grafikmods werden nicht angefasst.

param(
    [string]$GamePath = '',
    [string]$Payload  = "$PSScriptRoot\payload",
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

function Find-Game {
    foreach ($p in @(
        'D:\SteamLibrary\steamapps\common\Grand Theft Auto V',
        'C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V',
        'C:\Program Files\Rockstar Games\Grand Theft Auto V',
        'E:\SteamLibrary\steamapps\common\Grand Theft Auto V'
    )) { if (Test-Path (Join-Path $p 'GTA5.exe')) { return $p } }
    return $null
}

if (-not $GamePath) { $GamePath = Find-Game }
if (-not $GamePath -or -not (Test-Path (Join-Path $GamePath 'GTA5.exe'))) {
    throw "GTA V nicht gefunden. Bitte -GamePath angeben."
}
if (-not (Test-Path $Payload)) { throw "Payload-Ordner fehlt: $Payload" }

Write-Host "Spiel:   $GamePath"
Write-Host "Payload: $Payload"
Write-Host ""

# 1) BattlEye aus, sonst startet RAGE Plugin Hook nicht
$argsFile = Join-Path $GamePath 'args.txt'
$argsLine = '-nobattleye -noBE'
if (Test-Path $argsFile) {
    $cur = (Get-Content $argsFile -Raw).Trim()
    if ($cur -notmatch 'nobattleye') {
        Copy-Item $argsFile "$argsFile.bak" -Force
        Set-Content $argsFile "$cur $argsLine".Trim() -Encoding ASCII
        Write-Host "args.txt ergaenzt: -nobattleye -noBE"
    }
} else {
    Set-Content $argsFile $argsLine -Encoding ASCII
    Write-Host "args.txt angelegt."
}

# 2) Dateien kopieren
$count = 0
Get-ChildItem $Payload -Recurse -File | Where-Object { $_.Name -ne 'manifest.json' } | ForEach-Object {
    $rel = $_.FullName.Substring((Resolve-Path $Payload).Path.Length).TrimStart('\')
    $dst = Join-Path $GamePath $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
    if ((Test-Path $dst) -and -not $Force) {
        Copy-Item $dst "$dst.bak" -Force -ErrorAction SilentlyContinue
    }
    Copy-Item $_.FullName $dst -Force
    $count++
}
Write-Host "Kopiert: $count Dateien"

# 3) Konfiguration, die vom Standard abweicht
$keys = Join-Path $GamePath 'lspdfr\keys.ini'
if (Test-Path $keys) {
    $k = Get-Content $keys -Raw
    $k = [regex]::Replace($k, '(?m)^BACKUP_MENU_Key\s*=.*$',        'BACKUP_MENU_Key=NumPad9')
    $k = [regex]::Replace($k, '(?m)^CRIME_REPORT_Key\s*=.*$',       'CRIME_REPORT_Key=NumPad7')
    $k = [regex]::Replace($k, '(?m)^TRAFFICSTOP_START_Key\s*=.*$',  'TRAFFICSTOP_START_Key=LShiftKey')
    $k = [regex]::Replace($k, '(?m)^TRAFFICSTOP_INTERACT_Key\s*=.*$','TRAFFICSTOP_INTERACT_Key=NumPad3')
    Set-Content $keys $k -Encoding UTF8
    Write-Host "keys.ini gesetzt (B = Ultimate Backup, E = Stop The Ped, LShift = LSPDFR Stop)."
}

$ini = Join-Path $GamePath 'lspdfr\lspdfr.ini'
if (Test-Path $ini) {
    $t = Get-Content $ini -Raw
    $t = [regex]::Replace($t, '(?m)^Networking\.DisableDiscordRichPresence\s*=.*$', 'Networking.DisableDiscordRichPresence = false')
    Set-Content $ini $t -Encoding UTF8
}

# 4) Bericht
$plugins = Get-ChildItem (Join-Path $GamePath 'plugins\LSPDFR') -Filter '*.dll' -ErrorAction SilentlyContinue
Write-Host ""
Write-Host "Plugins in plugins\LSPDFR: $($plugins.Count)" -ForegroundColor Green
Write-Host ""
Write-Host "Start: RAGEPluginHook.exe   (nicht ueber Steam)" -ForegroundColor Cyan
Write-Host "Im Spiel: E = on duty, B = Ultimate Backup, G = Stop The Ped, F4 = Trainer" -ForegroundColor Cyan
Write-Host ""
Write-Host "Fehlt RAGEPluginHook.exe, zuerst die LSPDFR-Manual-Installation ausfuehren." -ForegroundColor DarkGray
