#ifdef GIT_DESCRIBE
#define SBAT_READABLE_VERSION GIT_DESCRIBE
#else
#define SBAT_READABLE_VERSION "unknown"
#endif

const char sbat[] __attribute__ ((section (".sbat"))) =
"sbat,1,SBAT Version,sbat,1,https://github.com/rhboot/shim/blob/main/SBAT.md\n"
"hackbgrt,1,Metabolix,HackBGRT," SBAT_READABLE_VERSION ",https://github.com/Metabolix/HackBGRT\n"
;
