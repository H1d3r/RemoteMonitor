﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SiMay.Basic;
using SiMay.Core;
using SiMay.ModelBinder;
using SiMay.Net.SessionProvider;

namespace SiMay.RemoteControls.Core
{
    [SiMay.Core.ApplicationKey(ApplicationKeyConstant.REMOTE_STARTUP)]
    public class StartupAdapterHandler : ApplicationBaseAdapterHandler
    {
        public readonly IReadOnlyList<GroupItem> StartupGroupItems;

        public StartupAdapterHandler()
        {
            var starupItems = new List<GroupItem>();
            starupItems.Add(new GroupItem
            {
                StartupType = StartupType.LocalMachineRun,
                StartupPath = "HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run"
            });
            starupItems.Add(new GroupItem
            {
                StartupType = StartupType.LocalMachineRunOnce,
                StartupPath = "HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunOnce"
            });
            starupItems.Add(new GroupItem
            {
                StartupType = StartupType.CurrentUserRun,
                StartupPath = "HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run"
            });
            starupItems.Add(new GroupItem
            {
                StartupType = StartupType.CurrentUserRunOnce,
                StartupPath = "HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunOnce"
            });
            starupItems.Add(new GroupItem
            {
                StartupType = StartupType.LocalMachineWoW64Run,
                StartupPath = "HKEY_LOCAL_MACHINE\\SOFTWARE\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Run"
            });

            starupItems.Add(new GroupItem
            {
                StartupType = StartupType.LocalMachineWoW64RunOnce,
                StartupPath = "HKEY_LOCAL_MACHINE\\SOFTWARE\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\RunOnce"
            });
            starupItems.Add(new GroupItem
            {
                StartupType = StartupType.StartMenu,
                StartupPath = "%APPDATA%\\Microsoft\\Windows\\Start Menu\\Programs\\Startup"
            });

            StartupGroupItems = starupItems;
        }

        public async Task<StartupItemPacket[]> GetStartup()
        {
            var responsed = await SendTo(MessageHead.S_STARTUP_GET_LIST);
            if (!responsed.IsNull() && responsed.IsOK)
            {
                var pack = responsed.Datas.GetMessageEntity<StartupPacket>();
                return pack.StartupItems;
            }

            return null;
        }

        public async Task AddStartupItem(string path, string name, StartupType startupType)
        {
            await SendTo(MessageHead.S_STARTUP_ADD_ITEM,
                new StartupItemPacket()
                {
                    Name = name,
                    Path = path,
                    Type = startupType
                });
        }

        public async Task RemoveStartupItem(IEnumerable<StartupItemPacket> startupItems)
        {
            await SendTo(MessageHead.S_STARTUP_REMOVE_ITEM,
               new StartupPacket()
               {
                   StartupItems = startupItems.ToArray()
               });
        }
        public class GroupItem
        {
            public StartupType StartupType { get; set; }
            public string StartupPath { get; set; }
        }
    }
}
