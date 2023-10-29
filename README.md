# FREE WING mod version 2020/11 to 2023/10
2023/10 [Merge master Metabolix Update change log and tag v2.1.0 da9909b #7](https://github.com/FREEWING-JP/HackBGRT/pull/7)  

## HackBGRT_MULTI
* Support PNG format image file  
  (But not Support Interlaced image)  
* Support JPEG format image file  
  (But not Support Progressive image)  
* Ofcourse also Support BMP format (^_^)  

### Support PNG format image file using uPNG library .
https://github.com/elanthis/upng  

These formats supported:  
  8-bit RGB, RGBA  
  16-bit RGB, RGBA  
  1,2,4,8-bit RGB INDEX Color Palette  
  1,2,4,8-bit Greyscale  
  8-bit Greyscale w/ 8-bit Alpha  
<img src="https://raw.githubusercontent.com/FREEWING-JP/HackBGRT/feature/mod_multi/upng.jpg" alt="HackBGRT_MULTI Support PNG format image file using uPNG library ." title="HackBGRT_MULTI Support PNG format image file using uPNG library ." width="320" height="240">

### Support JPEG format image file using picojpeg library .
https://code.google.com/archive/p/picojpeg/  
https://github.com/richgel999/picojpeg  

These formats supported:  
 SOF0 Baseline format only(not Support Progressive)  
<img src="https://raw.githubusercontent.com/FREEWING-JP/HackBGRT/feature/mod_multi/HackBGRT_MULTI_1280px-Burosch_Blue-only_Test_pattern_mit_erklaerung.jpg" alt="HackBGRT_MULTI Support JPEG format image file using picojpeg library ." title="HackBGRT_MULTI Support JPEG format image file using picojpeg library ." width="320" height="240">  
https://commons.wikimedia.org/wiki/File:Burosch_Blue-only_Test_pattern_mit_erklaerung.jpg  
1,280 × 720 pixels

## How to build HackBGRT.efi using Windows 10 WSL Debian
* Windows_WSL_Debian_1st.txt
* Windows_WSL_Debian_2nd.txt
* Windows_WSL_Debian_3rd.txt
* Install.txt

Caution:  
Build only the 64-bit version .  
Because I don't know how to write a Makefile script .  

Reference:  
UEFIアプリケーション開発環境を Windowsの WSL環境で構築して QEMU環境で動作確認する方法  
http://www.neko.ne.jp/~freewing/software/uefi_bios_hack/  

---
## Convert Progressive JPEG to Baseline JPEG ?
Converting Progressive JPEG Image file to Baseline JPEG Image file  
https://github.com/FREEWING-JP/CheckAndConvertJpegFile  

---
## Can't Disable Secure Boot ?
Super UEFIinSecureBoot Disk  
https://github.com/ValdikSS/Super-UEFIinSecureBoot-Disk  

rename HackBGRT_MULTI_x86_64.efi to grubx64_real.efi

---
# HackBGRT

HackBGRT is intended as a boot logo changer for UEFI-based Windows systems.

## Summary

When booting on a UEFI-based computer, Windows may show a vendor-defined logo which is stored on the UEFI firmware in a section called Boot Graphics Resource Table (BGRT). It's usually very difficult to change the image permanently, but a custom UEFI application may be used to overwrite it during the boot. HackBGRT does exactly that.

## Usage

**Important:** If you mess up the installation, your system may become unbootable! Create a rescue disk before use. This software comes with no warranty. Use at your own risk.

* Make sure that your computer is booting with UEFI.
* Make sure that Secure Boot is disabled, unless you know how to sign EFI applications.
* Make sure that BitLocker is disabled, or find your recovery key.

### Windows installation

* Get the latest release from the Releases page.
* Start `setup.exe` and follow the instructions.
	* You may need to manually disable Secure Boot and then retry.
	* The installer will launch Paint for editing the image.
	* If Windows later restores the original boot loader, just reinstall.
	* If you wish to change the image or other configuration, just reinstall.
	* For advanced settings, edit `config.txt` before installing. No extra support provided!

### Quiet (batch) installation

* Edit the `config.txt` and `splash.bmp` (or any other images) to your needs.
* Run `setup.exe batch COMMANDS` as administrator, with some of the following commands:
	* `install` – copy the files but don't enable.
	* `enable-entry` – create a new EFI boot entry.
	* `disable-entry` – disable the EFI boot entry.
	* `enable-bcdedit` – use `bcdedit` to create a new EFI boot entry.
	* `disable-bootmgr` – use `bcdedit` to disable the EFI boot entry.
	* `enable-overwrite` – overwrite the MS boot loader.
	* `disable-overwrite` – restore the MS boot loader.
	* `allow-secure-boot` – ignore Secure Boot in subsequent commands.
	* `allow-bitlocker` – ignore BitLocker in subsequent commands.
	* `allow-bad-loader` – ignore bad boot loader configuration in subsequent commands.
	* `disable` – run all relevant `disable-*` commands.
	* `uninstall` – disable and remove completely.
* For example, run `setup.exe batch install allow-secure-boot enable-overwrite` to copy files and overwrite the MS boot loader regardless of Secure Boot status.

### Multi-boot configurations

If you only need HackBGRT for Windows:

* Run `setup.exe`, install files without enabling.
* Configure your boot loader to start `\EFI\HackBGRT\loader.efi`.

If you need it for other systems as well:

* Configure HackBGRT to start your boot loader (such as systemd-boot): `boot=\EFI\systemd\systemd-bootx64.efi`.
* Run `setup.exe`, install as a new EFI boot entry.

To install purely on Linux, you can install with `setup.exe dry-run` and then manually copy files from `dry-run/EFI` to your `[EFI System Partition]/EFI`. For further instructions, consult the documentation of your own Linux system.

## Configuration

The configuration options are described in `config.txt`, which the installer copies into `[EFI System Partition]\EFI\HackBGRT\config.txt`.

## Images

The image path can be changed in the configuration file. The default path is `[EFI System Partition]\EFI\HackBGRT\splash.bmp`.

The installer copies and converts files whose `path` starts with `\EFI\HackBGRT\`. For example, to use a file named `my.jpg`, copy it in the installer folder (same folder as `setup.exe`) and set the image path in `config.txt` to `path=\EFI\HackBGFT\my.jpg`.

If you copy an image file to ESP manually, note that the image must be a 24-bit BMP file with a 54-byte header. That's a TrueColor BMP3 in Imagemagick, or 24-bit BMP/DIB in Microsoft Paint.

Advanced users may edit the `config.txt` to define multiple images, in which case one is picked at random.

## Recovery

If something breaks and you can't boot to Windows, you need to use the Windows installation disk (or recovery disk) to fix boot issues.

## Building

* Compiler: GCC targeting w64-mingw32
* Compiler flags: see Makefile
* Libraries: gnu-efi
