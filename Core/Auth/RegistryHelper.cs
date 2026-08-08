using Microsoft.Win32;

namespace ChatRoom.Core.Auth
{
    /// <summary>注册表读写工具（HKCU 下）。所有读取异常静默返回默认值。</summary>
    public static class RegistryHelper
    {
        /// <summary>读 HKCU 子键字符串值。</summary>
        public static string GetValue(string subKey, string name, string fallback = "")
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(subKey))
                {
                    var v = key?.GetValue(name);
                    return v?.ToString() ?? fallback;
                }
            }
            catch { return fallback; }
        }

        /// <summary>写 HKCU 子键字符串值（不存在则创建子键）。</summary>
        public static void SetValue(string subKey, string name, string value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(subKey))
                {
                    key?.SetValue(name, value ?? "");
                }
            }
            catch { }
        }
    }
}
