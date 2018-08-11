# Change Log

All notable changes to this project will be documented in this file.

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
