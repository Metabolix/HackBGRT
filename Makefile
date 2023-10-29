<<<<<<< HEAD
# for Windows 10 WSL Debian build
# use Normal GCC
CROSS_COMPILE =
# PREFIX=/usr/local/
PREFIX = ./gnu-efi/usr/local/
TARGET = HackBGRT_MULTI_$(ARCH)
_OBJS = main.o config.o types.o util.o
_OBJS += picojpeg.o
_OBJS += upng.o
_OBJS += my_efilib.o
ODIR = obj
SDIR = src
OBJS = $(patsubst %,$(ODIR)/%,$(_OBJS))
ARCH = x86_64

CC  = $(CROSS_COMPILE)gcc
LD  = $(CROSS_COMPILE)ld
OBJCOPY = $(CROSS_COMPILE)objcopy

# Linker script
LDSCRIPT = $(PREFIX)/lib/elf_$(ARCH)_efi.lds
# PE header and startup code
STARTOBJ = $(PREFIX)/lib/crt0-efi-$(ARCH).o
# include header
EFI_INCLUDE = $(PREFIX)/include/efi/
INCLUDES = -I. \
	-I$(EFI_INCLUDE) \
	-I$(EFI_INCLUDE)/$(ARCH) \
	-I$(EFI_INCLUDE)/protocol

# CFLAGS
CFLAGS = -std=c11 -O2 -ffreestanding -mno-red-zone -fno-stack-protector \
	-Wshadow -Wall -Wunused -Werror-implicit-function-declaration \
	-DCONFIG_$(GNUEFI_ARCH) -DGNU_EFI_USE_MS_ABI \
	-fpic -D__KERNEL__ \
	-maccumulate-outgoing-args \
	-fshort-wchar -fno-strict-aliasing \
	-fno-merge-all-constants -fno-stack-check
# -Werror
GIT_DESCRIBE = $(firstword $(shell git describe --tags) unknown)
CFLAGS += '-DGIT_DESCRIBE=L"$(GIT_DESCRIBE)"'

# LDFLAGS
LDFLAGS = -nostdlib --warn-common --no-undefined \
	--fatal-warnings --build-id=sha1 \
	-shared -Bsymbolic
# set EFI_SUBSYSTEM: Application(0x0a)
LDFLAGS += --defsym=EFI_SUBSYSTEM=0x0a
LDFLAGS += -L$(PREFIX)/lib


####### rules #########

all: $(TARGET).efi

# rebuild shared object to PE binary
$(TARGET).efi: $(TARGET).so
	$(OBJCOPY) \
		-j .text  \
		-j .sdata \
		-j .data  \
		-j .dynamic \
		-j .dynsym \
		-j .rel    \
		-j .rela   \
		-j .rel.*  \
		-j .rela.* \
		-j .rel*   \
		-j .rela*  \
		-j .reloc  \
		-O binary  \
		--target efi-app-$(ARCH) \
		$(TARGET).so $@

# build shared object
$(TARGET).so: $(OBJS)
	$(LD) $(LDFLAGS) $(STARTOBJ) $^ -o $@ \
		-lefi -lgnuefi \
		-T $(LDSCRIPT)

./obj/%.o: ./src/%.c
	$(CC) $(CFLAGS) $(INCLUDES) -c $< -o $@

# PNG upng
./obj/%.o: ./upng/%.c
	$(CC) $(CFLAGS) $(INCLUDES) -c $< -o $@

# JPEG picojpeg
./obj/%.o: ./picojpeg/%.c
	$(CC) $(CFLAGS) $(INCLUDES) -c $< -o $@

# my_efilib
./obj/%.o: ./my_efilib/%.c
	$(CC) $(CFLAGS) $(INCLUDES) -c $< -o $@

# clean rule
clean:
	rm -f ./obj/*.o *.so s*.efi
=======
CC      = $(CC_PREFIX)-gcc
CFLAGS  = -std=c11 -O2 -ffreestanding -mno-red-zone -fno-stack-protector -Wshadow -Wall -Wunused -Werror-implicit-function-declaration -Werror
CFLAGS += -I$(GNUEFI_INC) -I$(GNUEFI_INC)/$(GNUEFI_ARCH) -I$(GNUEFI_INC)/protocol
LDFLAGS = -nostdlib -shared -Wl,-dll -Wl,--subsystem,10 -e _EfiMain
LIBS    = -L$(GNUEFI_LIB) -lefi -lgcc

GNUEFI_INC = /usr/$(CC_PREFIX)/include/efi
GNUEFI_LIB = /usr/$(CC_PREFIX)/lib

FILES_C = src/main.c src/util.c src/types.c src/config.c
FILES_H = $(wildcard src/*.h)
FILES_CS = src/Setup.cs src/Esp.cs src/Efi.cs
GIT_DESCRIBE = $(firstword $(shell git describe --tags) unknown)
CFLAGS += '-DGIT_DESCRIBE=L"$(GIT_DESCRIBE)"'
ZIPDIR = HackBGRT-$(GIT_DESCRIBE:v%=%)
ZIP = $(ZIPDIR).zip

all: efi setup zip
efi: bootx64.efi bootia32.efi
setup: setup.exe

zip: $(ZIP)
$(ZIP): bootx64.efi bootia32.efi config.txt splash.bmp setup.exe README.md CHANGELOG.md README.efilib LICENSE
	test ! -d "$(ZIPDIR)"
	mkdir "$(ZIPDIR)"
	cp -a $^ "$(ZIPDIR)" || (rm -rf "$(ZIPDIR)"; exit 1)
	7z a -mx=9 "$(ZIP)" "$(ZIPDIR)" || (rm -rf "$(ZIPDIR)"; exit 1)
	rm -rf "$(ZIPDIR)"

src/GIT_DESCRIBE.cs: $(FILES_CS) $(FILES_C) $(FILES_H)
	echo 'public class GIT_DESCRIBE { public const string data = "$(GIT_DESCRIBE)"; }' > $@

setup.exe: $(FILES_CS) src/GIT_DESCRIBE.cs
	csc /define:GIT_DESCRIBE /out:$@ $^

bootx64.efi: CC_PREFIX = x86_64-w64-mingw32
bootx64.efi: GNUEFI_ARCH = x86_64
bootx64.efi: $(FILES_C)
	$(CC) $(CFLAGS) $(LDFLAGS) $^ -o $@ $(LIBS) -s

bootia32.efi: CC_PREFIX = i686-w64-mingw32
bootia32.efi: GNUEFI_ARCH = ia32
bootia32.efi: $(FILES_C)
	$(CC) $(CFLAGS) $(LDFLAGS) $^ -o $@ $(LIBS) -s
>>>>>>> master
