

using XPool.utils;
using XPool.core.crypto.native;

namespace XPool.core.crypto.hash.algorithm
{
    public unsafe class Scrypt : IHashAlgorithm
    {
        public Scrypt(uint n, uint r)
        {
            this.n = n;
            this.r = r;
        }

        private readonly uint n;
        private readonly uint r;

        public byte[] Digest(byte[] data, params object[] extra)
        {
            Assertion.RequiresNonNull(data, nameof(data));

            var result = new byte[32];

            fixed (byte* input = data)
            {
                fixed (byte* output = result)
                {
                    LibMultihash.scrypt(input, output, n, r, (uint)data.Length);
                }
            }

            return result;
        }
    }
}
