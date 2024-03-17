#pragma once

#include "efi.h"

/**
 * Convert a short ASCII string to UCS2, store in a static array.
 *
 * @param src The ASCII string. Will be truncated to 15 characters + null.
 * @param length The maximum length, if the string is not null-terminated.
 * @return The UCS2 string, statically allocated, null-terminated.
 */
extern const CHAR16* TmpStr(CHAR8 *src, int length);

/**
 * Print or log a string.
 *
 * @param mode -1 = print without logging, 0 = no, 1 = yes.
 * @param fmt The format string. Supports %d, %x, %s.
 */
extern void Log(int mode, IN CONST CHAR16 *fmt, ...);

/**
 * Dump the log buffer to the screen.
 */
extern void DumpLog(void);

/**
 * Clear the log EFI variable, for minor RAM savings.
 */
extern void ClearLogVariable(void);

/**
 * Return the greater of two numbers.
 */
static inline int max(int a, int b) {
	return a > b ? a : b;
}

/**
 * Return the smaller of two numbers.
 */
static inline int min(int a, int b) {
	return a < b ? a : b;
}

/**
 * Trim BOM, spaces and tabs from the beginning of a string.
 *
 * @param s The string.
 * @return Pointer to the first acceptable character.
 */
extern const CHAR16* TrimLeft(const CHAR16* s);

/**
 * Find the position of another string within a string.
 *
 * @param haystack The full text.
 * @param needle The string to look for.
 * @return Pointer to the first occurence of needle in the haystack, or 0.
 */
extern const CHAR16* StrStr(const CHAR16* haystack, const CHAR16* needle);


/**
 * Find the position after another string within a string.
 *
 * @param haystack The full text.
 * @param needle The string to look for.
 * @return Pointer after the first occurence of needle in the haystack, or 0.
 */
extern const CHAR16* StrStrAfter(const CHAR16* haystack, const CHAR16* needle);

/**
 * Rotate left a 64-bit value.
 */
static inline UINT64 rotl(const UINT64 x, int k) {
	return (x << k) | (x >> (64 - k));
}

/**
 * Generate a random 64-bit number.
 */
extern UINT64 Random(void);


/**
 * Seed the random number generator. Pass 0 and 0 to seed from the clock.
 */
extern void RandomSeed(UINT64 a, UINT64 b);

/**
 * Seed the random number generator automatically.
 */
extern void RandomSeedAuto(void);

/**
 * Wait for a key press. It will still remain in the buffer.
 *
 * @param timeout_ms The timeout in milliseconds, or 0 for no timeout.
 */
extern EFI_STATUS WaitKey(UINT64 timeout_ms);

/**
 * Wait for a key press and read it.
 *
 * @param timeout_ms The timeout in milliseconds, or 0 for no timeout.
 * @return The pressed key.
 */
extern EFI_INPUT_KEY ReadKey(UINT64 timeout_ms);

/**
 * Load a file, allocate some extra bytes as well.
 */
extern void* LoadFileWithPadding(EFI_FILE_HANDLE dir, const CHAR16* path, UINTN* size_ptr, UINTN padding);

/**
 * Load a file.
 */
static inline void* LoadFile(EFI_FILE_HANDLE dir, const CHAR16* path, UINTN* size_ptr) {
	return LoadFileWithPadding(dir, path, size_ptr, 0);
}

/**
 * Get a temporary pointer to GUID.
 */
static inline EFI_GUID* TmpGuidPtr(EFI_GUID guid) {
	static EFI_GUID g;
	g = guid;
	return &g;
}
