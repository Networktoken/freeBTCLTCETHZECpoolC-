

namespace XPool.core.crypto
{
    public interface IHashAlgorithm
    {
        byte[] Digest(byte[] data, params object[] extra);
    }
}
