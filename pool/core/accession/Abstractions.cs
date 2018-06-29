

using System;
using System.Net;

namespace XPool.core.accession
{
    public interface IBanManager
    {
        bool IsBanned(IPAddress address);
        void Ban(IPAddress address, TimeSpan duration);
    }
}
