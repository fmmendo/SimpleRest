﻿#if !Smartphone
using System.Diagnostics;
#endif

namespace SimpleRest.Authenticators.OAuth
{
#if !Smartphone
    [DebuggerDisplay("{Name}:{Value}")]
#endif
    internal class WebParameter : WebPair
    {
        public WebParameter(string name, string value)
            : base(name, value)
        {
        }
    }
}