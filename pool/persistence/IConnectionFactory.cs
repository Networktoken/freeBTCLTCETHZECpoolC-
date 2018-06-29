

using System.Data;

namespace XPool.Persistence
{
    public interface IConnectionFactory
    {
        IDbConnection OpenConnection();
    }
}
