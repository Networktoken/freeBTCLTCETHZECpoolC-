

using System.Security.Cryptography;
using XPool.utils;

namespace XPool.core.crypto.hash.algorithm
{
                public class Sha256S : IHashAlgorithm
    {
        public byte[] Digest(byte[] data, params object[] extra)
        {
            Assertion.RequiresNonNull(data, nameof(data));

            using(var hasher = SHA256.Create())
            {
                return hasher.ComputeHash(data);
            }
        }
    }
}
