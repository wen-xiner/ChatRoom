using System;
using System.IO;
using Newtonsoft.Json;

namespace ChatRoom.Core.Storage
{
    /// <summary>JSON 文件读写（原子写：临时文件 + 替换，防写一半损坏）。</summary>
    public static class JsonStore
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        /// <summary>读 JSON 文件，没了或坏了就返回个默认的。</summary>
        public static T Load<T>(string filePath) where T : new()
        {
            try
            {
                if (!File.Exists(filePath)) return new T();
                var json = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(json)) return new T();
                var result = JsonConvert.DeserializeObject<T>(json);
                return result == null ? new T() : result;
            }
            catch { return new T(); }
        }

        /// <summary>写 JSON 文件，目录没有会自动建。</summary>
        public static void Save(string filePath, object data)
        {
            try
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var tmp = filePath + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(data, Settings));
                File.Copy(tmp, filePath, true);
                try { File.Delete(tmp); } catch { }
            }
            catch { /* 写盘失败忽略，不影响运行 */ }
        }
    }
}
