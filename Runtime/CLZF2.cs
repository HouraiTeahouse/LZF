﻿/*
 * Addition of ArrayPool, use of unsafe pointers and stackalloc.
 * Copyright (c) 2016 James Liu <contact@jamessliu.com>
 *
 * Fewer allocations version:
 * Copyright (c) 2016 Chase Pettit <chasepettit@gmail.com>
 * 
 * Improved version to C# LibLZF Port:
 * Copyright (c) 2010 Roman Atachiants <kelindar@gmail.com>
 *
 * Original CLZF Port:
 * Copyright (c) 2005 Oren J. Maurice <oymaurice@hazorea.org.il>
 *
 * Original LibLZF Library  Algorithm:
 * Copyright (c) 2000-2008 Marc Alexander Lehmann <schmorp@schmorp.de>
 *
 * Redistribution and use in source and binary forms, with or without modifica-
 * tion, are permitted provided that the following conditions are met:
 *
 *   1.  Redistributions of source code must retain the above copyright notice,
 *       this list of conditions and the following disclaimer.
 *
 *   2.  Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *
 *   3.  The name of the author may not be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE AUTHOR ``AS IS'' AND ANY EXPRESS OR IMPLIED
 * WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MER-
 * CHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.  IN NO
 * EVENT SHALL THE AUTHOR BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPE-
 * CIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
 * PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS;
 * OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
 * WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTH-
 * ERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED
 * OF THE POSSIBILITY OF SUCH DAMAGE.
 */
