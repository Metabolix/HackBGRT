#pragma once

#include "efi.h"

/**
 * Possible actions to perform on the BGRT.
 */
enum HackBGRT_action {
	HackBGRT_ACTION_KEEP = 0,
	HackBGRT_ACTION_REPLACE,
	HackBGRT_ACTION_REMOVE
};

/**
 * Special values for the image coordinates.
 * @see struct HackBGRT_config
 */
enum HackBGRT_coordinate_mode {
	HackBGRT_COORDINATE_MODE_KEEP = 0,
	HackBGRT_COORDINATE_MODE_CENTERED,
	HackBGRT_COORDINATE_MODE_FRACTION,
};

/**
 * Constants for the fractional coordinates.
 */
enum HackBGRT_fraction {
	HackBGRT_FRACTION_DIGITS = 4,
	HackBGRT_FRACTION_ONE = 10000,
	HackBGRT_FRACTION_HALF = 5000,
	HackBGRT_FRACTION_381966011 = 3820,
};

/**
 * Possible values for the orientation.
 */
enum HackBGRT_orientation {
	HackBGRT_ORIENTATION_KEEP = -1,
};

/**
 * The configuration for one image.
 */
struct HackBGRT_image_config {
	enum HackBGRT_action action;
	const CHAR16* path;
	enum HackBGRT_coordinate_mode x_mode, y_mode;
	int x, y;
	int orientation;
};

/**
 * The configuration.
 */
struct HackBGRT_config {
	int debug, log;
	struct HackBGRT_image_config image;
	int image_weight_sum;
	int resolution_x;
	int resolution_y;
	int old_resolution_x;
	int old_resolution_y;
	const CHAR16* boot_path;
};

/**
 * Read a configuration parameter. (May recursively read config files.)
 *
 * @param config The configuration to modify.
 * @param base_dir The base directory, in case the parameter contains an include.
 * @param line The configuration line to parse.
 */
extern void ReadConfigLine(struct HackBGRT_config* config, EFI_FILE_HANDLE base_dir, const CHAR16* line);

/**
 * Read a configuration file. (May recursively read more files.)
 *
 * @param config The configuration to modify.
 * @param base_dir The base directory.
 * @param path The path to the file.
 * @return FALSE, if the file couldn't be read, TRUE otherwise.
 */
extern BOOLEAN ReadConfigFile(struct HackBGRT_config* config, EFI_FILE_HANDLE base_dir, const CHAR16* path);
