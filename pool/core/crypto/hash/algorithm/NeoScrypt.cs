

using System;
using XPool.utils;
using XPool.core.crypto.native;

namespace XPool.core.crypto.hash.algorithm
{
    public unsafe class NeoScrypt : IHashAlgorithm
    {
        public NeoScrypt(uint profile)
        {
            this.profile = profile;
        }

        private readonly uint profile;

        public byte[] Digest(byte[] data, params object[] extra)
        {
            Assertion.RequiresNonNull(data, nameof(data));
            Assertion.Requires<ArgumentException>(data.Length == 80, $"{nameof(data)} length must be exactly 80 bytes");

            var result = new byte[32];

            fixed (byte* input = data)
            {
                fixed (byte* output = result)
                {
                    LibMultihash.neoscrypt(input, output, (uint)data.Length, profile);
                }
            }

            return result;
        }
    }
}
