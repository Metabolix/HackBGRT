# FREE WING mod version 2020/11

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
<img src="https://raw.githubusercontent.com/FREEWING-JP/HackBGRT/test/add_upng/upng.jpg" alt="HackBGRT_MULTI Support PNG format image file using uPNG library ." title="HackBGRT_MULTI Support PNG format image file using uPNG library ." width="320" height="240">

### Support JPEG format image file using picojpeg library .
https://code.google.com/archive/p/picojpeg/  
https://github.com/richgel999/picojpeg  

These formats supported:  
 SOF0 Baseline format only(not Support Progressive)  
<img src="https://raw.githubusercontent.com/FREEWING-JP/HackBGRT/test/add_picojpeg/HackBGRT_MULTI_1280px-Burosch_Blue-only_Test_pattern_mit_erklaerung.jpg" alt="HackBGRT_MULTI Support JPEG format image file using picojpeg library ." title="HackBGRT_MULTI Support JPEG format image file using picojpeg library ." width="320" height="240">  
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

---
# HackBGRT

HackBGRT is intended as a boot logo changer for UEFI-based Windows systems.

## Summary

When booting on a UEFI-based computer, Windows may show a vendor-defined logo which is stored on the UEFI firmware in a section called Boot Graphics Resource Table (BGRT). It's usually very difficult to change the image permanently, but a custom UEFI application may be used to overwrite it during the boot. HackBGRT does exactly that.

## Usage

**Important:** If you mess up the installation, your system may become unbootable! Create a rescue disk before use. This software comes with no warranty. Use at your own risk.

* Make sure that your computer is booting with UEFI.
* Make sure that Secure Boot is disabled, unless you know how to sign EFI applications.

### Windows installation

* Get the latest release from the Releases page.
* Start `setup.exe` and follow the instructions.
	* You may need to manually disable Secure Boot and then retry.
	* The installer will launch Notepad for modifying the configuration.
		* If you need only one custom image, the defaults are fine.
		* Otherwise, check out the examples in the configuration file.
	* The installer will launch Paint for creating the image(s).
		* You can create multiple images by using Save As.
		* Be sure to always use the 24-bit BMP/DIB format.
	* If Windows later restores the original boot loader, just reinstall.
	* If you wish to change the image or other configuration, just reinstall.

### Multi-boot configurations

If you only need HackBGRT for Windows:

* Extract the latest release to `[EFI System Partition]\EFI\HackBGRT\`.
* Set `boot=\EFI\Microsoft\Boot\bootmgfw.efi` in `config.txt`.
* Point your boot loader to `\EFI\HackBGRT\bootx64.efi`.

If you need it for other systems as well:

* Extract the latest release to `[EFI System Partition]\EFI\HackBGRT\`.
* Set `boot=\EFI\your-actual-boot-loader.efi` in `config.txt`.
* Set `\EFI\HackBGRT\bootx64.efi` as your default boot loader with `efibootmgr` or some other EFI boot manager tool.

On 32-bit machines, use `bootia32.efi` instead of `bootx64.efi`.

## Configuration

The configuration options are described in `config.txt`, which should be stored in `[EFI System Partition]\EFI\HackBGRT\config.txt`.

## Images

The image path can be changed in the configuration file. The default path is `[EFI System Partition]\EFI\HackBGRT\splash.bmp`.

The image must be a 24-bit BMP file with a 54-byte header. That's a TrueColor BMP3 in Imagemagick, or 24-bit BMP/DIB in Microsoft Paint.

Multiple images may be specified, in which case one is picked at random.

## Recovery

If something breaks and you can't boot to Windows, you have the following options:

* Windows installation (or recovery) media can fix boot issues.
* You can copy `[EFI System Partition]\EFI\HackBGRT\bootmgfw-original.efi` into `[EFI System Partition]\EFI\Microsoft\Boot\bootmgfw.efi` by some other means such as Linux or Windows command prompt.

## Building

* Compiler: GCC targeting w64-mingw32
* Compiler flags: see Makefile
* Libraries: gnu-efi
