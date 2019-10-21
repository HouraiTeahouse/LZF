using System;
using System.Linq;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace HouraiTeahouse.Compression {

public class CLZF2Test {
    
    System.Random random;

    [SetUp]
    public void Setup() {
        random = new System.Random();
    }

    byte [] CreateRandomArray(int length) {
        var val = new byte[length];
        for (var i = 0; i < val.Length; i++) {
            val[i] = (byte)random.Next();
        }
        return val;
    }

	[Test]
    [TestCase(10), TestCase(100), TestCase(1000), TestCase(10000)]
	public unsafe void CLZF2CompressionTests(int length) {
        var ratio = new List<double>();
        for (var i = 0; i < 1000; i++) {
            var input = CreateRandomArray(length);
            var compressed = CLZF2.Compress(input);
            var decompressed = CLZF2.Decompress(compressed);
            CollectionAssert.AreEqual(input, decompressed);
            ratio.Add((double)compressed.Length / (double) input.Length);
        }
        Debug.Log($"CLZF2 Average Compression Ratio ({length}): {ratio.Average()}.");
	}

}

}