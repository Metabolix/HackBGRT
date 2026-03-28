#include "config.h"
#include "util.h"

BOOLEAN ReadConfigFile(struct HackBGRT_config* config, EFI_FILE_HANDLE base_dir, const CHAR16* path) {
	void* data = 0;
	UINTN data_bytes = 0;
	data = LoadFileWithPadding(base_dir, path, &data_bytes, 4);
	if (!data) {
		Log(1, L"Failed to load configuration (%s)!\n", path);
		return FALSE;
	}
	CHAR16* str;
	UINTN str_len;
	if (*(CHAR16*)data == 0xfeff) {
		// UCS-2
		str = data;
		str_len = data_bytes / sizeof(*str);
	} else {
		// UTF-8 -> UCS-2
		EFI_STATUS e = BS->AllocatePool(EfiBootServicesData, data_bytes * 2 + 2, (void**)&str);
		if (EFI_ERROR(e)) {
			BS->FreePool(data);
			return FALSE;
		}
		UINT8* str0 = data;
		for (UINTN i = str_len = 0; i < data_bytes;) {
			UINTN unicode = 0xfffd;
			if (str0[i] < 0x80) {
				unicode = str0[i];
				i += 1;
			} else if (str0[i] < 0xc0) {
				i += 1;
			} else if (str0[i] < 0xe0) {
				unicode = ((str0[i] & 0x1f) << 6) | ((str0[i+1] & 0x3f) << 0);
				i += 2;
			} else if (str0[i] < 0xf0) {
				unicode = ((str0[i] & 0x0f) << 12) | ((str0[i+1] & 0x3f) << 6) | ((str0[i+2] & 0x3f) << 0);
				i += 3;
			} else if (str0[i] < 0xf8) {
				unicode = ((str0[i] & 0x07) << 18) | ((str0[i+1] & 0x3f) << 12) | ((str0[i+2] & 0x3f) << 6) | ((str0[i+3] & 0x3f) << 0);
				i += 4;
			} else {
				i += 1;
			}
			if (unicode <= 0xffff) {
				str[str_len++] = unicode;
			} else {
				str[str_len++] = 0xfffd;
			}
		}
		str[str_len] = 0;
		BS->FreePool(data);
	}

	for (int i = 0; i < str_len;) {
		int j = i;
		while (j < str_len && str[j] != '\r' && str[j] != '\n') {
			++j;
		}
		while (j < str_len && (str[j] == '\r' || str[j] == '\n')) {
			str[j] = 0;
			++j;
		}
		ReadConfigLine(config, base_dir, &str[i]);
		i = j;
	}
	// NOTICE: string is not freed, because paths are not copied.
	return TRUE;
}

static void SetBMPWithRandom(struct HackBGRT_config* config, const struct HackBGRT_image_config* image, int weight) {
	config->image_weight_sum += weight;
	UINT32 random = (((UINT64) Random() & 0xffffffff) * config->image_weight_sum) >> 32;
	UINT32 limit = ((UINT64) 0xffffffff * weight) >> 32;
	Log(
		config->debug,
		L"image n=%d, action=%d, x=%d (mode %d), y=%d (mode %d), o=%d, path=%s, rand=%x/%x, chosen=%d\n",
		weight, image->action, image->x, image->x_mode, image->y, image->y_mode, image->orientation, image->path, random, limit, (random <= limit)
	);
	if (random <= limit) {
		config->image = *image;
	}
}

static void ParseCoordinate(const CHAR16* str, int* out_int, enum HackBGRT_coordinate_mode* out_mode) {
	if (!str) {
		return;
	}
	if (StrnCmp(str, L"keep", 4) == 0) {
		*out_int = 0;
		*out_mode = HackBGRT_COORDINATE_MODE_KEEP;
		return;
	}
	if (str[0] == L'.' && L'0' <= str[1] && str[1] <= L'9') {
		int result = 0, i = 1, length = 0;
		for (length = 0; length < HackBGRT_FRACTION_DIGITS; ++length) {
			result = 10 * result;
			if (L'0' <= str[i] && str[i] <= L'9') {
				result += str[i] - L'0';
				i += 1;
			}
		}
		*out_mode = HackBGRT_COORDINATE_MODE_FRACTION;
		*out_int = result;
		return;
	}
	int neg = str[0] == L'-' ? 1 : 0;
	if (L'0' <= str[neg] && str[neg] <= L'9') {
		*out_int = Atoi(str + neg) * (neg ? -1 : 1);
		*out_mode = HackBGRT_COORDINATE_MODE_CENTERED;
		return;
	}
}

