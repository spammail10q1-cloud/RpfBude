# RpfBude

Werkzeug für GTA V RPF7-Archive. Kann Archive lesen und schreiben, die das Spiel
im Original nur NG-verschlüsselt akzeptiert.

## Warum

OpenIV schreibt bei diesem Build (1.0.3889) die `update.rpf` neu und zerstört sie
(`ERR_GEN_INVALID`). RpfBude ändert stattdessen nur den betroffenen Block und
verschlüsselt den Index mit dem NG-Verfahren neu. Die Datei bleibt NG und
byte-genau gültig.

## Was geht

- RPF7 öffnen: `OPEN`, `NG`, `NONE`
- AES-Key automatisch aus `GTA5.exe` suchen
- NG-Entschlüsselung über `magic.dat`
- NG-Verschlüsselung: RoundA über Gauß-Inversion, RoundB über Lookup-Tabellen
- Dateien ansehen und extrahieren
- Dateien ersetzen (NG wird korrekt neu verschlüsselt)
- dlcpack in `dlclist.xml` eintragen und austragen
- dlcpack-Ordner installieren (kopieren + eintragen in einem Schritt)
- `.oiv`-Pakete bauen und installieren
- Integritätsprüfung (Eintragszahlen, Datenende gegen Dateilänge)

## Tabellen

Der erste Start baut die Verschlüsselungstabellen einmalig (ca. 3 Minuten) und
legt sie als `ngenc_a.bin` / `ngenc_b.bin` neben die EXE. Danach wird geladen.

## Bauen

```
dotnet build -c Release
```

Ziel ist `net8.0-windows`, WinForms. `magic.dat` muss neben der EXE liegen.

## Stand

Geprüft: NG-Entschlüsselung liest die echte `update.rpf` (1644 Einträge, 102
dlclist-Einträge). Vollständige Rückverschlüsselung des Index ist byte-identisch
(0 Abweichungen über 26.304 + 15.520 Bytes).

Offen: vollständiger Klick-Durchlauf der Oberfläche.
