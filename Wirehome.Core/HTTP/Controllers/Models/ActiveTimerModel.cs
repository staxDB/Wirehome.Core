﻿using Wirehome.Core.Python.Models;

namespace Wirehome.Core.HTTP.Controllers.Models
{
    public class ActiveTimerModel
    {
        public int Interval { get; set; }

        public ExceptionPythonModel LastException { get; set; }

        public long LastDuration { get; set; }
    }
}
