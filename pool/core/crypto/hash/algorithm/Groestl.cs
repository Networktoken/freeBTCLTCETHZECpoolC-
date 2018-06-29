

using XPool.utils;
using XPool.core.crypto.native;

namespace XPool.core.crypto.hash.algorithm
{
    public unsafe class Groestl : IHashAlgorithm
    {
        public byte[] Digest(byte[] data, params object[] extra)
        {
            Assertion.RequiresNonNull(data, nameof(data));

            var result = new byte[32];

            fixed(byte* input = data)
            {
                fixed(byte* output = result)
                {
                    LibMultihash.groestl(input, output, (uint) data.Length);
                }
            }

            return result;
        }
    }
}
