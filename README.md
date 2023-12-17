# HackBGRT

HackBGRT is intended as a boot logo changer for UEFI-based Windows systems.

## Summary

When booting on a UEFI-based computer, Windows may show a vendor-defined logo which is stored on the UEFI firmware in a section called Boot Graphics Resource Table (BGRT). It's usually very difficult to change the image permanently, but a custom UEFI application may be used to overwrite it during the boot. HackBGRT does exactly that.

**Note:** The original logo is often visible for a moment before HackBGRT is started. This is expected, please do not report this "bug". This can't be changed without modifying computer firmware, which this project will not do.

## Usage

**Important:** If you mess up the installation, your system may become unbootable! Create a rescue disk before use. This software comes with no warranty. Use at your own risk.

* Make sure that your computer is booting with UEFI.
* Make sure that you have read the Secure Boot instructions.
* Make sure that BitLocker is disabled, or find your recovery key.

### Secure Boot instructions

HackBGRT is not approved by Microsoft. Instead, HackBGRT comes with the *shim* boot loader, which allows to manually select HackBGRT as a trusted program. After installing HackBGRT and rebooting your computer, you have to **follow the instructions in [shim.md](shim.md)** to achieve this. These steps cannot be automated, that's the whole point of Secure Boot. Although HackBGRT is self-signed with a certificate, it's not advisable to enroll foreign certificates directly into your firmware.

The *shim* boot loader is maintained by Red Hat, Inc, and the included signed copy of *shim* is extracted from Debian GNU/Linux – many thanks to the maintainers! For copyright information, see [shim-signed/COPYRIGHT](shim-signed/COPYRIGHT).

### Windows installation

* Get the latest release from the Releases page.
* Start `setup.exe` and follow the instructions.
	* The installer will launch Paint for editing the image.
	* If Windows later restores the original boot loader, just reinstall.
	* If you wish to change the image or other configuration, just reinstall.
	* For advanced settings, edit `config.txt` before installing. No extra support provided!
	* After installing, read the instructions in [shim.md](shim.md) and reboot your computer.

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
	* `skip-shim` – skip *shim* when installing.
	* `allow-secure-boot` – ignore Secure Boot in subsequent commands.
	* `allow-bitlocker` – ignore BitLocker in subsequent commands.
	* `allow-bad-loader` – ignore bad boot loader configuration in subsequent commands.
	* `disable` – run all relevant `disable-*` commands.
	* `uninstall` – disable and remove completely.
	* `show-boot-log` – show the debug log collected during boot (if `log=1` is set in `config.txt`).
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

If you only need one image, just edit `splash.bmp` to your needs.

Advanced users may edit the `config.txt` to define multiple images, in which case one is picked at random. The installer copies and converts the images. For example, to use a file named `my.jpg`, copy it in the installer folder (same folder as `setup.exe`) and set the image path in `config.txt` to `path=my.jpg` before running the installer.

If you copy an image file to ESP manually, note that the image must be a 24-bit BMP file with a 54-byte header. That's a TrueColor BMP3 in Imagemagick, or 24-bit BMP/DIB in Microsoft Paint.

## Recovery

If something breaks and you can't boot to Windows, you need to use the Windows installation disk (or recovery disk) to fix boot issues.

## Building

* Compiler: Clang
* Compiler flags: see Makefile
* Libraries: gnu-efi
