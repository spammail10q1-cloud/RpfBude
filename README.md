# RpfBude

Tool for GTA V RPF7 archives. Reads and writes archives that the game only
accepts NG-encrypted in their original form.

## Why this exists

OpenIV rebuilds `update.rpf` on save and corrupts it on this game build
(1.0.3889), which the game reports as `ERR_GEN_INVALID`. RpfBude only touches
the affected block and re-encrypts the index with the NG cipher. The archive
stays NG and remains byte-valid.

## Features

- Open RPF7 archives in `OPEN`, `NG` and `NONE` encryption
- Find the AES key inside `GTA5.exe` automatically
- NG decryption using `magic.dat`
- NG encryption: round A through Gaussian inversion, rounds 2 to 15 through
  lookup tables
- Browse the folder tree and extract single files
- Replace a file inside the archive (re-encrypts NG correctly)
- Add or remove a dlcpack entry in `dlclist.xml`
- Install a dlcpack folder (copies it and writes the entry in one step)
- Build and install `.oiv` packages
- Integrity check: entry counts and data extent against file length

## Layout

```
src/
  Crypto/    key search, NG decrypt, deflate helpers
  Rpf/       archive model, read and write paths
  Oiv/       package build and install
  Ui/        WinForms window and entry point
magic.dat    NG key material, required next to the executable
```

## Tables

The first start builds the encryption tables once (about 3 minutes) and stores
them as `ngenc_a.bin` and `ngenc_b.bin` next to the executable. Later starts
load them.

## Build

```
dotnet build -c Release
```

Target is `net8.0-windows` with WinForms. `magic.dat` has to sit next to the
executable.

## Verified

NG decryption reads the real `update.rpf`: 1644 entries, 102 entries in
`dlclist.xml`. Re-encrypting the index is byte-identical, with 0 differences
over 26,304 and 15,520 bytes.

Still open: a full click-through of the interface.
