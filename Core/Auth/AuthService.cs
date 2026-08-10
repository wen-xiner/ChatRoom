using System;
using System.Text;
using ChatRoom.Config;

namespace ChatRoom.Core.Auth
{
    /// <summary>
    /// 认证与身份服务：注册码、认证码校验、注册表写入、20 位用户 ID 拼装。
    /// 认证状态决定 发送/接收 图片、文件 是否可用（模块4 门禁）。
    /// </summary>
    public static class AuthService
    {
        /// <summary>
        /// 本机注册码（组合多种硬件指纹 → SHA256）。
        /// 组合 CPU/主板/BIOS/磁盘/物理网卡 MAC/系统安装时间，任一项读取失败不影响整体唯一性；
        /// 修复个别电脑因 CPU/主板序列号读不到、且安装时间相同（统一镜像部署）导致的注册码重复。
        /// </summary>
        public static string GetRegCode()
        {
            return AuthAlgorithm.ComputeRegCode(
                MachineFingerprint.GetCpuSerial(),
                MachineFingerprint.GetMotherboardSerial(),
                MachineFingerprint.GetBiosSerial(),
                MachineFingerprint.GetDiskSerial(),
                MachineFingerprint.GetPhysicalMac(),
                MachineFingerprint.GetSystemInstallTime());
        }

        /// <summary>本机注册码应对应的正确认证码。</summary>
        public static string GetExpectedAuthCode()
        {
            return AuthAlgorithm.ComputeAuthCode(GetRegCode());
        }

        /// <summary>当前机器是否已认证（注册表认证码与正确值一致）。</summary>
        public static bool IsAuthenticated()
        {
            var stored = RegistryHelper.GetValue(AppConfig.RegKeyAuth, AppConfig.RegValueAuthCode);
            if (string.IsNullOrWhiteSpace(stored))
                return false;
            return string.Equals(stored.Trim(), GetExpectedAuthCode(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 注册：校验认证码并写入注册表。
        /// </summary>
        public static bool TryRegister(string authCodeInput, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(authCodeInput))
            {
                error = "请输入认证码。";
                return false;
            }
            if (!string.Equals(authCodeInput.Trim(), GetExpectedAuthCode(), StringComparison.OrdinalIgnoreCase))
            {
                error = "认证码不正确。请用注册机根据本机注册码生成后重试。";
                return false;
            }
            RegistryHelper.SetValue(AppConfig.RegKeyAuth, AppConfig.RegValueAuthCode, authCodeInput.Trim());
            return true;
        }

        /// <summary>
        /// 20 位用户 ID = 安装时写入注册表的 4 位随机字符 + 注册码后 16 位。
        /// 开发/未安装场景首次运行自动生成随机 4 字符并持久化。
        /// </summary>
        public static string GetUserId()
        {
            var random4 = RegistryHelper.GetValue(AppConfig.RegKeyInstall, AppConfig.RegValueUserIdRandom);
            if (random4.Length != 4)
            {
                random4 = GenerateRandom4();
                RegistryHelper.SetValue(AppConfig.RegKeyInstall, AppConfig.RegValueUserIdRandom, random4);
            }
            var regCode = GetRegCode();
            var last16 = regCode.Substring(Math.Max(0, regCode.Length - 16));
            return (random4 + last16).ToUpperInvariant();
        }

        private static string GenerateRandom4()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 去掉易混淆的 I/O/0/1
            var rnd = new Random();
            var sb = new StringBuilder(4);
            for (var i = 0; i < 4; i++)
                sb.Append(chars[rnd.Next(chars.Length)]);
            return sb.ToString();
        }
    }
}
