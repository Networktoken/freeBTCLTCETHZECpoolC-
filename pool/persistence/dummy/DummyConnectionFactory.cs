

using System;
using System.Data;

namespace XPool.Persistence.Dummy
{
    public class DummyConnectionFactory : IConnectionFactory
    {
        public DummyConnectionFactory(string connectionString)
        {
        }

                                        public IDbConnection OpenConnection()
        {
            throw new NotImplementedException();
        }
    }
}
