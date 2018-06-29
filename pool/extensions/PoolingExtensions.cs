using System;
using System.Collections.Generic;
using System.Text;
using XPool.utils;

namespace XPool.extensions
{
    public static class PoolingExtensions
    {
        public static void Dispose<T>(this IEnumerable<PooledArraySegment<T>> col)
        {
            foreach(var seg in col)
                seg.Dispose();
        }
    }
}
