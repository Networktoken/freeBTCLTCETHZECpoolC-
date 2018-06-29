

using System;
using System.Data;
using System.Diagnostics;
using System.Threading.Tasks;
using XPool.Persistence;
using NLog;

namespace XPool.extensions
{
    public static class ConnectionFactoryExtensions
    {
                                public static void Run(this IConnectionFactory factory, Action<IDbConnection> action)
        {
            using(var con = factory.OpenConnection())
            {
                action(con);
            }
        }

                                        public static T Run<T>(this IConnectionFactory factory, Func<IDbConnection, T> action)
        {
            using(var con = factory.OpenConnection())
            {
                return action(con);
            }
        }

                                public static void Run(this IConnectionFactory factory, Action<IDbConnection> action, Stopwatch sw, ILogger logger)
        {
            using (var con = factory.OpenConnection())
            {
                sw.Reset();
                sw.Start();

                action(con);

                sw.Stop();
                logger.Debug(()=> $"Query took {sw.ElapsedMilliseconds} ms");
            }
        }

                                        public static T Run<T>(this IConnectionFactory factory, Func<IDbConnection, T> action, Stopwatch sw, ILogger logger)
        {
            using (var con = factory.OpenConnection())
            {
                sw.Reset();
                sw.Start();

                var result = action(con);

                sw.Stop();
                logger.Debug(() => $"Query took {sw.ElapsedMilliseconds} ms");
                return result;
            }
        }

                                        public static async Task<T> RunAsync<T>(this IConnectionFactory factory,
            Func<IDbConnection, Task<T>> action)
        {
            using(var con = factory.OpenConnection())
            {
                return await action(con);
            }
        }

                                        public static void RunTx(this IConnectionFactory factory,
            Action<IDbConnection, IDbTransaction> action,
            bool autoCommit = true, IsolationLevel isolation = IsolationLevel.ReadCommitted)
        {
            using(var con = factory.OpenConnection())
            {
                using(var tx = con.BeginTransaction(isolation))
                {
                    try
                    {
                        action(con, tx);

                        if (autoCommit)
                            tx.Commit();
                    }

                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }

                                                public static T RunTx<T>(this IConnectionFactory factory,
            Func<IDbConnection, IDbTransaction, T> action,
            bool autoCommit = true, IsolationLevel isolation = IsolationLevel.ReadCommitted)
        {
            using(var con = factory.OpenConnection())
            {
                using(var tx = con.BeginTransaction(isolation))
                {
                    try
                    {
                        var result = action(con, tx);

                        if (autoCommit)
                            tx.Commit();

                        return result;
                    }

                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }

                                                public static async Task<T> RunTxAsync<T>(this IConnectionFactory factory,
            Func<IDbConnection, IDbTransaction, Task<T>> action,
            bool autoCommit = true, IsolationLevel isolation = IsolationLevel.ReadCommitted)
        {
            using(var con = factory.OpenConnection())
            {
                using(var tx = con.BeginTransaction(isolation))
                {
                    try
                    {
                        var result = await action(con, tx);

                        if (autoCommit)
                            tx.Commit();

                        return result;
                    }

                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }

                                                public static async Task RunTxAsync(this IConnectionFactory factory,
            Func<IDbConnection, IDbTransaction, Task> action,
            bool autoCommit = true, IsolationLevel isolation = IsolationLevel.ReadCommitted)
        {
            using(var con = factory.OpenConnection())
            {
                using(var tx = con.BeginTransaction(isolation))
                {
                    try
                    {
                        await action(con, tx);

                        if (autoCommit)
                            tx.Commit();
                    }

                    catch
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }
    }
}
