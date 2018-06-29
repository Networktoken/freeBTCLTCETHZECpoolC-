﻿using System;
using System.Net;

namespace XPool.core.coindintf
{
    public class DaemonClientException : Exception
    {
        public DaemonClientException(string msg) : base(msg)
        {
        }

        public DaemonClientException(HttpStatusCode code, string msg) : base(msg)
        {
            Code = code;
        }

        public HttpStatusCode Code { get; set; }
    }
}
