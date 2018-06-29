

using System;
using System.Linq;
using XPool.utils;
using XPool.core.crypto.native;

namespace XPool.core.crypto.hash.algorithm
{
    public unsafe class ScryptN : IHashAlgorithm
    {
        public ScryptN(IMasterClock clock, Tuple<long, long>[] timetable = null)
        {
            this.timetable = timetable ?? defaultTimetable;
            this.clock = clock;
        }

        private readonly Tuple<long, long>[] timetable;
        private readonly IMasterClock clock;

        private static readonly Tuple<long, long>[] defaultTimetable = new[]
        {
            Tuple.Create(2048L, 1389306217L),
            Tuple.Create(4096L, 1456415081L),
            Tuple.Create(8192L, 1506746729L),
            Tuple.Create(16384L, 1557078377L),
            Tuple.Create(32768L, 1657741673L),
            Tuple.Create(65536L, 1859068265L),
            Tuple.Create(131072L, 2060394857L),
            Tuple.Create(262144L, 1722307603L),
            Tuple.Create(524288L, 1769642992L),
        }.OrderByDescending(x => x.Item1).ToArray();

        public byte[] Digest(byte[] data, params object[] extra)
        {
            Assertion.RequiresNonNull(data, nameof(data));

                        var ts = ((DateTimeOffset) clock.Now).ToUnixTimeSeconds();
            var n = timetable.First(x => ts >= x.Item2).Item1;
            var nFactor = Math.Log(n) / Math.Log(2);

            var result = new byte[32];

            fixed(byte* input = data)
            {
                fixed(byte* output = result)
                {
                    LibMultihash.scryptn(input, output, (uint) nFactor, (uint) data.Length);
                }
            }

            return result;
        }
    }
}
