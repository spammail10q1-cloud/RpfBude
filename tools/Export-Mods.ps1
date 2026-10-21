# Export-Mods.ps1
# Packt die aktuelle LSPDFR-Installation in einen Payload-Ordner,
# den ein anderer Rechner mit Install-LSPDFR.ps1 einspielen kann.
# Scripts (SHVDN) werden bewusst NICHT mitgenommen.

param(
    [string]$GamePath = 'D:\SteamLibrary\steamapps\common\Grand Theft Auto V',
    [string]$OutDir   = "$PSScriptRoot\payload",
    [switch]$WithAudio
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $GamePath)) { throw "Spielordner nicht gefunden: $GamePath" }

$files = @()
$dirs  = @()

# Root-Dateien, die RAGE Plugin Hook und LSPDFR brauchen
$files += @(
    'RAGEPluginHook.exe',
    'EasyHook64.dll', 'EasyLoad64.dll',
    'SlimDX.dll', 'Gwen.dll', 'Gwen.UnitTest.dll',
    'Mono.Cecil.dll', 'Mono.Cecil.Mdb.dll', 'Mono.Cecil.Pdb.dll', 'Mono.Cecil.Rocks.dll',
    'FW1FontWrapper.dll',
    'DiscordRpcNet.dll', 'discord-rpc.dll'
)

# LSPDFR selbst
$files += @('plugins\LSPD First Response.dll', 'plugins\LSPDFR\LSPDFR.dll')

# Alle Plugins
$dirs += @('plugins\LSPDFR')

# Konfiguration und Daten
$dirs += @('lspdfr')

$copied = 0
function Copy-One([string]$rel) {
    $src = Join-Path $GamePath $rel
    if (-not (Test-Path $src)) { Write-Host "  fehlt, uebersprungen: $rel" -ForegroundColor DarkYellow; return }
    $dst = Join-Path $OutDir $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
    Copy-Item $src $dst -Force
    $script:copied++
}

foreach ($f in $files) { Copy-One $f }

foreach ($d in $dirs) {
    $src = Join-Path $GamePath $d
    if (-not (Test-Path $src)) { Write-Host "  fehlt, uebersprungen: $d" -ForegroundColor DarkYellow; continue }
    Get-ChildItem $src -Recurse -File | ForEach-Object {
        if (-not $WithAudio -and $_.Extension -eq '.wav') { return }
        $rel = $_.FullName.Substring($GamePath.Length).TrimStart('\')
        Copy-One $rel
    }
}

# Manifest fuer die Gegenstelle
$manifest = @{
    created  = (Get-Date).ToString('s')
    source   = $GamePath
    withAudio = [bool]$WithAudio
    files    = $copied
}
$manifest | ConvertTo-Json | Set-Content (Join-Path $OutDir 'manifest.json') -Encoding UTF8

Write-Host ""
Write-Host "Fertig: $copied Dateien in $OutDir" -ForegroundColor Green
Write-Host "Diesen Ordner zusammen mit Install-LSPDFR.ps1 weitergeben."
Write-Host "Nicht enthalten: scripts\ (SHVDN-Mods), Trainer, Grafikmods." -ForegroundColor DarkGray