using System;
using System.Buffers;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace HouraiTeahouse.Compression {

/// <summary>
/// Improved C# LZF Compressor, a very small data compression library. 
/// The compression algorithm is extremely fast.
/// </summary>
public static unsafe class CLZF2 {

  /// <summary>
  /// Size of hashtable is 2^HLOG bytes. 
  /// Decompression is independent of the hash table size.
  /// The difference between 15 and 14 is very small
  /// for small blocks (and 14 is usually a bit faster).
  /// For a low-memory/faster configuration, use HLOG == 13;
  /// For best compression, use 15 or 16 (or more, up to 22).
  /// </summary>
  const uint HLOG = 14;

  const uint HSIZE = (1 << (int)HLOG);
  const uint MAX_LIT = (1 << 5);
  const uint MAX_OFF = (1 << 13);
  const uint MAX_REF = ((1 << 8) + (1 << 3));

  /// <summary>
  /// Hashtable, that can be allocated only once.
  /// </summary>
  static readonly long* HashTable;

  static CLZF2() {
    HashTable = (long*)UnsafeUtility.Malloc(
      HSIZE * sizeof(long),
      UnsafeUtility.AlignOf<long>(),
      Allocator.Persistent);
  }

  /// <summary>
  /// Lock object for access to hashtable so that we can keep things thread safe.
  /// Still up to the caller to make sure any shared outputBuffer use is thread safe.
  /// </summary>
  static readonly object hashTableLock = new object();

    /// <summary>
  /// Compress input bytes.
  /// </summary>
  /// <param name="inputBytes">Bytes to compress.</param>
  /// <returns>Compressed bytes.</returns>
  public static byte[] Compress(byte[] inputBytes) => Compress(inputBytes, inputBytes.Length);

  /// <summary>
  /// Compress input bytes.
  /// </summary>
  /// <param name="inputBytes">Bytes to compress.</param>
  /// <param name="inputLength">Length of data in inputBytes to decompress.</param>
  /// <returns>Compressed bytes.</returns>
  public static byte[] Compress(byte[] inputBytes, int inputLength) {
    byte[] tempBuffer = null;
    int byteCount = Compress(inputBytes, ref tempBuffer, inputLength);

    byte[] outputBytes = new byte[byteCount];
    Buffer.BlockCopy(tempBuffer, 0, outputBytes, 0, byteCount);
    return outputBytes;
  }

  /// <summary>
  /// Compress input bytes.
  /// </summary>
  /// <param name="inputBytes">Bytes to compress.</param>
  /// <param name="outputBuffer">Output/work buffer. Upon completion, will contain the output.</param>
  /// <returns>Length of output.</returns>
  public static int Compress(byte[] inputBytes, ref byte[] outputBuffer) => 
    Compress(inputBytes, ref outputBuffer, inputBytes.Length);

  /// <summary>
  /// Compress input bytes.
  /// </summary>
  /// <param name="inputBytes">Bytes to compress.</param>
  /// <param name="output">Output buffer. This may not be the same buffer by the time the function completes.</param>
  /// <param name="inputLength">Length of data in inputBytes.</param>
  /// <returns>Length of output.</returns>
  public static unsafe int Compress(byte[] input, ref byte[] output, int len) {
    // If byteCount is 0, increase buffer size and try again.
    int outputSize = Math.Min(1, input.Length);
    fixed (byte* inputPtr = input) {
      while (true) {
        byte* buffer = stackalloc byte[outputSize];
        outputSize *= 2;
        int count = TryCompress(inputPtr, buffer, len, outputSize);
        if (count == 0) continue;
        CopyBuffer(buffer, ref output, count);
        return count;
      }
    }
  }

  /// <summary>
  /// Decompress input bytes.
  /// </summary>
  /// <param name="inputBytes">Bytes to decompress.</param>
  /// <returns>Decompressed bytes.</returns>
  public static byte[] Decompress(byte[] input) => Decompress(input, input.Length);

  /// <summary>
  /// Decompress input bytes.
  /// </summary>
  /// <param name="inputBytes">Bytes to decompress.</param>
  /// <param name="inputLength">Length of data in inputBytes to decompress.</param>
  /// <returns>Decompressed bytes.</returns>
  public static byte[] Decompress(byte[] input, int len) {
    byte[] temp = null;
    int count = Decompress(input, ref temp, len);

    byte[] output = new byte[count];
    Buffer.BlockCopy(temp, 0, output, 0, count);
    return output;
  }

  /// <summary>
  /// Decompress input bytes.
  /// </summary>
  /// <param name="inputBytes">Bytes to decompress.</param>
  /// <param name="outputBuffer">Output/work buffer. Upon completion, will contain the output.</param>
  /// <returns>Length of output.</returns>
  public static int Decompress(byte[] input, ref byte[] output) => 
    Decompress(input, ref output, input.Length);

  /// <summary>
  /// Decompress input bytes.
  /// </summary>
  /// <param name="input">Bytes to decompress.</param>
  /// <param name="outputBuffer">Output/work buffer. Upon completion, will contain the output.</param>
  /// <param name="inputLength">Length of data in inputBytes.</param>
  /// <returns>Length of output.</returns>
  public static unsafe int Decompress(byte[] input, ref byte[] output, int inputLength) {
    // If byteCount is 0, increase buffer size and try again.
    int outputSize = input.Length;
    fixed (byte* inputPtr = input) {
      while (true) {
        byte* buffer = stackalloc byte[outputSize];
        int count = TryDecompress(inputPtr, buffer, inputLength, outputSize);
        outputSize *= 2;
        if (count == 0) continue;
        CopyBuffer(buffer, ref output, count);
        return count;
      }
    }
  }

  static unsafe byte[] CopyBuffer(byte* buffer, ref byte[] output, int count) {
    if (output == null || count > output.Length) {
      var pool = ArrayPool<byte>.Shared; 
      if (output != null) pool.Return(output);
      output = pool.Rent(count);
    }
    fixed (byte* outputPtr = output) {
      UnsafeUtility.MemCpy(outputPtr, buffer, count);
    }
    return output;
  }

  /// <summary>
  /// Attempts to compress data using LibLZF algorithm.
  /// </summary>
  /// <param name="src">Reference to the data to compress.</param>
  /// <param name="dst">Reference to a buffer which will contain the compressed data.</param>
  /// <param name="srcLen">Length of input bytes to process.</param>
  /// <returns>The size of the compressed archive in the output buffer. If non-positive, compression failed.</returns>
  public static unsafe int TryCompress(byte* src, byte* dst, int srcLen, int dstLen) {
    long hslot;
    uint iidx = 0;
    uint oidx = 0;
    long reference;

    uint hval = (uint)((*src << 8) | src[1]); // FRST(in_data, iidx);
    long off;
    int lit = 0;

    // Lock so we have exclusive access to hashtable.
    lock (hashTableLock) {
      UnsafeUtility.MemClear(HashTable, HSIZE * sizeof(long));
      for (;;) {
        if (iidx < srcLen - 2) {
          hval = (hval << 8) | src[iidx + 2];
          hslot = ((hval ^ (hval << 5)) >> (int)(((3 * 8 - HLOG)) - hval * 5) & (HSIZE - 1));
          reference = HashTable[hslot];
          HashTable[hslot] = (long)iidx;

          if ((off = iidx - reference - 1) < MAX_OFF
              && iidx + 4 < srcLen
              && reference > 0
              && src[reference + 0] == src[iidx + 0]
              && src[reference + 1] == src[iidx + 1]
              && src[reference + 2] == src[iidx + 2]
              )
          {
            /* match found at *reference++ */
            uint len = 2;
            uint maxlen = (uint)srcLen - iidx - len;
            maxlen = maxlen > MAX_REF ? MAX_REF : maxlen;

            if (oidx + lit + 1 + 3 >= dstLen) return 0;

            do {
              len++;
            } while (len < maxlen && src[reference + len] == src[iidx + len]);

            if (lit != 0) {
              dst[oidx++] = (byte)(lit - 1);
              lit = -lit;
              do {
                dst[oidx++] = src[iidx + lit];
              } while ((++lit) != 0);
            }

            len -= 2;
            iidx++;

            if (len < 7) {
              dst[oidx++] = (byte)((off >> 8) + (len << 5));
            } else {
              dst[oidx++] = (byte)((off >> 8) + (7 << 5));
              dst[oidx++] = (byte)(len - 7);
            }

            dst[oidx++] = (byte)off;

            iidx += len - 1;
            hval = (uint)(((src[iidx]) << 8) | src[iidx + 1]);

            hval = (hval << 8) | src[iidx + 2];
            HashTable[((hval ^ (hval << 5)) >> (int)(((3 * 8 - HLOG)) - hval * 5) & (HSIZE - 1))] = iidx;
            iidx++;

            hval = (hval << 8) | src[iidx + 2];
            HashTable[((hval ^ (hval << 5)) >> (int)(((3 * 8 - HLOG)) - hval * 5) & (HSIZE - 1))] = iidx;
            iidx++;
            continue;
          }
        } else if (iidx == srcLen) {
          break;
        }

        /* one more literal byte we must copy */
        lit++;
        iidx++;

        if (lit == MAX_LIT) {
          if (oidx + 1 + MAX_LIT >= dstLen) return 0;

          dst[oidx++] = (byte)(MAX_LIT - 1);
          lit = -lit;
          do {
            dst[oidx++] = src[iidx + lit];
          } while ((++lit) != 0);
        }
      } // for
    } // lock

    if (lit != 0) {
      if (oidx + lit + 1 >= dstLen)
          return 0;

      dst[oidx++] = (byte)(lit - 1);
      lit = -lit;
      do {
        dst[oidx++] = src[iidx + lit];
      } while ((++lit) != 0);
    }

    return (int)oidx;
  }

  /// <summary>
  /// Decompresses the data using LibLZF algorithm.
  /// </summary>
  /// <param name="src">Reference to the data to decompress.</param>
  /// <param name="dst">Reference to a buffer which will contain the decompressed data.</param>
  /// <param name="srcLen">Length of input bytes to process.</param>
  /// <returns>The size of the decompressed archive in the output buffer.</returns>
  public static unsafe int TryDecompress(byte* src, byte* dst, int srcLen, int dstLen) {
    byte* srcEnd = src + srcLen;
    byte* dstStart = dst;
    byte* dstEnd = dst + dstLen;

    do {
      uint ctrl = *src++;

      if (ctrl < (1 << 5))  { /* literal run */
        ctrl++;

        if (dst + ctrl > dstEnd) return 0;

        do {
          *dst++ = *src++;
        } while ((--ctrl) != 0);
      } else { /* back reference */
        uint len = ctrl >> 5;

        int reference = (int)((dst - dstStart) - ((ctrl & 0x1f) << 8) - 1);

        if (len == 7) len += *src++;

        reference -= *src++;

        if (dst + len + 2 > dstEnd) return 0;
        if (reference < 0) return 0;

        byte* refPtr = dstStart + reference;

        *dst++ = *refPtr++;
        *dst++ = *refPtr++;

        do {
          *dst++ = *refPtr++;
        } while ((--len) != 0);
      }
    } while (src < srcEnd);
    return (int)(dst - dstStart);
  }

}

}
