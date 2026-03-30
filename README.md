# HackBGRT

HackBGRT is intended as a boot logo changer for UEFI-based Windows systems.

## Summary

When booting on a UEFI-based computer, Windows may show a vendor-defined logo which is stored on the UEFI firmware in a section called Boot Graphics Resource Table (BGRT). It's usually very difficult to change the image permanently, but a custom UEFI application may be used to overwrite it during the boot. HackBGRT does exactly that.

**Note:** The original logo is often visible for a moment before HackBGRT is started. This is expected, please do not report this "bug". This can't be changed without modifying computer firmware, which this project will not do.

## Usage

**Important:** If you mess up the installation, your system may become unbootable! Create a rescue disk before use. This software comes with no warranty. Use at your own risk.

### Secure Boot

HackBGRT is not approved by Microsoft. Instead, HackBGRT comes with the *shim* boot loader, which allows to manually select HackBGRT as a trusted program. After installing HackBGRT and rebooting your computer, you have to **follow the instructions in [shim.md](shim.md)** to achieve this. These steps cannot be automated, that's the whole point of Secure Boot. Although HackBGRT is self-signed with a certificate, it's not advisable to enroll foreign certificates directly into your firmware.

The *shim* boot loader is maintained by Red Hat, Inc, and the included signed copy of *shim* is extracted from Debian GNU/Linux – many thanks to the maintainers! For copyright information, see [shim-signed/COPYRIGHT](shim-signed/COPYRIGHT).

### TPM

TPM, or Trusted Platform Module, watches how your computer boots. When there is a change in the boot process – such as using HackBGRT – some things may stop working. This includes:

* BitLocker or similar disk encryption.
* Anti-cheat software.
* Windows PIN unlock method.
* Other security-related things which mention TPM.

You should disable these features before using HackBGRT. Some of them can be re-enabled afterwards, some can't. For any TPM problems, it's recommended to either uninstall HackBGRT or stop using the problematic feature.

