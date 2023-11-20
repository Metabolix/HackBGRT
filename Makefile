CC      = $(CC_PREFIX)-gcc
CFLAGS  = -std=c11 -O2 -ffreestanding -mno-red-zone -fno-stack-protector -Wshadow -Wall -Wunused -Werror-implicit-function-declaration -Werror
CFLAGS += -I$(GNUEFI_INC) -I$(GNUEFI_INC)/$(GNUEFI_ARCH) -I$(GNUEFI_INC)/protocol
LDFLAGS = -nostdlib -shared -Wl,-dll -Wl,--subsystem,10 -e efi_main -s

GNUEFI_INC = /usr/$(CC_PREFIX)/include/efi

FILES_C = src/main.c src/util.c src/types.c src/config.c src/sbat.c src/efi.c
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
	$(CC) $(CFLAGS) $(LDFLAGS) $^ -o $@

efi/bootia32.efi: CC_PREFIX = i686-w64-mingw32
efi/bootia32.efi: GNUEFI_ARCH = ia32
efi/bootia32.efi: $(FILES_C)
	@mkdir -p efi
	$(CC) $(CFLAGS) $(LDFLAGS) $^ -o $@

clean:
	rm -rf setup.exe efi efi-signed
