using System.Management;
using Microsoft.Win32;

namespace ChatRoom.Core.Auth
{
    /// <summary>读取机器指纹：CPU 序列号 + 主板序列号 + 系统安装时间。</summary>
    public static class MachineFingerprint
    {
        /// <summary>CPU 序列号（Win32_Processor.ProcessorId）。</summary>
        public static string GetCpuSerial()
        {
            return WmiFirst("SELECT ProcessorId FROM Win32_Processor", "ProcessorId");
        }

        /// <summary>主板序列号（Win32_BaseBoard.SerialNumber）。</summary>
        public static string GetMotherboardSerial()
        {
            return WmiFirst("SELECT SerialNumber FROM Win32_BaseBoard", "SerialNumber");
        }

        /// <summary>系统安装时间（注册表 InstallDate，Unix 秒），稳定不变。</summary>
        public static string GetSystemInstallTime()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    var val = key?.GetValue("InstallDate");
                    if (val != null)
                        return val.ToString();
                }
            }
            catch { /* 忽略，注册表不可读时返回空 */ }
            return "";
        }

        private static string WmiFirst(string query, string property)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var v = obj[property];
                        if (v != null)
                        {
                            var s = v.ToString().Trim();
                            if (s.Length > 0)
                                return s;
                        }
                    }
                }
            }
            catch { /* WMI 不可用时返回空 */ }
            return "";
        }
    }
}
