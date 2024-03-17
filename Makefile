<<<<<<< HEAD
# for Windows 10 WSL Debian build
# use Normal GCC
CROSS_COMPILE =
# PREFIX=/usr/local/
PREFIX = ./gnu-efi/usr/local/
TARGET = HackBGRT_MULTI_$(ARCH)
_OBJS = main.o config.o types.o util.o sbat.o
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
CFLAGS += '-DGIT_DESCRIBE_W=L"$(GIT_DESCRIBE)"' '-DGIT_DESCRIBE="$(GIT_DESCRIBE)"'

# LDFLAGS
LDFLAGS = -nostdlib --warn-common --no-undefined \
	--fatal-warnings --build-id=sha1 \
	-shared -Bsymbolic
# set EFI_SUBSYSTEM: Application(0x0a)
LDFLAGS += --defsym=EFI_SUBSYSTEM=0x0a
LDFLAGS += -L$(PREFIX)/lib
# ld: warning: ./gnu-efi/usr/local//lib/crt0-efi-x86_64.o: missing .note.GNU-stack section implies executable stack
# ld: NOTE: This behaviour is deprecated and will be removed in a future version of the linker
LDFLAGS += -z noexecstack


####### rules #########
=======
CC = clang
CFLAGS = -target $(CLANG_TARGET) -ffreestanding -fshort-wchar
CFLAGS += -std=c17 -Wshadow -Wall -Wunused -Werror-implicit-function-declaration
CFLAGS += -I$(GNUEFI_INC) -I$(GNUEFI_INC)/$(GNUEFI_ARCH) -I$(GNUEFI_INC)/protocol
CFLAGS += $(ARCH_CFLAGS)
LDFLAGS = -target $(CLANG_TARGET) -nostdlib -Wl,-entry:efi_main -Wl,-subsystem:efi_application -fuse-ld=lld
ARCH_CFLAGS = -O2 -mno-red-zone

GNUEFI_INC = gnu-efi/inc

