using System;
using System.Collections.Generic;
using System.Text;

namespace XPool.utils
{
    public class StandardClock : IMasterClock
    {
        public DateTime Now => DateTime.UtcNow;
    }
}
