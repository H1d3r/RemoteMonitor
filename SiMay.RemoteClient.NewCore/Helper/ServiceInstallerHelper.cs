using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SiMay.ServiceCore
{
    public class ServiceInstallerHelper
    {
        #region public Variables  
        private const int SC_MANAGER_CREATE_SERVICE = 0x0002;
        private const int SERVICE_WIN32_OWN_PROCESS = 0x00000010;
        private const int SERVICE_DEMAND_START = 0x00000003;
        private const int SERVICE_ERROR_NORMAL = 0x00000001;
        private const int STANDARD_RIGHTS_REQUIRED = 0xF0000;
        private const int SERVICE_QUERY_CONFIG = 0x0001;
        private const int SERVICE_CHANGE_CONFIG = 0x0002;
        private const int SERVICE_QUERY_STATUS = 0x0004;
        private const int SERVICE_ENUMERATE_DEPENDENTS = 0x0008;
        private const int SERVICE_START = 0x0010;
        private const int SERVICE_STOP = 0x0020;
        private const int SERVICE_PAUSE_CONTINUE = 0x0040;
        private const int SERVICE_INTERROGATE = 0x0080;
        private const int SERVICE_USER_DEFINED_CONTROL = 0x0100;
        private const int SERVICE_ALL_ACCESS = (STANDARD_RIGHTS_REQUIRED |
         SERVICE_QUERY_CONFIG |
         SERVICE_CHANGE_CONFIG |
         SERVICE_QUERY_STATUS |
         SERVICE_ENUMERATE_DEPENDENTS |
         SERVICE_START |
         SERVICE_STOP |
         SERVICE_PAUSE_CONTINUE |
         SERVICE_INTERROGATE |
         SERVICE_USER_DEFINED_CONTROL);
        private const int SERVICE_AUTO_START = 0x00000002;
        private const int GENERIC_WRITE = 0x40000000;
        private const int DELETE = 0x10000;
        #endregion

        #region DLLImport  
        [DllImport("advapi32.dll")]
        private static extern IntPtr OpenSCManager(string lpMachineName, string lpSCDB, int scParameter);
        [DllImport("advapi32.dll")]
        private static extern IntPtr CreateService(IntPtr SC_HANDLE, string lpSvcName, string lpDisplayName,
         int dwDesiredAccess, int dwServiceType, int dwStartType, int dwErrorControl, string lpPathName,
         string lpLoadOrderGroup, int lpdwTagId, string lpDependencies, string lpServiceStartName, string lpPassword);
        [DllImport("advapi32.dll")]
        private static extern void CloseServiceHandle(IntPtr SCHANDLE);
        [DllImport("advapi32.dll")]
        private static extern int StartService(IntPtr SVHANDLE, int dwNumServiceArgs, string lpServiceArgVectors);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern IntPtr OpenService(IntPtr SCHANDLE, string lpSvcName, int dwNumServiceArgs);
        [DllImport("advapi32.dll")]
        private static extern int DeleteService(IntPtr SVHANDLE);
        [DllImport("kernel32.dll")]
        private static extern int GetLastError();
        #endregion DLLImport  


        public static bool InstallService(string svcPath, string svcName, string svcDispName)
        {
            try
            {
                // 获得服务控制管理器句柄  
                IntPtr sc_handle = OpenSCManager(null, null, SC_MANAGER_CREATE_SERVICE);
                if (sc_handle.ToInt64() != 0)
                {
                    //获得服务句柄  
                    IntPtr sv_handle = CreateService(sc_handle, svcName, svcDispName, SERVICE_ALL_ACCESS, SERVICE_WIN32_OWN_PROCESS, SERVICE_AUTO_START, SERVICE_ERROR_NORMAL, svcPath, null, 0, null, null, null);
                    if (sv_handle.ToInt64() == 0)
                    {
                        CloseServiceHandle(sc_handle);
                        return false;
                    }
                    else
                    {
                        //试尝启动服务  
                        int i = StartService(sv_handle, 0, null);
                        if (i == 0)
                        {
                            return false;
                        }
                        CloseServiceHandle(sc_handle);
                        return true;
                    }
                }
                else
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool UnInstallService(string svcName)
        {
            bool returnValue = false;
            try
            {
                IntPtr sc_handle = OpenSCManager(null, null, GENERIC_WRITE);
                if (sc_handle.ToInt64() != 0)
                {
                    IntPtr sv_handle = OpenService(sc_handle, svcName, DELETE);
                    if (sv_handle.ToInt64() != 0)
                    {
                        if (DeleteService(sv_handle) != 0)
                        {
                            CloseServiceHandle(sv_handle);
                            returnValue = true;
                        }
                        else
                        {
                            CloseServiceHandle(sv_handle);
                        }
                    }
                }
            }
            catch { }
            return returnValue;
        }
    }
}
