#include <SDL/SDL.h>
#ifdef __APPLE__
#include <OpenGL/gl.h>
#else
#include <GL/gl.h>
#endif
#include <stdio.h>
#include <malloc.h>

#include "upng.h"

static GLuint checkboard(unsigned w, unsigned h) {
	unsigned char* buffer;
	unsigned x, y, xc = 0;
	char dark = 0;
	GLuint texture;

	buffer = (unsigned char*)calloc(w * h, 3);

	for (y = 0; y != h; ++y) {
		for (x = 0; x != w; ++x, ++xc) {
			if ((xc % (w >> 3)) == 0) {
				dark = 1 - dark;
			}

			if (dark) {
				buffer[y * w * 3 + x * 3 + 0] = 0x6F;
				buffer[y * w * 3 + x * 3 + 1] = 0x6F;
				buffer[y * w * 3 + x * 3 + 2] = 0x6F;
			} else {
				buffer[y * w * 3 + x * 3 + 0] = 0xAF;
				buffer[y * w * 3 + x * 3 + 1] = 0xAF;
				buffer[y * w * 3 + x * 3 + 2] = 0xAF;
			}
		}

		if ((y % (h >> 3)) == 0) {
			dark = 1 - dark;
		}
	}

	glEnable(GL_TEXTURE_2D);
	glGenTextures(1, &texture);
	glBindTexture(GL_TEXTURE_2D, texture);
	glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
	glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
	glTexImage2D(GL_TEXTURE_2D, 0, 3, w, h, 0, GL_RGB, GL_UNSIGNED_BYTE, buffer);

	free(buffer);

	return texture;
}

int main(int argc, char** argv) {
	SDL_Event event;
	upng_t* upng;
	GLuint texture, cb;

	if (argc <= 1) {
		return 0;
	}

	upng = upng_new_from_file(argv[1]);
	upng_decode(upng);
	if (upng_get_error(upng) != UPNG_EOK) {
		printf("error: %u %u\n", upng_get_error(upng), upng_get_error_line(upng));
		return 0;
	}

	SDL_Init(SDL_INIT_VIDEO);
	SDL_SetVideoMode(upng_get_width(upng), upng_get_height(upng), 0, SDL_OPENGL|SDL_DOUBLEBUF);

	glDisable(GL_DEPTH_TEST);
	glDisable(GL_CULL_FACE);
	glEnable(GL_BLEND);
	glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
	glClearColor(0.f, 0.f, 0.f, 0.f);

	glMatrixMode(GL_PROJECTION);
	glLoadIdentity();
	glOrtho(0, 1, 0, 1, 0, 1);

	glMatrixMode(GL_MODELVIEW);
	glLoadIdentity();

	cb = checkboard(upng_get_width(upng), upng_get_height(upng));

	glEnable(GL_TEXTURE_2D);
	glGenTextures(1, &texture);
	glBindTexture(GL_TEXTURE_2D, texture);
	glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
	glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);

	switch (upng_get_components(upng)) {
	case 1:
		glTexImage2D(GL_TEXTURE_2D, 0, GL_LUMINANCE, upng_get_width(upng), upng_get_height(upng), 0, GL_LUMINANCE, GL_UNSIGNED_BYTE, upng_get_buffer(upng));
		break;
	case 2:
		glTexImage2D(GL_TEXTURE_2D, 0, GL_LUMINANCE_ALPHA, upng_get_width(upng), upng_get_height(upng), 0, GL_LUMINANCE_ALPHA, GL_UNSIGNED_BYTE, upng_get_buffer(upng));
		break;
	case 3:
		glTexImage2D(GL_TEXTURE_2D, 0, GL_RGB, upng_get_width(upng), upng_get_height(upng), 0, GL_RGB, GL_UNSIGNED_BYTE, upng_get_buffer(upng));
		break;
	case 4:
		glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, upng_get_width(upng), upng_get_height(upng), 0, GL_RGBA, GL_UNSIGNED_BYTE, upng_get_buffer(upng));
		break;
	default:
		return 1;
	}

	upng_free(upng);

	while (SDL_WaitEvent(&event)) {
		if (event.type == SDL_QUIT) {
			break;
		}

		glClear(GL_COLOR_BUFFER_BIT);

		glBindTexture(GL_TEXTURE_2D, cb);
		glBegin(GL_QUADS);
			glTexCoord2f(0.f, 1.f);
			glVertex2f(0.f, 0.f);

			glTexCoord2f(0.f, 0.f);
			glVertex2f(0.f, 1.f);

			glTexCoord2f(1.f, 0.f);
			glVertex2f(1.f, 1.f);

			glTexCoord2f(1.f, 1.f);
			glVertex2f(1.f, 0.f);
		glEnd();

		glBindTexture(GL_TEXTURE_2D, texture);
		glBegin(GL_QUADS);
			glTexCoord2f(0.f, 1.f);
			glVertex2f(0.f, 0.f);

			glTexCoord2f(0.f, 0.f);
			glVertex2f(0.f, 1.f);

			glTexCoord2f(1.f, 0.f);
			glVertex2f(1.f, 1.f);

			glTexCoord2f(1.f, 1.f);
			glVertex2f(1.f, 0.f);
		glEnd();

		SDL_GL_SwapBuffers();
	}

	glDeleteTextures(1, &texture);
	glDeleteTextures(1, &cb);
	SDL_Quit();
	return 0;
}
