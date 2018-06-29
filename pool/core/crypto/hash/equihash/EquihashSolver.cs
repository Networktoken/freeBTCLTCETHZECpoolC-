

using System;
using System.Threading;
using XPool.utils;
using XPool.extensions;
using XPool.core.crypto.native;
using NLog;

namespace XPool.core.crypto.hash.equihash
{
    public unsafe class EquihashSolver
    {
        private EquihashSolver(int maxConcurrency)
        {
                                    sem = new Semaphore(maxConcurrency, maxConcurrency);
        }

        private static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        public static Lazy<EquihashSolver> Instance { get; } = new Lazy<EquihashSolver>(() =>
            new EquihashSolver(maxThreads));

        private static int maxThreads = 1;

        public static int MaxThreads
        {
            get => maxThreads;
            set
            {
                if(Instance.IsValueCreated)
                    throw new InvalidOperationException("Too late: singleton value already created");

                maxThreads = value;
            }
        }

        private readonly Semaphore sem;

                                                        public bool Verify(byte[] header, byte[] solution)
        {
            Assertion.RequiresNonNull(header, nameof(header));
            Assertion.Requires<ArgumentException>(header.Length == 140, $"{nameof(header)} must be exactly 140 bytes");
            Assertion.RequiresNonNull(solution, nameof(solution));
            Assertion.Requires<ArgumentException>(solution.Length == 1344, $"{nameof(solution)} must be exactly 1344 bytes");

            logger.LogInvoke();

            try
            {
                sem.WaitOne();

                fixed(byte *h = header)
                {
                    fixed(byte *s = solution)
                    {
                        return LibMultihash.equihash_verify(h, header.Length, s, solution.Length);
                    }
                }
            }

            finally
            {
                sem.Release();
            }
        }
    }
}
