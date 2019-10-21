# LZF

> A simple C# LZF compression implementation which attempts to minimize memory
> allocations.

While LZF compression might not have the great compression ratio of some
algorithms, it is simple and extremely fast. This makes it great for real-time
applications where you want some compression but need to prioritize speed and
responsiveness, such as in game development.

Some implementations of LZF unfortunately are a bit careless with memory
allocations which could potentially cause some issues if you're on a platforms
with limited memory and/or where you're trying to manage and minimize garbage
collection. This implemenation has the following changes:

 * Use of Unity's
   [UnsafeUtility](https://docs.unity3d.com/ScriptReference/Unity.Collections.LowLevel.Unsafe.UnsafeUtility.html)
   class for allocating and managing native memory. This however means it's tied
   to the Unity engine.
 * Use of `stackalloc` to reduce impact of intermediate buffers while
   compressing/decompressing.
 * Use of the equivalent of `System.Buffers.ArrayPool` to reuse `byte[]` buffers
   wherever possible.
 * Uses a singular buffer while compressing to minimize allocations.

This makes the following tradeoffs, focusing on it's use in game development:

 * Heavy use of `stackalloc` moves buffer allocation onto the stack instead of
   the heap, this is exceptionally performant but limits the size of the
   allocated buffers based on the max size of the stack. In most C# enviroments,
   this is ~1MB in size by default. This may make it impractical to use on large
   buffers, and work better on smaller buffers like those used in game
   networking, which tend to be less than 1500 bytes in size.
 * Use of singular buffer with a lock reduces total allocations but limits use to
   efffectively one thread at a time. Game development tends to heavily utilize
   one thread for serialization or network messaging, so this isn't too much of
   an issue in those use cases; howerever, highly parallel processes may see a
   signifigant slowdown.

For more algorithm details, refer to
[Marc Lehmann's original release](http://oldhome.schmorp.de/marc/liblzf.html).

## Installation
In Unity 2018.3 and later, add the following to your `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.houraiteahouse.lzf": "1.0.0"
  },
  "scopedRegistries": [
    {
      "name": "Hourai Teahouse",
      "url": "https://upm.houraiteahouse.net",
      "scopes": ["com.houraiteahouse"]
    }
  ]
}
```
## Usage

This implementation includes several compress/decompress methods which can be
called. However, some of these aren't geared towards minimizing memory
allocation and are included primarily for legacy support with older versions of
this library.

jf you're looking to keep memory allocations as limited as possible, you'll want
to use these methods:

```csharp
public static int Compress(byte[] input, ref byte[] output)
public static int Deompress(byte[] input, ref byte[] output)
```

* `input`: Input bytes to compress/decompress.
* `output`: A reference to the buffer where the output will be produced. This
  will be allocated/resized as necessary, so it's acceptable to initially pass
  in a null reference.
* `Returns`: The size of the output in bytes. This is **VERY IMPORTANT** to note
  because this will be smaller than outputBuffer.Length in most cases. A small
  inconvenience but necessary if you want to avoid unnecessary memory
  allocations.

There are a few ways to tweak the algorithm to prefer speed, compression, or
memory use. See the `Tunable Constants` at the top of the source file for
details.

Here's a quick example of using this library to compress some generic input data
and write it to a file:

```csharp
// Declare input/output buffers.
byte[] inputBuffer = null;
byte[] outputBuffer = null;

// Open output file.
using(FileStream outputFile = File.OpenWrite(outputFilePath))
{
    // GetInput is some method that fills inputBuffer and returns
    // inputBuffer.Length.
    while(GetInput(ref inputBuffer) > 0)
    {
        // Compress input.
        int compressedSize = CLZF2.Compress(inputBuffer, ref outputBuffer);

        // Write compressed data to file.
        // Note the use of the size returned by Compress and not
        // outputBuffer.Length.
        outputFile.Write(outputBuffer, 0, compressedSize);
    }
}
```

There are also unsafe alternatives called TryCompress and TryDecompress. These
will return a length of 0 when it fails to compress within the provided buffers.
This is particularly useful in combination with

## License
This code is under a BSD license. This essentially means you can freely use it
as long as you include the copyright statements as attribution. See the license
file for details.
