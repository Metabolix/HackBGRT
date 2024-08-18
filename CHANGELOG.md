# Change Log

All notable changes to this project will be documented in this file.

## 2.5.1 - 2024-08-18

### Changed
- Update *shim* to 15.8.

## 2.5.0 - 2024-06-21

### Changed
- Properly handle skip-shim with enable-overwrite.
- Improve instructions (documentation).
- Improve error reporting and logging.

## 2.4.1 - 2024-04-11

### Fixed
- Report better if BCDEdit is unable to operate.
- Improve support for non-BCDEdit boot entries.
- Remove old version before copying any new files.

## 2.4.0 - 2023-12-31

### Fixed
- Fix BCDEdit boot entries to avoid *shim* error messages.
- Combine BCDEdit and own code to create boot entries more reliably.

### Changed
- Clear the screen to wipe the vendor logo as soon as possible.
- Image paths in `config.txt` may be relative (just file names).

## 2.3.1 - 2023-11-27

### Fixed
- BitLocker detection is more reliable.

## 2.3.0 - 2023-11-27

### Added
- AArch64 and ARM builds, and *shim* for AArch64.

### Fixed
- Boot entry is more reliable, avoids conflicts with firmware entries.

## 2.2.0 - 2023-11-17

### Added
- Support Secure Boot with *shim* boot loader.
- Gather debug log during boot and read it with setup.exe.

## 2.1.0 - 2023-10-04

### Added
- Check image size, crop if it's bigger than the screen.
- Check BitLocker status to avoid unbootable machine.

## 2.0.0 - 2023-09-10

### Added
- Log to `setup.log`.
- Image conversion (GIF, EXIF, JPG, PNG, TIFF) to BMP during setup.
- Quiet (batch) setup.
- Dry run in setup.
- EFI boot entry support in setup.
- Orientation parameter (o=0|90|180|270) for images.

### Changed
- Configure (edit config and images) before installing.
- Escalate privileges only when needed (after the menu).
- Try to detect and avoid some configuration errors.
- Wait at most 15 seconds for key presses during boot.
- Image coordinates are now relative to the center.

## 1.5.1 - 2018-08-11

### Fixed
- Clarify the default config.txt.
- Fix an exception in some cases when trying to boot to UEFI setup.

## 1.5.0 - 2017-09-30

### Added
- Support for rebooting to UEFI setup.

### Changed
- Minor enhancements to installer.

## 1.4.0 - 2017-08-29

### Added
- Use UTF-8 in the configuration file.
- Use the default boot loader path if the configured one doesn't work.

## 1.3.0 - 2016-12-22

### Added
- Check Secure Boot status before installing.

## 1.2.0 - 2016-06-05

### Added
- Better installer, setup.exe.
- Support for low-end machines with 32-bit IA-32 UEFI.
- Support for changing resolution.
- Version information in the program.
- Change log.

### Removed
- Removed old install scripts, install.bat and uninstall.bat.

## 1.1.0 - 2016-05-14

### Changed
- Wait for input before booting if debug=1 is set.

### Fixed
- Fix handling of multiple BGRT entries.
- Fix ACPI table checksums.

## 1.0.0 - 2016-05-11

### Added
- Easy-to-use installation script.
- Git repository for the project.

## 0.2.0 - 2016-04-26

### Added
- Support for randomly alternating images.
- Support for black background.
- Support for the native Windows logo.

### Changed
- New configuration file format for images.

## 0.1.0 - 2016-01-15

### Added
- Support for loading a bitmap and updating the BGRT.
- Support for loading the next boot loader.
- Support for a configuration file.
