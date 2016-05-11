# HackBGRT

HackBGRT is intended as a boot logo changer for UEFI-based Windows systems.

## Summary

When booting on a UEFI-based computer, Windows may show a vendor-defined logo which is stored on the UEFI firmware in a section called Boot Graphics Resource Table (BGRT). It's usually very difficult to change the image permamently, but a custom UEFI application may be used to overwrite it during the boot. HackBGRT does exactly that.

## Usage

**Important:** If you mess up the installation, your system may become unbootable! Create a rescue disk before use. This software comes with no warranty. Use at your own risk.

* Make sure that your computer is booting with UEFI.
* Make sure that you have a 64-bit x86-64 processor.
* Make sure that Secure Boot is disabled, or learn to sign EFI applications.
* Simple Windows installation:
	* Get at least these files: `bootx64.efi`, `config.txt`, `install.bat`, `splash.bmp`.
	* Run Command Prompt as Administrator.
	* Run `install.bat` from the Command Prompt.
		* The installer will launch Paint for creating the image(s).
		* The installer will launch Notepad for modifying the configuration.
	* If Windows later reinstalls the original boot loader, run `install.bat` again.
* Installation for Windows with another boot loader (e.g. GRUB):
	* Copy the mentioned files to `[EFI System Partition]\EFI\HackBGRT\`.
	* Set `boot=\EFI\Microsoft\Boot\bootmgfw.efi` in `config.txt`.
	* Point your boot loader to `\EFI\HackBGRT\bootx64.efi`.
* Installation for all operating systems:
	* Copy the mentioned files to `[EFI System Partition]\EFI\HackBGRT\`.
	* Set `boot=` to your preferred boot loader in `config.txt`.
	* Set `\EFI\HackBGRT\bootx64.efi` as your default boot loader with `efibootmgr` or some other EFI boot manager tool.

## Configuration

The configuration options are described in `config.txt`, which should be stored in `[EFI System Partition]\EFI\HackBGRT\config.txt`.

## Images

The image path can be changed in the configuration file. The default path is `[EFI System Partition]\EFI\HackBGRT\splash.bmp`.

The image must be a 24-bit BMP file with a 54-byte header. That's a TrueColor BMP3 in Imagemagick, or 24-bit BMP/DIB in Microsoft Paint.

Multiple images may be specified, in which case one is picked at random.

## Building

* Compiler: GCC targeting w64-mingw32
* Compiler flags: see Makefile
* Libraries: gnu-efi
