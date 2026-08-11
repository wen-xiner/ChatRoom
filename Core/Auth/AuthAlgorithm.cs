using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ChatRoom.Core.Auth
{
    /// <summary>
    /// 纯算法层（无注册表/WMI 依赖），与注册机 KeyGen 共用同一文件。
    /// </summary>
    public static class AuthAlgorithm
    {
        /// <summary>盐值：认证码加盐用。改它记得连 KeyGen 一起改（同一个文件）。</summary>
        public const string Salt = "ChatRoom-Salt@2026-Xiner#K7";

        /// <summary>
        /// 拿机器指纹算注册码：SHA256 后转成 64 位小写十六进制（32 字节）。
        /// 把 CPU/主板/BIOS/磁盘/UUID/安装时间拼一起，空值自动跳过；少一两个不影响认机器。
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

        /// <summary>由注册码生成认证码：MD5(注册码 + 盐) → 32 位小写十六进制。</summary>
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
