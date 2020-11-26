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
