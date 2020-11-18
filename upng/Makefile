UNAME := $(shell uname)
ifeq ($(UNAME), Darwin)
	CFLAGS := -framework OpenGL
else
	CFLAGS := -lgl
endif

all: png2tga glview

png2tga: png2tga.c upng.c upng.h Makefile
	$(CC) -o png2tga png2tga.c upng.c -Wall -pedantic -g -O0

glview: glview.c upng.c upng.h Makefile
	$(CC) -o glview glview.c upng.c -Wall -pedantic -g -O3 -flto -lSDL $(CFLAGS)
