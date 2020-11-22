﻿using SiMay.Core;
using SiMay.ModelBinder;
using SiMay.Net.SessionProvider;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SiMay.Service.Core
{
    public class ConfiguartionSimpleService : RemoteSimpleServiceBase
    {
        [PacketHandler(MessageHead.S_SIMPLE_DES)]
        public void SetDescribe(SessionProviderContext session)
        {
            var des = session.GetMessage().ToUnicodeString();
            AppConfiguration.GetApplicationConfiguration<AppConfiguration>().Describe = des;
            AppConfiguration.GetApplicationConfiguration<AppConfiguration>().Flush();
        }

        [PacketHandler(MessageHead.S_SIMPLE_GROUP_NAME)]
        public void SetGroupName(SessionProviderContext session)
        {
            var groupName = session.GetMessage().ToUnicodeString();
            AppConfiguration.GetApplicationConfiguration<AppConfiguration>().GroupName = groupName;
            AppConfiguration.GetApplicationConfiguration<AppConfiguration>().Flush();
        }
    }
}
