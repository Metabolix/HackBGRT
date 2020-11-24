uPNG -- derived from LodePNG version 20100808
==========================================

Copying
-------

Copyright (c) 2005-2010 Lode Vandevenne
Copyright (c) 2010 Sean Middleditch

This software is provided 'as-is', without any express or implied
warranty. In no event will the authors be held liable for any damages
arising from the use of this software.

Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:

  1. The origin of this software must not be misrepresented; you must not
  claim that you wrote the original software. If you use this software
  in a product, an acknowledgment in the product documentation would be
  appreciated but is not required.

  2. Altered source versions must be plainly marked as such, and must not be
  misrepresented as being the original software.

  3. This notice may not be removed or altered from any source
  distribution.

Features
--------

uPNG supports loading and decoding PNG images into a simple byte buffer, suitable
for passing directly to OpenGL as texture data.

uPNG does NOT support interlaced images, paletted images, and fixed-transparency.
Checksums are NOT verified and corrupt image data may be undetected.

It DOES support RGB, RGBA, greyscale, and greyscale-with-alpha images.  RGB and
RGBA are currently only supported in 8-bit color depths, and greyscale images
are supported in either 1-, 2-, 4-, or 8-bit color depths.

WARNING: the source project that uPNG is derived from, LodePNG, did not have
the cleanest or best documented code.  Several potential buffer overflows in the
original source have been fixed in uPNG, but there may be more.  Do NOT use uPNG
to load data from untrusted sources, e.g. the Web.  Doing so may open a
remotely exploitable buffer overflow attack in your application.

Installation
------------

Copy the upng.c and upng.h files into your project, and add them to your build
system.  upng.c will compile as C++ if necessary.

Usage
-----

To load a PNG, you must create an upng_t instance, load the raw PNG into the
decoder, and then you can query the upng_t for image properties and the
decoded image buffer.

  upng_t* upng;

  upng = upng_new_from_file("image.png");
  if (upng != NULL) {
    upng_decode(upng);
    if (upng_get_error(upng) == UPNG_EOK) {
      /* do stuff with image */
    }

    upng_free(upng);
  }

You can load a PNG either from an in-memory buffer of bytes or from a file
specified by file path.

  upng_new_from_bytes(const unsigned char*, unsigned long length)
  upng_new_from_file(const char*)

Once an upng_t object is created, you can read just its header properties,
decode the entire file, and release its resources.

  upng_header(upng_t*)  Reads just the header, sets image properties
  upng_decode(upng_t*)  Decodes image data
  upng_free(upng_t*)    Frees the resources attached to a upng_t object

The query functions are:

  upng_get_width(upng_t*)       Returns width of image in pixels
  upng_get_height(upng_t*)      Returns height of image in pixels
  upng_get_size(upng_t*)        Returns the total size of the image buffer in bytes
  upng_get_bpp(upng_t*)         Returns the number of bits per pixel (e.g., 32 for 8-bit RGBA)
  upng_get_bitdepth(upng_t*)    Returns the number of bits per component (e.g., 8 for 8-bit RGBA)
  upng_get_pixelsize(upng_t*)   Returns the number of bytes per pixel (e.g., 4 for 8-bit RGBA)
  upng_get_components(upng_t*)  Returns the number of components per pixel (e.g., 4 for 8-bit RGBA)
  upng_get_format(upng_t*)      Returns the format of the image buffer (see below)
  upng_get_buffer(upng_t*)      Returns a pointer to the image buffer

Additionally, for error handling, you can use:

  upng_get_error(upng_t*)       Returns the error state of the upng object (UPNG_EOK means no error)
  upng_get_error_line(upng_t*)  Returns the line in the upng.c file where the error state was set

The formats supported are:

  UPNG_RGB8         24-bit RGB
  UPNG_RGB16        48-bit RGB
  UPNG_RGBA8        32-bit RGBA
  UPNG_RGBA16       64-bit RGBA
  UPNG_LUMINANCE8   8-bit Greyscale
  UPNG_LUMINANCEA8  8-bit Greyscale w/ 8-bit Alpha

Possible error states are:

  UPNG_EOK          No error (success)
  UPNG_ENOMEM       Out of memory
  UPNG_ENOTFOUND    Resource not found
  UPNG_ENOTPNG      Invalid file header (not a PNG image)
  UPNG_EMALFORMED   PNG image data does not follow spec and is malformed
  UPNG_EUNSUPPORTED PNG image data is well-formed but not supported by uPNG

TODO
----

- Audit the code (particularly the Huffman decoder) for buffer overflows.  Make sure
  uPNG is safe to use even with image data from untrusted sources.

- Make the decompressor work in a streaming/buffered manner, so that we don't need
  to stitch together the PNG IDATA chunks before decompressing, shaving off one
  unnecessary allocation.

- Update the unfiltering code to work on the decompressed image buffer, rather than
  needing a separate output buffer.  The removal of the Adam7 de-interlacing support
  makes this easier.  Removes another unnecessary allocation.

- Update the decoder API to work in a stream/buffered manner, so files can be read
  without needing to allocate a temporary buffer.  This removes yet another
  unnecessary allocation.

- Update the decoder API to allow the user to provide an output buffer, so that
  PNG images can be decoded directly to mapped texture memory.  Removes the need
  for the last unnecessary allocation.

- Test that greyscale images with less than 8-bits of depth actually work, fix
  or remove if they do not.

- Provide optional format conversion (as an extension to byte swizzling) to
  convert input PNGs in one format to one of a (limited) set of target output
  formats commonly used for texturing.

- Provide floating-point conversion, at least for 16-bit source images, for
  HDR textures.

- Provide vertical flipping of decoded image data for APIs that prefer textures
  with an origin in the lower-left instead of upper-left.
