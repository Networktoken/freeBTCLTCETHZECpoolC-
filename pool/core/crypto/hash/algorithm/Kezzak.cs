

using System;
using System.Linq;
using XPool.utils;
using XPool.extensions;
using XPool.core.crypto.native;

namespace XPool.core.crypto.hash.algorithm
{
    public unsafe class Kezzak : IHashAlgorithm
    {
        public byte[] Digest(byte[] data, params object[] extra)
        {
            Assertion.RequiresNonNull(data, nameof(data));
            Assertion.RequiresNonNull(extra, nameof(extra));
            Assertion.Requires<ArgumentException>(extra.Length > 0, $"{nameof(extra)} must not be empty");

                        var nTime = (ulong) extra[0];
            var dataEx = data.Concat(nTime.ToString("X").HexToByteArray()).ToArray();

            var result = new byte[32];

            fixed(byte* input = dataEx)
            {
                fixed(byte* output = result)
                {
                    LibMultihash.kezzak(input, output, (uint) data.Length);
                }
            }

            return result;
        }
    }
}