(If you find an easy way to reconfigure the TPM, please document, test and share it. Technically one solution would be to install HackBGRT before Windows and make sure that Windows is always booted through HackBGRT. But that's easier said than done.)

### Windows installation

* Make sure you **read the above sections**.
* Get the latest release from the Releases page.
* Make sure you have only one bootable hard drive. Otherwise the automatic setup may fail.
* Start `setup.exe` and follow the instructions.
	* The installer will launch Paint for editing the image, or you can edit it otherwise.
	* For advanced settings, edit `config.txt` before installing. No extra support provided!
	* Read the instructions in [shim.md](shim.md).
	* Check the common [troubleshooting](#troubleshooting) to be prepared.
	* Reboot your computer.
* If Windows later restores the original boot loader, just reinstall.
* If you wish to change the image or configuration later, choose the option to only install files.

### Quiet (batch) installation

* Edit the `config.txt` and `splash.bmp` (or any other images) to your needs.
* Run `setup.exe batch COMMANDS` as administrator, with some of the following commands:
	* `install` – copy the files but don't enable.
	* `enable-bcdedit` – use `bcdedit` to create a new EFI boot entry.
	* `disable-bcdedit` – use `bcdedit` to disable the EFI boot entry.
	* `enable-entry` – write NVRAM to create a new EFI boot entry.
	* `disable-entry` – write NVRAM to disable the EFI boot entry.
	* `enable-overwrite` – overwrite the MS boot loader.
	* `disable-overwrite` – restore the MS boot loader.
	* `skip-shim` – skip *shim* when installing.
	* `allow-secure-boot` – ignore Secure Boot in subsequent commands.
	* `allow-bitlocker` – ignore BitLocker in subsequent commands.
	* `allow-bad-loader` – ignore bad boot loader configuration in subsequent commands.
	* `disable` – run all relevant `disable-*` commands.
	* `uninstall` – disable and remove completely.
	* `show-boot-log` – show the debug log collected during boot (if `log=1` is set in `config.txt`).
	* `arch=...` – force architecture.
	* `esp=...` – force EFI System Partition path.
	* `dry-run` – skip actual changes.
* For example, `setup.exe batch disable install enable-bcdedit` would disable any previous installation, then install the files and create the EFI boot entry with `bcdedit`.

### Multi-boot configurations

If you only need HackBGRT for Windows:

* Run `setup.exe`, install files without enabling.
* Configure your boot loader to start `\EFI\HackBGRT\loader.efi`.

If you need it for other systems as well:

* Configure HackBGRT to start your boot loader (such as systemd-boot): `boot=\EFI\systemd\systemd-bootx64.efi`.
* Run `setup.exe`, install as a new EFI boot entry.

To install purely on Linux, you can install with `setup.exe dry-run` and then manually copy files from `dry-run/EFI` to your `[EFI System Partition]/EFI`. For further instructions, consult the documentation of your own Linux system.

HackBGRT tries to read its configuration from the same directory where it's installed, so you can even make (manually) multiple installations in different directories.

## Configuration

The configuration options are described in `config.txt`, which the installer copies into `[EFI System Partition]\EFI\HackBGRT\config.txt`. For debugging purposes, the same options may be given also as command line parameters in the EFI Shell.

## Images

If you only need one image, just edit `splash.bmp` to your needs.

Advanced users may edit the `config.txt` to define multiple images, in which case one is picked at random. The installer copies and converts the images. For example, to use a file named `my.jpg`, copy it in the installer folder (same folder as `setup.exe`) and set the image path in `config.txt` to `path=my.jpg` before running the installer.

If you copy an image file to ESP manually, note that the image must be a 24-bit BMP file with a 54-byte header. That's a TrueColor BMP3 in Imagemagick, or 24-bit BMP/DIB in Microsoft Paint.

## Troubleshooting

### BCDEdit failed

You can first try the other installation option in the menu. If it doesn't work either, your computer might have a problem. Open Command Prompt and figure out why `bcdedit /enum firmware` fails. In some cases, disabling antivirus, checking the hard disk or searching for 'how to fix 0x800703EE' may help.

### Verification failed, Security violation

This is part of the setup on first boot. Make sure you have read and understood [shim.md](shim.md).

### Boot is slow, boot is stuck, just spinning

Sometimes the first boot is very slow (multiple minutes) for an unknown reason. Wait patiently until you get into Windows. Try to reboot at least a few times to see if it gets any better. It it does not, there's not much else to do than give up.

### Image is not visible, "nothing happens"

Run the setup again and select the option to check the boot log, marked with `BOOT LOG START` in the log file. Continue troubleshooting according to the log contents:

#### Boot log is empty

If the log is empty, then HackBGRT is not in use. Many computers now have a security feature which causes this problem: the computer prevents enabling HackBGRT automatically, instead it resets a certain setting (BootOrder) on reboot and skips the newly-installed HackBGRT.

You have to fix this manually. (After all, the security feature is specifically designed to prevent automatic changes.)

1. Run the setup again.
2. Select the option "boot to UEFI setup".
3. After a reboot, you should get into your computer's own setup utility (UEFI or Firmware settings, or so-called "BIOS").
4. Find boot options and the list of boot entries.
5. Select HackBGRT as the default boot entry (before Windows Boot Loader).

The setup utility is different for each computer and manufacturer, so search online for "[computer model] UEFI setup" or "firmware setup" for images and instructions.

Some people report that HackBGRT is not visible in the computer settings. That's unfortunately a problem with your computer, and you should ask your computer manufacturer how to edit boot entries inside your computer settings. HackBGRT needs to boot `\EFI\HackBGRT\loader.efi`.

If all else fails and you are sure about your computer skills, you can try the legacy installation method. The method bypasses this particular problem but may cause very serious problems if configured incorrectly.

#### Boot log is not empty

If the log shows that HackBGRT has been run during boot, the problem is usually in your configuration file or image. Try to reinstall HackBGRT with the default configuration and image.

If the default logo works, try again with your custom image. Make sure that the image has a reasonable size and position so that it fits the resolution which HackBGRT reports during boot. The resolution may be lower than your desktop resolution.

When you get your image working with the default configuration, you can do any other necessary changes to `config.txt`.

If the default logo does not work, check the boot log again to see if there is some obvious error.
You may report an issue and attach the `setup.log` file.

### Impossible to boot at all

If you used the default installation method, then your Windows boot loader is still in place and you should be able to access UEFI Setup ("BIOS setup") or boot loader list by some key combination right after powering on your computer. There you can choose the `Windows Boot Loader` and continue as usual to uninstall HackBGRT.

If you selected the legacy installation method which overwrites Windows boot loader, then you need to use the Windows installation disk (or recovery disk) to fix boot issues.

## Building

* Compiler: Clang
* Compiler flags: see Makefile
* Libraries: gnu-efi
