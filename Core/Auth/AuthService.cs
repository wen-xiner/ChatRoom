using System;
using System.Text;
using ChatRoom.Config;

namespace ChatRoom.Core.Auth
{
    /// <summary>
    /// 管认证和身份：注册码、认证码校验、写注册表、拼 20 位用户 ID。
    /// 认没认证决定能不能发/收图片和文件（模块4 门禁）。
    /// </summary>
    public static class AuthService
    {
        /// <summary>
        /// 本机注册码（多种硬件指纹 → SHA256）。
        /// 拼 CPU/主板/BIOS/磁盘/UUID/安装时间，缺一项不影响唯一性。
        /// 特意用 UUID 而不用网卡 MAC——多网卡时 MAC 取哪块说不准。
        /// </summary>
        public static string GetRegCode()
        {
            return AuthAlgorithm.ComputeRegCode(
                MachineFingerprint.GetCpuSerial(),
                MachineFingerprint.GetMotherboardSerial(),
                MachineFingerprint.GetBiosSerial(),
                MachineFingerprint.GetDiskSerial(),
                MachineFingerprint.GetMachineUuid(),
                MachineFingerprint.GetSystemInstallTime());
        }

        /// <summary>根据本机注册码算出的正确认证码。</summary>
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

        /// <summary>注册：校验认证码并写入注册表。</summary>
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
        /// 20 位用户 ID = 注册表里 4 位随机字符 + 注册码后 16 位。
        /// 首次运行没有就自动生成 4 位随机字符存下来。
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
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 剔掉容易看混的 I/O/0/1
            var rnd = new Random();
            var sb = new StringBuilder(4);
            for (var i = 0; i < 4; i++)
                sb.Append(chars[rnd.Next(chars.Length)]);
            return sb.ToString();
        }
    }
}
