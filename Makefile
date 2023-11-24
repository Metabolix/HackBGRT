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
ZIPDIR = HackBGRT-$(GIT_DESCRIBE:v%=%)
ZIP = $(ZIPDIR).zip

EFI_ARCH_LIST = x64 ia32 aa64 arm

.PHONY: all efi efi-signed setup zip clean

all: efi setup
efi: $(patsubst %,efi/boot%.efi,$(EFI_ARCH_LIST))
efi-signed: $(patsubst %,efi-signed/boot%.efi,$(EFI_ARCH_LIST))
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

clean:
	rm -rf setup.exe efi efi-signed
