

using System.Linq;
using XPool.extensions;

namespace XPool.core.crypto.hash.special
{
    public class DigestReverser : IHashAlgorithm
    {
        public DigestReverser(IHashAlgorithm upstream)
        {
            this.upstream = upstream;
        }

        private readonly IHashAlgorithm upstream;

        public byte[] Digest(byte[] data, params object[] extra)
        {
            return upstream.Digest(data, extra).ReverseArray();
        }
    }
}
