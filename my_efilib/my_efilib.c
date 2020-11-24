// Created by FREE WING,Y.Sakamoto
// http://www.neko.ne.jp/~freewing/

#include "my_efilib.h"

// #include <stdlib.h>
// Memory Allocation
void *malloc(size_t size) {
	void* data;
	BS->AllocatePool(EfiBootServicesData, size, &data);
	return data;
}

void *calloc(size_t nmemb, size_t size) {
	void* data;
	BS->AllocatePool(EfiBootServicesData, (nmemb * size), &data);
	// calloc function is Allocate and Clear zero memory
	memset(data, 0x00, (nmemb * size));
	return data;
}

void free(void *ptr) {
	FreePool(ptr);
}

void *realloc(void *ptr, size_t size) {
	void* data = malloc(size);
	if (data && ptr) {
		// Ignore Read Overflow of Old size < New size
		memcpy(data, ptr, size);
		free(ptr);
	}
	return data;
}

// #include <string.h>
// Compare two areas of memory
int memcmp(const void *cs, const void *ct, size_t count) {
	const unsigned char *su1, *su2;
	int res = 0;

	for (su1 = cs, su2 = ct; 0 < count; ++su1, ++su2, --count) {
		if ((res = *su1 - *su2) != 0) break;
	}

	return res;
}

