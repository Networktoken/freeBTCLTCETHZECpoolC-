

using System;

namespace XPool.core.crypto.hash.special
{
    public class DummyHasher : IHashAlgorithm
    {
        public byte[] Digest(byte[] data, params object[] extra)
        {
            throw new InvalidOperationException("Don't call me");
        }
    }
}
