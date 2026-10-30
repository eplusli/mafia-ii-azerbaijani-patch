# Mafia II — Azərbaycan dili yaması

Mafia II Classic üçün gündəlik Azərbaycan dilində hazırlanmış qeyri-rəsmi mətn yaması. Menyular, tapşırıqlar, təlimatlar və dialoq altyazıları daxildir. Əə, Ğğ, Şş, İı və digər Azərbaycan hərfləri dəstəklənir. Səslər, DLC-lər və rəsmlər dəyişdirilmir.

## Yüklə və quraşdır

**[Windows quraşdırıcısını yüklə](https://github.com/eplusli/mafia-ii-azerbaijani-patch/releases/latest)**

1. Oyunu bağla və quraşdırıcını aç.
2. **Select mafia2.exe...** ilə oyunun `pc` qovluğundakı `mafia2.exe` faylını seç. **Select folder...** ilə əsas oyun qovluğunu da seçə bilərsən.
3. **Install Azerbaijani patch** düyməsinə bas. Quraşdırıcı uyğunluğu yoxlayacaq və orijinal faylların ehtiyat nüsxəsini saxlayacaq.
4. Oyunun dilini **English** saxla. Mətnlər Azərbaycanca görünəcək, səslər əvvəlki kimi qalacaq.

İngilis mətnlərini geri qaytarmaq üçün eyni quraşdırıcıda oyunu seç və **Restore original English** düyməsinə bas. Oyun qovluğundakı `MafiaII.Azerbaijani.Backup` qovluğunu silmə.

## English

An offline Windows installer for the Azerbaijani text patch for **Mafia II Classic PC**. It covers all 10,166 entries in the identified base-game text corpus: menus, objectives, tutorials, character information, chapter text, credits labels, and dialogue subtitles. Proper names, physical key labels, technical character tests, and vocalizations are preserved where appropriate. The game voices are unchanged.

### Install

Download `Mafia-II-Azerbaijani-Patch-Setup-1.0.0.exe` from [Releases](https://github.com/eplusli/mafia-ii-azerbaijani-patch/releases). Close the game, open the installer, select `mafia2.exe` or the game folder, and choose **Install Azerbaijani patch**. Keep the game's language set to **English**.

The installer works offline and does not download or include a copy of the game. It reconstructs translated archives from the player's existing files using small copy/literal deltas. It verifies all 304 required archive checksums before changing game files, stages the patch, and verifies an original backup for every modified archive. Save files are not read or changed.

### Compatibility and restore

- Windows 10/11 with .NET Framework 4.8. No Node.js, Python, or separate installer runtime is needed.
- Mafia II **Classic**, using the English archives that match the checksums in `data/manifest.json`. Other editions/builds and modified archives are rejected before patching. **Definitive Edition is not supported.**
- Only base-game text and the two required font atlases change. DLC and artwork are excluded.
- Allow approximately 1 GB of free space during installation. Staged archives are removed after the operation; original backups remain in `MafiaII.Azerbaijani.Backup/1.0.0/original` under the selected game folder.
- Use **Restore original English** in this same installer to restore verified originals. Backups are kept after restore. Later modifications or corrupted backups are rejected instead of being overwritten.
- If you already applied the earlier manual patch, use its original restore script first. This installer cannot invent an original backup for an already-patched game.
- For a protected game folder, run the installer as administrator only if Windows denies access. For very long game paths, use a shorter folder such as `C:\Games\Mafia II`.

This is an unsigned community executable. Its SHA-256 checksum is published with the release. There is no telemetry, auto-update service, or bundled game executable.

### Validation and limitations

The text corpus has zero missing entries or ID/button/format placeholder mismatches. All 8,272 resources in the 304 rebuilt archives were compared against the prepared patch, preserving every non-text/non-font payload. The installer test suite checks complete installation, backup integrity, repeat installation, interrupted-install recovery, rejection of incompatible/missing files, refusal to overwrite later mods, corrupted backup handling, and byte-for-byte restoration.

The earlier game check successfully displayed Azerbaijani letters in the main menu. A full playthrough and independent native-speaker review remain outstanding. Complete extracted text coverage does not guarantee that every line or layout has been checked in gameplay. Please [report translation or compatibility issues](https://github.com/eplusli/mafia-ii-azerbaijani-patch/issues) with the chapter and a screenshot when possible.

## Build from source

On Windows, run `build.ps1` in PowerShell. The script uses the Windows .NET Framework compiler and embeds `data` into a single executable in `release`. It also writes `SHA256SUMS.txt`. No private game archives or credentials are required to build the installer.

`src` contains the installer and SDS/delta implementation. `data` contains the public patch manifest and delta assets. `translations` contains the Azerbaijani text and source-resource mapping for review; editing text there alone does not regenerate the binary patch data.

To run the integration suite, supply a directory containing compatible original archives under `pc/sds_en` and `pc/sds`:

```powershell
.\tests\Test-Installer.ps1 -OriginalFiles 'D:\PrivateTestData\OriginalFiles' -TestWorkspace 'D:\PatchTests'
```

Tests create their own fixture and never patch the supplied originals or launch the game. Private test inputs and saves must not be committed to this repository.

Installer source code is MIT licensed. Mafia II and its original content/trademarks belong to their respective owners; no ownership of the original game is claimed. The release contains patch data, not game archives. SDS format research used [MafiaToolkit](https://github.com/Greavesy1899/MafiaToolkit).
