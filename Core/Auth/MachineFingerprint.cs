using System.Management;
using Microsoft.Win32;

namespace ChatRoom.Core.Auth
{
    /// <summary>
    /// 读取机器指纹：CPU / 主板 / BIOS / 磁盘 / 物理网卡 MAC / 系统安装时间。
    /// 组合多种源保证唯一性：个别电脑读不到 CPU/主板序列号（尤其统一镜像部署），
    /// 且系统安装时间相同，仅靠两者会产生重复注册码。
    /// </summary>
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

        /// <summary>BIOS 序列号（Win32_BIOS.SerialNumber）。</summary>
        public static string GetBiosSerial()
        {
            return WmiFirst("SELECT SerialNumber FROM Win32_BIOS", "SerialNumber");
        }

        /// <summary>物理磁盘序列号（Win32_DiskDrive，主盘 Index=0）。</summary>
        public static string GetDiskSerial()
        {
            return WmiFirst("SELECT SerialNumber FROM Win32_DiskDrive WHERE Index = 0", "SerialNumber");
        }

        /// <summary>物理网卡 MAC 地址（去掉分隔符、转大写；同一镜像克隆的机器 MAC 通常仍各不相同）。</summary>
        public static string GetPhysicalMac()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT MACAddress FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE AND MACAddress IS NOT NULL"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var v = obj["MACAddress"];
                        if (v != null)
                        {
                            var s = v.ToString().Replace(":", "").Replace("-", "").ToUpperInvariant();
                            if (s.Length >= 12)
                                return s;
                        }
                    }
                }
            }
            catch { /* 网卡不可用时返回空 */ }
            return "";
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
