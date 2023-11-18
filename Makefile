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

FILES_C = src/main.c src/util.c src/types.c src/config.c src/sbat.c
FILES_H = $(wildcard src/*.h)
FILES_CS = src/Setup.cs src/Esp.cs src/Efi.cs
GIT_DESCRIBE := $(firstword $(GIT_DESCRIBE) $(shell git describe --tags) unknown)
CFLAGS += '-DGIT_DESCRIBE_W=L"$(GIT_DESCRIBE)"' '-DGIT_DESCRIBE="$(GIT_DESCRIBE)"'
ZIPDIR = HackBGRT-$(GIT_DESCRIBE:v%=%)
ZIP = $(ZIPDIR).zip

.PHONY: all efi efi-signed setup zip clean

all: efi setup
efi: efi/bootx64.efi efi/bootia32.efi
efi-signed: efi-signed/bootx64.efi efi-signed/bootia32.efi
setup: setup.exe

zip: $(ZIP)
$(ZIP): efi-signed certificate.cer config.txt splash.bmp setup.exe README.md CHANGELOG.md README.efilib LICENSE shim-signed shim.md
	test ! -d "$(ZIPDIR)"
	mkdir "$(ZIPDIR)"
	cp -a $^ "$(ZIPDIR)" || (rm -rf "$(ZIPDIR)"; exit 1)
	7z a -mx=9 "$(ZIP)" "$(ZIPDIR)" || (rm -rf "$(ZIPDIR)"; exit 1)
	rm -rf "$(ZIPDIR)"

src/GIT_DESCRIBE.cs: $(FILES_CS) $(FILES_C) $(FILES_H)
	echo 'public class GIT_DESCRIBE { public const string data = "$(GIT_DESCRIBE)"; }' > $@

setup.exe: $(FILES_CS) src/GIT_DESCRIBE.cs
	csc /define:GIT_DESCRIBE /out:$@ $^

certificate.cer pki:
	@echo
	@echo "You need proper keys to sign the EFI executables."
	@echo "Example:"
	@echo "mkdir -p pki"
	@echo "certutil --empty-password -N -d pki"
	@echo "efikeygen -d pki -n HackBGRT-signer -S -k -c 'CN=HackBGRT Secure Boot Signer,OU=HackBGRT,O=Unknown,MAIL=unknown@example.com' -u 'URL'"
	@echo "certutil -d pki -n HackBGRT-signer -Lr > certificate.cer"
	@echo "Modify and run the commands yourself."
	@echo
	@false

efi-signed/%.efi: efi/%.efi
	mkdir -p efi-signed
	pesign --force -n pki -i $< -o $@ -c HackBGRT-signer -s

efi-signed/bootx64.efi: pki
efi-signed/bootia32.efi: pki

efi/bootx64.efi: CC_PREFIX = x86_64-w64-mingw32
efi/bootx64.efi: GNUEFI_ARCH = x86_64
efi/bootx64.efi: $(FILES_C)
	@mkdir -p efi
	$(CC) $(CFLAGS) $(LDFLAGS) $^ -o $@ $(LIBS) -s

efi/bootia32.efi: CC_PREFIX = i686-w64-mingw32
efi/bootia32.efi: GNUEFI_ARCH = ia32
efi/bootia32.efi: $(FILES_C)
	@mkdir -p efi
	$(CC) $(CFLAGS) $(LDFLAGS) $^ -o $@ $(LIBS) -s

clean:
	rm -rf setup.exe efi efi-signed
>>>>>>> origin/master
