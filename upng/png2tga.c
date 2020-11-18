#include <stdio.h>
#include <malloc.h>

#include "upng.h"

#define HI(w) (((w) >> 8) & 0xFF)
#define LO(w) ((w) & 0xFF)

int main(int argc, char** argv) {
	FILE* fh;
	upng_t* upng;
	unsigned width, height, depth;
	unsigned x, y, d;

	if (argc <= 2) {
		return 0;
	}

	upng = upng_new_from_file(argv[1]);
	if (upng_get_error(upng) == UPNG_EOK) {
		printf("error: %u %u\n", upng_get_error(upng), upng_get_error_line(upng));
		return 0;
	}

	width = upng_get_width(upng);
	height = upng_get_height(upng);
	depth = upng_get_bpp(upng) / 8;

	printf("size:	%ux%ux%u (%u)\n", width, height, upng_get_bpp(upng), upng_get_size(upng));
	printf("format:	%u\n", upng_get_format(upng));

	if (upng_get_format(upng) == UPNG_RGB8 || upng_get_format(upng) == UPNG_RGBA8) {
		fh = fopen(argv[2], "wb");
		fprintf(fh, "%c%c%c", 0, 0, 2);
		fprintf(fh, "%c%c%c%c%c", 0, 0, 0, 0, 0);
		fprintf(fh, "%c%c%c%c%c%c%c%c%c%c", 0, 0, 0, 0, LO(width), HI(width), LO(height), HI(height), upng_get_bpp(upng), upng_get_bitdepth(upng));

		for (y = 0; y != height; ++y) {
			for (x = 0; x != width; ++x) {
				for (d = 0; d != depth; ++d) {
					putc(upng_get_buffer(upng)[(height - y - 1) * width * depth + x * depth + (depth - d - 1)], fh);
				}
			}
		}

		fclose(fh);
	}

	upng_free(upng);
	return 0;
}
