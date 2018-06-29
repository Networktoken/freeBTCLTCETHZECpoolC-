

using System.Security.Cryptography;
using XPool.utils;
using XPool.core.crypto.native;

namespace XPool.core.crypto.hash.algorithm
{
                public unsafe class Sha3_256 : IHashAlgorithm
    {
        public byte[] Digest(byte[] data, params object[] extra)
        {
            Assertion.RequiresNonNull(data, nameof(data));

            var result = new byte[32];

            fixed(byte* input = data)
            {
                fixed(byte* output = result)
                {
                    LibMultihash.sha3_256(input, output, (uint) data.Length);
                }
            }

            return result;
        }
    }
}
