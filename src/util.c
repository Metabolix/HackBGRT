#include "util.h"

#include <efilib.h>

const CHAR16* TmpStr(CHAR8 *src, int length) {
	static CHAR16 arr[4][16];
	static int j;
	CHAR16* dest = arr[j = (j+1) % 4];
	int i;
	for (i = 0; i < length && i < 16-1 && src[i]; ++i) {
		dest[i] = src[i];
	}
	dest[i] = 0;
	return dest;
}

#define log_buffer_size (65536)
CHAR16 log_buffer[log_buffer_size] = {0};

CHAR16 LogVarName[] = L"HackBGRTLog";
EFI_GUID LogVarGuid = {0x03c64761, 0x075f, 0x4dba, {0xab, 0xfb, 0x2e, 0xd8, 0x9e, 0x18, 0xb2, 0x36}}; // self-made: 03c64761-075f-4dba-abfb-2ed89e18b236

void Log(int print, IN CONST CHAR16 *fmt, ...) {
	va_list args;
	CHAR16 buf[256];
	va_start(args, fmt);
	VSPrint(buf, sizeof(buf), fmt, args); // size is in bytes, not CHAR16s
	va_end(args);
	if (print) {
		Print(L"%s", buf);
	}
	StrnCat(log_buffer, buf, log_buffer_size - StrLen(log_buffer) - 1);
	LibSetVariable(LogVarName, &LogVarGuid, StrLen(log_buffer) * 2, log_buffer);
}

void DumpLog(void) {
	Print(L"%s", log_buffer);
}

void ClearLogVariable(void) {
	LibDeleteVariable(LogVarName, &LogVarGuid);
}

const CHAR16* TrimLeft(const CHAR16* s) {
	// Skip white-space and BOM.
	while (s[0] == L'\xfeff' || s[0] == ' ' || s[0] == '\t') {
		++s;
	}
	return s;
}

const CHAR16* StrStr(const CHAR16* haystack, const CHAR16* needle) {
	int len = StrLen(needle);
	while (haystack && haystack[0]) {
		if (StrnCmp(haystack, needle, len) == 0) {
			return haystack;
		}
		++haystack;
	}
	return 0;
}

const CHAR16* StrStrAfter(const CHAR16* haystack, const CHAR16* needle) {
	return (haystack = StrStr(haystack, needle)) ? haystack + StrLen(needle) : 0;
}

UINT64 Random_a, Random_b;

UINT64 Random(void) {
	// Implemented after xoroshiro128plus.c
	if (!Random_a && !Random_b) {
		RandomSeedAuto();
	}
	UINT64 a = Random_a, b = Random_b, r = a + b;
	b ^= a;
	Random_a = rotl(a, 55) ^ b ^ (b << 14);
	Random_b = rotl(b, 36);
	return r;
}

void RandomSeed(UINT64 a, UINT64 b) {
	Random_a = a;
	Random_b = b;
}

void RandomSeedAuto(void) {
	EFI_TIME t;
	RT->GetTime(&t, 0);
	UINT64 a, b = ((((((UINT64) t.Second * 100 + t.Minute) * 100 + t.Hour) * 100 + t.Day) * 100 + t.Month) * 10000 + t.Year) * 300000 + t.Nanosecond;
	BS->GetNextMonotonicCount(&a);
	RandomSeed(a, b), Random(), Random();
}

EFI_STATUS WaitKey(UINT64 timeout_ms) {
	ST->ConIn->Reset(ST->ConIn, FALSE);
	const int ms_to_100ns = 10000;
	return WaitForSingleEvent(ST->ConIn->WaitForKey, timeout_ms * ms_to_100ns);
}

EFI_INPUT_KEY ReadKey(UINT64 timeout_ms) {
	EFI_INPUT_KEY key = {0};
	WaitKey(timeout_ms);
	ST->ConIn->ReadKeyStroke(ST->ConIn, &key);
	return key;
}

void* LoadFileWithPadding(EFI_FILE_HANDLE dir, const CHAR16* path, UINTN* size_ptr, UINTN padding) {
	EFI_STATUS e;
	EFI_FILE_HANDLE handle;

	e = dir->Open(dir, &handle, (CHAR16*) path, EFI_FILE_MODE_READ, 0);
	if (EFI_ERROR(e)) {
		return 0;
	}

	EFI_FILE_INFO *info = LibFileInfo(handle);
	UINTN size = info->FileSize;
	FreePool(info);

	void* data = 0;
	e = BS->AllocatePool(EfiBootServicesData, size + padding, &data);
	if (EFI_ERROR(e)) {
		handle->Close(handle);
		return 0;
	}
	e = handle->Read(handle, &size, data);
	for (int i = 0; i < padding; ++i) {
		*((char*)data + size + i) = 0;
	}
	handle->Close(handle);
	if (EFI_ERROR(e)) {
		FreePool(data);
		return 0;
	}
	if (size_ptr) {
		*size_ptr = size;
	}
	return data;
}
