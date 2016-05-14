#pragma once

#include <efi.h>

/**
 * Possible actions to perform on the BGRT.
 */
enum HackBGRT_action {
	HackBGRT_KEEP = 0, HackBGRT_REPLACE, HackBGRT_REMOVE
};

/**
 * Special values for the image coordinates.
 * @see struct HackBGRT_config
 */
enum HackBGRT_coordinate {
	HackBGRT_coord_auto = 0x10000001,
	HackBGRT_coord_native = 0x10000002
};

/**
 * The configuration.
 */
struct HackBGRT_config {
	int debug;
	enum HackBGRT_action action;
	const CHAR16* image_path;
	int image_x;
	int image_y;
	int image_weight_sum;
	const CHAR16* boot_path;
};

/**
 * Read a configuration parameter. (May recursively read config files.)
 *
 * @param config The configuration to modify.
 * @param root_dir The root directory, in case the parameter contains an include.
 * @param line The configuration line to parse.
 */
extern void ReadConfigLine(struct HackBGRT_config* config, EFI_FILE_HANDLE root_dir, const CHAR16* line);

/**
 * Read a configuration file. (May recursively read more files.)
 *
 * @param config The configuration to modify.
 * @param root_dir The root directory.
 * @param path The path to the file.
 * @return FALSE, if the file couldn't be read, TRUE otherwise.
 */
extern BOOLEAN ReadConfigFile(struct HackBGRT_config* config, EFI_FILE_HANDLE root_dir, const CHAR16* path);
