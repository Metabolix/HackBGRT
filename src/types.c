#include "types.h"

UINT8 SumBytes(const UINT8* arr, UINTN size) {
	UINT8 sum = 0;
	for (UINTN i = 0; i < size; ++i) {
		sum += arr[i];
	}
	return sum;
}

int VerifyAcpiRsdp2Checksums(const void* data) {
	const UINT8* arr = data;
	UINTN size = *(const UINT32*)&arr[20];
	return SumBytes(arr, 20) == 0 && SumBytes(arr, size) == 0;
}

void SetAcpiRsdp2Checksums(void* data) {
	UINT8* arr = data;
	UINTN size = *(const UINT32*)&arr[20];
	arr[9] = 0;
	arr[32] = 0;
	arr[9] = -SumBytes(arr, 20);
	arr[32] = -SumBytes(arr, size);
}

int VerifyAcpiSdtChecksum(const void* data) {
	const UINT8* arr = data;
	UINTN size = *(const UINT32*)&arr[4];
	return SumBytes(arr, size) == 0;
}

void SetAcpiSdtChecksum(void* data) {
	UINT8* arr = data;
	UINTN size = *(const UINT32*)&arr[4];
	arr[9] = 0;
	arr[9] = -SumBytes(arr, size);
}
