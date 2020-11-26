# picojpeg
Exported from http://code.google.com/p/picojpeg. Original author is Rich Geldreich at *richgel99 at gmail.com*.

picojpeg is a public domain [JPEG](http://en.wikipedia.org/wiki/JPEG) decompressor written in plain C in a single source file [picojpeg.c](picojpeg.c) and a single header [picojpeg.h](picojpeg.h). It has several fairly unique properties that make it useful on small 8/16-bit embedded devices or in very memory constrained environments:

* Optimized for minimal memory consumption: Uses only ~2.3KB of work memory.
* Written assuming ROM code memory is more abundant than available RAM, which is the case on many microcontrollers.
* All work arrays are limited to less than or equal to 256 bytes in size.
* Written entirely in C, no reliance at all on the C run-time library (it includes no headers other than picojpeg.h), and does not use any dynamic memory allocation.
* Simple API: Call pjpeg_decode_init() to initialize the decoder, and pjpeg_decode_mcu() to decode each MCU (Minimum Coded Unit).
* Majority of expressions use only 8-bit integer operations, most variables are only 8-bits. 16-bit operations are only used when necessary.
* Uses a Winograd IDCT to minimize the number of multiplies (5 per 1D IDCT, up to 80 total, each signed mul can be implemented as 16 bits*8 bits=24 bits).
* Whenever possible, multiplies are limited to 8x8 or 16x8 bits, and are done against compile-time constants.
* As of v1.1 the decoder supports a special "reduce" mode that returns a 1/8th size version of the compressed image. This mode skips several expensive steps: AC dequantization, IDCT, and per-pixel chroma upsampling/colorspace conversion.

picojpeg has several disadantages and known issues compared to other implementations:

* Quality is traded off for minimal RAM memory consumption and decent performance on small microcontrollers. For example, the chroma upsamplers use only box filtering, 8x8 multiplies, and minimal 16-bit operations so the decoder is not as accurate as it could be.
* Only supports baseline sequential greyscale, or YCbCr H1V1, H1V2, H2V1, and H2V2 chroma sampling factors. Progressive JPEG's are not supported.
* The Huffman decoder currently only reads a bit at a time to minimize RAM usage, so it's pretty slow. (However, on microcontroller CPU's with weak integer shift capabilities this method may be reasonable.)
* All work arrays (approx. 2.3KB) are globals, because this resulted in the best code generation with the embedded compiler I was using during development. I'm assuming either this is not an issue, or the user is using a compiler that allows them to overlap these variables with other unrelated things. picojpeg is not thread safe because of this.

A close variant of picojpeg has been successfully compiled and executed on Microchip's [PIC18F4610](http://www.microchip.com/wwwproducts/Devices.aspx?dDocName=en010303) microcontroller using [SourceBoost Technologies BoostC embedded compiler](http://www.sourceboost.com/Products/BoostC/Overview.html). (Please email if you would like to see this variant.) picojpeg.c has also been used on the MSP430 and ARM Cortex-M4 CPU's. (For an example usage, see [Using picojpeg library on a PIC with ILI9341 320x240 LCD module](http://minhdanh2002.blogspot.com/2014/03/using-picojpeg-library-on-pic24-with.html)).

The source distribution includes a sample VS2005 project and precompiled Win32/Win64 command line executables that convert JPG to TGA files using picojpeg for decompression. Sean Barrett's public domain [stb_image.c](http://nothings.org/stb_image.c) module is used to write TGA files.

picojpeg was originally based off my [jpgd decompressor C++ class](http://code.google.com/p/jpgd/), which (on modern CPU's) is faster and more capable than picojpeg.c but uses a lot more memory and assumes int's are 32-bits.

Here's picojpeg working on a 6809 CPU (a Tandy Color Computer 3), compiled using gcc6809: [picojpeg: Decoding Lena on a Tandy Color Computer 3](http://richg42.blogspot.com/2014/02/picojpeg-decoding-lena-on-tandy-color.html).

## Release History

* v1.1 - 3/23/2020: Fixed unsigned/signed issue in macro, fixed overflow issue in decoder preventing the decode of very large JPEG's

* v1.1 - Feb. 19, 2013: Dual licensed as both public domain and (where public domain is not acceptable) the MIT license. Please contact me for the source drop.
* v1.1 - Feb. 9, 2013: Optimized the IDCT row/col loops to avoid the full inverse transform when only the DC component is non-zero, added "reduce" mode for fast 1/8th res decoding, better error handling, added support for H2V1/H1V2 chroma subsampling factors, ported jpg2tga.cpp to jpg2jpg.c (so all modules are written in plain C now), added code to compare picojpeg's decoded output vs. stb_image'c for testing/verification.
* v1.0 - Nov. 10, 2010: Initial release. Derived from the original version which was tested on a PIC18F series CPU.

## Special Thanks

Thanks to Daniel Swenson <swenson@ksu.edu> for contributing to picojpeg's development, and Chris Phoenix <cphoenix@gmail.com> for his MSP430 patches.

For any questions or problems with this module please contact Rich Geldreich at *richgel99 at gmail.com*. Here's my [twitter page](http://twitter.com/#!/richgel999).
