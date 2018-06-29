

using System.Data;
using Npgsql;

namespace XPool.Persistence.Postgres
{
    public class PgConnectionFactory : IConnectionFactory
    {
        public PgConnectionFactory(string connectionString)
        {
            this.connectionString = connectionString;
        }

        private readonly string connectionString;

                                        public IDbConnection OpenConnection()
        {
            var con = new NpgsqlConnection(connectionString);
            con.Open();
            return con;
        }
    }
}
