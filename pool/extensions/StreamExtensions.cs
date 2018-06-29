﻿

using System.IO;

namespace XPool.extensions
{
    public static class StreamExtensions
    {
        public static void Write(this Stream stream, byte[] data)
        {
            stream.Write(data, 0, data.Length);
        }
    }
}