static void ReadConfigImage(struct HackBGRT_config* config, const CHAR16* line) {
	struct HackBGRT_image_config image = {
		.action = HackBGRT_ACTION_KEEP,
		.orientation = HackBGRT_ORIENTATION_KEEP,
	};
	const CHAR16* tmp;
	image.path = StrStrAfter(line, L"path=");
	if (image.path) {
		image.action = HackBGRT_ACTION_REPLACE;
		// Default: x centered, y 38.2 % (= 1 - 1 / golden_ratio)
		image.x_mode = image.y_mode = HackBGRT_COORDINATE_MODE_FRACTION;
		image.x = HackBGRT_FRACTION_HALF;
		image.y = HackBGRT_FRACTION_381966011;
	} else if (StrStr(line, L"remove")) {
		image.action = HackBGRT_ACTION_REMOVE;
	} else if (StrStr(line, L"black")) {
		image.action = HackBGRT_ACTION_REPLACE;
		image.path = 0;
	} else if (StrStr(line, L"keep")) {
		image.action = HackBGRT_ACTION_KEEP;
		image.x_mode = image.y_mode = HackBGRT_COORDINATE_MODE_KEEP;
	} else {
		Log(1, L"Invalid image line: %s\n", line);
		return;
	}
	ParseCoordinate(StrStrAfter(line, L"x="), &image.x, &image.x_mode);
	ParseCoordinate(StrStrAfter(line, L"y="), &image.y, &image.y_mode);
	if (StrStrAfter(line, L"o=keep")) {
		image.orientation = HackBGRT_ORIENTATION_KEEP;
	} else if ((tmp = StrStrAfter(line, L"o="))) {
		// convert orientation in degrees to number 0-3 (* 90 degrees)
		int i = tmp[0] == L'-' ? -(int)Atoi(tmp+1) : (int)Atoi(tmp);
		image.orientation = (i / 90) & 3;
	}
	int weight = 1;
	if ((tmp = StrStrAfter(line, L"n="))) {
		weight = Atoi(tmp);
	}
	SetBMPWithRandom(config, &image, weight);
}

static void ReadConfigResolution(struct HackBGRT_config* config, const CHAR16* line) {
	const CHAR16* x = line;
	const CHAR16* y = StrStrAfter(line, L"x");
	if (x && *x && y && *y) {
		config->resolution_x = *x == '-' ? -(int)Atoi(x+1) : (int)Atoi(x);
		config->resolution_y = *y == '-' ? -(int)Atoi(y+1) : (int)Atoi(y);
	} else {
		Log(1, L"Invalid resolution line: %s\n", line);
	}
}

void ReadConfigLine(struct HackBGRT_config* config, EFI_FILE_HANDLE base_dir, const CHAR16* line) {
	line = TrimLeft(line);
	if (line[0] == L'#' || line[0] == 0) {
		return;
	}

	if (StrnCmp(line, L"debug=", 6) == 0) {
		config->debug = (StrCmp(line, L"debug=1") == 0);
		return;
	}
	if (StrnCmp(line, L"log=", 4) == 0) {
		config->log = (StrCmp(line, L"log=1") == 0);
		return;
	}
	if (StrnCmp(line, L"image=", 6) == 0) {
		ReadConfigImage(config, line + 6);
		return;
	}
	if (StrnCmp(line, L"boot=", 5) == 0) {
		config->boot_path = line + 5;
		return;
	}
	if (StrnCmp(line, L"config=", 7) == 0) {
		ReadConfigFile(config, base_dir, line + 7);
		return;
	}
	if (StrnCmp(line, L"resolution=", 11) == 0) {
		ReadConfigResolution(config, line + 11);
		return;
	}
	Log(1, L"Unknown configuration directive: %s\n", line);
}
