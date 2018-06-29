

using System;
using XPool.utils;
using XPool.core.crypto.native;

namespace XPool.core.crypto.hash.algorithm
{
    public unsafe class Lyra2Rev2 : IHashAlgorithm
    {
        public byte[] Digest(byte[] data, params object[] extra)
        {
            Assertion.RequiresNonNull(data, nameof(data));
            Assertion.Requires<ArgumentException>(data.Length == 80, $"{nameof(data)} must be exactly 80 bytes long");

            var result = new byte[32];

            fixed(byte* input = data)
            {
                fixed(byte* output = result)
                {
                    LibMultihash.lyra2rev2(input, output);
                }
            }

            return result;
        }
    }
}
