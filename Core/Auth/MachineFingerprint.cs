using System;
using System.Management;
using Microsoft.Win32;

namespace ChatRoom.Core.Auth
{
    /// <summary>
    /// 读机器指纹：CPU / 主板 / BIOS / 磁盘 / 机器 UUID / 系统安装时间。
    /// 多凑几样免得某台机器序列号读不到导致注册码重了。
    /// </summary>
    public static class MachineFingerprint
    {
        /// <summary>把 CPU 序列号抠出来（Win32_Processor.ProcessorId）。</summary>
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

        /// <summary>
        /// 机器 UUID（Win32_ComputerSystemProduct.UUID，SMBIOS 全局唯一）。
        /// 物理机厂商写死、虚拟机每台不一样；比 MAC 稳，不受多网卡/网卡启停影响。
        /// </summary>
        public static string GetMachineUuid()
        {
            var v = WmiFirst("SELECT UUID FROM Win32_ComputerSystemProduct", "UUID");
            if (string.IsNullOrWhiteSpace(v)) return "";
            var s = v.Trim().ToUpperInvariant();
            // 滤掉厂商占位值（主板没写 UUID 时返回全 F / 全 0），
            // 免得多台机器共用同一个占位值。
            if (s == "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF" ||
                s == "00000000-0000-0000-0000-000000000000")
                return "";
            return s;
        }

        /// <summary>系统安装时间（注册表 InstallDate，Unix 秒），稳定不变。</summary>
        public static string GetSystemInstallTime()
        {
            try
            {
                // 显式开 64 位视图，绕开 WOW64 注册表重定向：
                // 32 位进程默认读 32 位视图，HKLM\SOFTWARE 会被重定向到 Wow6432Node，
                // 那里的 InstallDate 常为 0/不存在，导致指纹读空、多台机器注册码一致。
                // 32 位系统上没有 64 位视图，退回默认即可。
                var view = Environment.Is64BitOperatingSystem
                    ? RegistryView.Registry64
                    : RegistryView.Default;
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    var val = key?.GetValue("InstallDate");
                    if (val != null)
                        return val.ToString();
                }
            }
            catch { /* 注册表读不了就返回空 */ }
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
            catch { /* WMI 挂了就返回空 */ }
            return "";
        }
    }
}
