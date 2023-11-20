#include "efi.h"
#include "util.h"

// New implementations of some functions in gnu-efi.
// These functions are designed to avoid other gnu-efi calls.

EFI_STATUS LibLocateProtocol(IN EFI_GUID *ProtocolGuid, OUT VOID **Interface) {
	EFI_HANDLE buffer[256];
	UINTN size = sizeof(buffer);
	if (!EFI_ERROR(BS->LocateHandle(ByProtocol, ProtocolGuid, NULL, &size, buffer))) {
		for (int i = 0; i < size / sizeof(EFI_HANDLE); ++i) {
			if (!EFI_ERROR(BS->HandleProtocol(buffer[i], ProtocolGuid, Interface))) {
				return EFI_SUCCESS;
			}
		}
	}
	return EFI_NOT_FOUND;
}

EFI_DEVICE_PATH *FileDevicePath(IN EFI_HANDLE Device OPTIONAL, IN CHAR16 *FileName) {
	EFI_DEVICE_PATH *old_path = 0;
	if (!Device || EFI_ERROR(BS->HandleProtocol(Device, TmpGuidPtr((EFI_GUID) EFI_DEVICE_PATH_PROTOCOL_GUID), (void**)&old_path))) {
		static EFI_DEVICE_PATH end_path = {END_DEVICE_PATH_TYPE, END_ENTIRE_DEVICE_PATH_SUBTYPE, {sizeof(end_path), 0}};
		old_path = &end_path;
	}
	UINTN old_path_size = 0, instances = 0;
	for (EFI_DEVICE_PATH *p0 = old_path;; p0 = NextDevicePathNode(p0)) {
		old_path_size += DevicePathNodeLength(p0);
		if (IsDevicePathEndType(p0)) {
			instances += 1;
		}
		if (IsDevicePathEnd(p0)) {
			break;
		}
	}

	UINTN size_str = (StrLen(FileName) + 1) * sizeof(*FileName);
	UINTN size_fdp = SIZE_OF_FILEPATH_DEVICE_PATH + size_str;

	EFI_DEVICE_PATH *new_path;
	if (EFI_ERROR(BS->AllocatePool(EfiBootServicesData, old_path_size + instances * size_fdp, (void**)&new_path))) {
		return 0;
	}

	EFI_DEVICE_PATH *p1 = new_path;
	for (EFI_DEVICE_PATH *p0 = old_path;; p0 = NextDevicePathNode(p0)) {
		if (IsDevicePathEndType(p0)) {
			*p1 = (EFI_DEVICE_PATH) {
				.Type = MEDIA_DEVICE_PATH,
				.SubType = MEDIA_FILEPATH_DP,
				.Length = {size_fdp, size_fdp >> 8},
			};
			FILEPATH_DEVICE_PATH *f = (FILEPATH_DEVICE_PATH *) p1;
			BS->CopyMem(f->PathName, FileName, size_str);
			p1 = NextDevicePathNode(p1);
		}
		BS->CopyMem(p1, p0, DevicePathNodeLength(p0));
		if (IsDevicePathEnd(p0)) {
			break;
		}
		p1 = NextDevicePathNode(p1);
	}

	return new_path;
}

INTN CompareMem(IN CONST VOID *Dest, IN CONST VOID *Src, IN UINTN len) {
	CONST UINT8 *d = Dest, *s = Src;
	for (UINTN i = 0; i < len; ++i) {
		if (d[i] != s[i]) {
			return d[i] - s[i];
		}
	}
	return 0;
}

void StrnCat(IN CHAR16* dest, IN CONST CHAR16* src, UINTN len) {
	CHAR16* d = dest;
	while (*d) {
		++d;
	}
	while (len-- && *src) {
		*d++ = *src++;
	}
	*d = 0;
}

UINTN StrLen(IN CONST CHAR16* s) {
	UINTN i = 0;
	while (*s++) {
		++i;
	}
	return i;
}

INTN StriCmp(IN CONST CHAR16* s1, IN CONST CHAR16* s2) {
	while (*s1 && *s2) {
		CHAR16 c1 = *s1++, c2 = *s2++;
		if (c1 >= 'A' && c1 <= 'Z') {
			c1 += 'a' - 'A';
		}
		if (c2 >= 'A' && c2 <= 'Z') {
			c2 += 'a' - 'A';
		}
		if (c1 != c2) {
			return c1 - c2;
		}
	}
	return *s1 - *s2;
}

INTN StrnCmp(IN CONST CHAR16* s1, IN CONST CHAR16* s2, UINTN len) {
	while (*s1 && *s2 && len--) {
		CHAR16 c1 = *s1++, c2 = *s2++;
		if (c1 >= 'A' && c1 <= 'Z') {
			c1 += 'a' - 'A';
		}
		if (c2 >= 'A' && c2 <= 'Z') {
			c2 += 'a' - 'A';
		}
		if (c1 != c2) {
			return c1 - c2;
		}
	}
	return len ? *s1 - *s2 : 0;
}

INTN StrCmp(IN CONST CHAR16* s1, IN CONST CHAR16* s2) {
	while (*s1 && *s2) {
		if (*s1 != *s2) {
			return *s1 - *s2;
		}
		++s1, ++s2;
	}
	return *s1 - *s2;
}

UINTN Atoi(IN CONST CHAR16* s) {
	UINTN n = 0;
	while (*s >= '0' && *s <= '9') {
		n = n * 10 + *s++ - '0';
	}
	return n;
}

void *memset(void *s, int c, __SIZE_TYPE__ n) {
	unsigned char *p = s;
	while (n--)
		*p++ = c;
	return s;
}

void *memcpy(void *dest, const void *src, __SIZE_TYPE__ n) {
	const unsigned char *q = src;
	unsigned char *p = dest;
	while (n--)
		*p++ = *q++;
	return dest;
}
