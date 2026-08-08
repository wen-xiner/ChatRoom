using System.Security.Cryptography;
using System.Text;

namespace ChatRoom.Core.Auth
{
    /// <summary>
    /// 纯算法层（无注册表/WMI 依赖），注册机 KeyGen 链接同一文件，保证算法与盐值完全一致。
    /// </summary>
    public static class AuthAlgorithm
    {
        /// <summary>盐值：注册码 → 认证码时加盐。修改必须同步 KeyGen（共享同一文件）。</summary>
        public const string Salt = "ChatRoom-Salt@2026-Xiner#K7";

        /// <summary>
        /// 由机器指纹生成注册码：SHA256 → 64 位小写十六进制（32 字节）。
        /// </summary>
        public static string ComputeRegCode(string cpuSerial, string motherboardSerial, string installTime)
        {
            var raw = string.Concat(cpuSerial ?? "", "|", motherboardSerial ?? "", "|", installTime ?? "");
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(raw)));
            }
        }

        /// <summary>
        /// 由注册码生成认证码：MD5(注册码 + 盐) → 32 位小写十六进制。
        /// </summary>
        public static string ComputeAuthCode(string regCode)
        {
            var input = (regCode ?? "") + Salt;
            using (var md5 = MD5.Create())
            {
                return ToHex(md5.ComputeHash(Encoding.UTF8.GetBytes(input)));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
