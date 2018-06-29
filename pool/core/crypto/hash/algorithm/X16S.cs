

using XPool.utils;
using XPool.core.crypto.native;

namespace XPool.core.crypto.hash.algorithm
{
    public unsafe class X16S : IHashAlgorithm
    {
        public byte[] Digest(byte[] data, params object[] extra)
        {
            Assertion.RequiresNonNull(data, nameof(data));

            var result = new byte[32];

            fixed(byte* input = data)
            {
                fixed(byte* output = result)
                {
                    LibMultihash.x16s(input, output, (uint) data.Length);
                }
            }

            return result;
        }
    }
}
