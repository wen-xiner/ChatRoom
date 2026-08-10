using System.Linq;
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
        /// 输入为若干指纹片段（CPU/主板/BIOS/磁盘/MAC/安装时间等），自动跳过空值后拼接。
        /// 单个指纹缺失不影响整体唯一性——只要任一硬件序列号可读，不同机器即可区分。
        /// </summary>
        public static string ComputeRegCode(params string[] fingerprints)
        {
            var raw = string.Join("|", (fingerprints ?? new string[0])
                .Where(f => !string.IsNullOrWhiteSpace(f)));
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