FILES_C = src/main.c src/util.c src/types.c src/config.c src/sbat.c src/efi.c
FILES_H = $(wildcard src/*.h)
FILES_CS = src/Setup.cs src/Esp.cs src/Efi.cs
GIT_DESCRIBE := $(firstword $(GIT_DESCRIBE) $(shell git describe --tags) unknown)
CFLAGS += '-DGIT_DESCRIBE_W=L"$(GIT_DESCRIBE)"' '-DGIT_DESCRIBE="$(GIT_DESCRIBE)"'
RELEASE_NAME = HackBGRT-$(GIT_DESCRIBE:v%=%)

EFI_ARCH_LIST = x64 ia32 aa64 arm
EFI_SIGNED_FILES = $(patsubst %,efi-signed/boot%.efi,$(EFI_ARCH_LIST))

.PHONY: all efi efi-signed setup release clean

all: efi setup
	@echo "Run 'make efi-signed' to sign the EFI executables."
	@echo "Run 'make release' to build a release-ready ZIP archive."
	@echo "Run 'make run-qemu-<arch>' to test the EFI executables with QEMU."

efi: $(patsubst %,efi/boot%.efi,$(EFI_ARCH_LIST))
	@echo "EFI executables are in the efi/ directory."

efi-signed: $(patsubst %,efi-signed/boot%.efi,$(EFI_ARCH_LIST))
	@echo "Signed EFI executables are in the efi-signed/ directory."

setup: setup.exe

release: release/$(RELEASE_NAME).zip
	@echo "Current version is packaged: $<"

release/$(RELEASE_NAME): $(EFI_SIGNED_FILES) certificate.cer config.txt splash.bmp setup.exe README.md CHANGELOG.md README.efilib LICENSE shim-signed/* shim.md
	rm -rf $@
	tar c --transform=s,^,$@/, $^ | tar x

release/$(RELEASE_NAME).zip: release/$(RELEASE_NAME)
	rm -rf $@
	(cd release; 7z a -mx=9 "$(RELEASE_NAME).zip" "$(RELEASE_NAME)" -bd -bb1)
>>>>>>> master

all: $(TARGET).efi

<<<<<<< HEAD
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
=======
setup.exe: $(FILES_CS) src/GIT_DESCRIBE.cs
	csc /nologo /define:GIT_DESCRIBE /out:$@ $^
>>>>>>> master

# build shared object
$(TARGET).so: $(OBJS)
	$(LD) $(LDFLAGS) $(STARTOBJ) $^ -o $@ \
		-lefi -lgnuefi \
		-T $(LDSCRIPT)

<<<<<<< HEAD
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
=======
efi-signed/%.efi: efi/%.efi pki
	@mkdir -p efi-signed
	pesign --force -n pki -i $< -o $@ -c HackBGRT-signer -s

efi/bootx64.efi: CLANG_TARGET = x86_64-pc-windows-msvc
efi/bootx64.efi: GNUEFI_ARCH = x86_64

efi/bootia32.efi: CLANG_TARGET = i386-pc-windows-msvc
efi/bootia32.efi: GNUEFI_ARCH = ia32

efi/bootaa64.efi: CLANG_TARGET = aarch64-pc-windows-msvc
efi/bootaa64.efi: GNUEFI_ARCH = aa64

efi/boot%.efi: $(FILES_C)
	@mkdir -p efi
	$(CC) $(CFLAGS) $(LDFLAGS) $^ -o $@

efi/bootarm.efi: CLANG_TARGET = armv6-pc-windows-msvc
efi/bootarm.efi: GNUEFI_ARCH = arm
efi/bootarm.efi: ARCH_CFLAGS = -O # skip -O2 and -mno-red-zone
efi/bootarm.efi: $(FILES_C)
	@mkdir -p efi
	$(CC) $(CFLAGS) $(LDFLAGS) $^ -o $@
	@echo "Fix $@ architecture code (IMAGE_FILE_MACHINE_ARMTHUMB_MIXED = 0x01C2)"
	echo -en "\xc2\x01" | dd of=$@ bs=1 seek=124 count=2 conv=notrunc status=none
>>>>>>> master

# clean rule
clean:
<<<<<<< HEAD
	rm -f ./obj/*.o *.so s*.efi
=======
	rm -rf setup.exe efi efi-signed
	rm -f src/GIT_DESCRIBE.cs
	rm -rf release
	rm -rf test

.PHONY: test $(patsubst %,run-qemu-%,$(EFI_ARCH_LIST))

test: run-qemu-x64
	@echo "Run 'make run-qemu-<arch>' to test other architectures."

test/esp-%: efi/boot%.efi splash.bmp
	rm -rf $@
	mkdir -p $@/EFI/HackBGRT
	cp efi/boot$*.efi splash.bmp $@/EFI/HackBGRT
	echo -en "FS0:\n cd EFI\n cd HackBGRT\n boot$*.efi resolution=-1x-1 debug=1 image=path=splash.bmp" > $@/startup.nsh

QEMU_ARGS = -bios $(word 2, $^) -net none -drive media=disk,file=fat:rw:./$<,format=raw

run-qemu-x64: test/esp-x64 /usr/share/ovmf/x64/OVMF.fd
	qemu-system-x86_64 $(QEMU_ARGS)

run-qemu-ia32: test/esp-ia32 /usr/share/ovmf/ia32/OVMF.fd
	qemu-system-i386 $(QEMU_ARGS)

run-qemu-aa64: test/esp-aa64 /usr/share/ovmf/aarch64/QEMU_EFI.fd
	@echo "Press Ctrl+Alt+2 to switch to QEMU console."
	qemu-system-aarch64 -machine virt -cpu max $(QEMU_ARGS)

run-qemu-arm: test/esp-arm /usr/share/ovmf/arm/QEMU_EFI.fd
	@echo "Press Ctrl+Alt+2 to switch to QEMU console."
	qemu-system-arm -machine virt -cpu max $(QEMU_ARGS)
>>>>>>> master
