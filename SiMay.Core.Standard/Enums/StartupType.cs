﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SiMay.Core
{
    public enum StartupType
    {
        LocalMachineRun,
        LocalMachineRunOnce,
        CurrentUserRun,
        CurrentUserRunOnce,
        LocalMachineWoW64Run,
        LocalMachineWoW64RunOnce,
        StartMenu
    }
}
