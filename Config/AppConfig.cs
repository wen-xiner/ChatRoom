using System;
using System.IO;

namespace ChatRoom.Config
{
    /// <summary>
    /// 全局配置：路径、注册表键名、默认值。
    /// 运行期数据统一存放在 <see cref="DataDirectory"/> 下，用 JSON 持久化。
    /// </summary>
    public static class AppConfig
    {
        /// <summary>应用名（用于注册表、目录名）。</summary>
        public const string AppName = "ChatRoom";

        /// <summary>当前程序所在目录。</summary>
        public static readonly string AppDirectory =
            AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

        /// <summary>运行期数据目录（JSON / 上传文件）。首次使用自动创建。</summary>
        public static readonly string DataDirectory =
            Path.Combine(AppDirectory, "Data");

        /// <summary>上传文件存储目录。</summary>
        public static readonly string FileStorageDirectory =
            Path.Combine(DataDirectory, "Files");

        /// <summary>历史消息存储目录（按房间分文件）。</summary>
        public static readonly string HistoryDirectory =
            Path.Combine(DataDirectory, "History");

        /// <summary>配置文件目录。</summary>
        public static readonly string ConfigDirectory =
            Path.Combine(DataDirectory, "Config");

        // ---- JSON 持久化文件路径 ----
        public static readonly string ServerConfigPath = Path.Combine(ConfigDirectory, "server-config.json");
        public static readonly string RoomsConfigPath = Path.Combine(ConfigDirectory, "rooms.json");
        public static readonly string UsersConfigPath = Path.Combine(ConfigDirectory, "users.json");
        public static readonly string RoomUsersConfigPath = Path.Combine(ConfigDirectory, "room-users.json");
        public static readonly string BlacklistConfigPath = Path.Combine(ConfigDirectory, "blacklist.json");
        public static readonly string ClientConfigPath = Path.Combine(ConfigDirectory, "client-config.json");
        public static readonly string UiConfigPath = Path.Combine(ConfigDirectory, "ui-config.json");

        // ---- 注册表 ----
        /// <summary>注册表根路径（HKCU）。</summary>
        public const string RegistryRoot = @"Software\ChatRoom";

        /// <summary>安装信息：安装时写入的 4 位随机字符。</summary>
        public const string RegKeyInstall = @"Software\ChatRoom\Install";
        public const string RegValueUserIdRandom = "UserIdRandom";

        /// <summary>认证信息：注册成功的认证码。</summary>
        public const string RegKeyAuth = @"Software\ChatRoom\Auth";
        public const string RegValueAuthCode = "AuthCode";

        // ---- 网络默认值 ----
        /// <summary>服务器默认监听端口。</summary>
        public const int DefaultPort = 8080;

        /// <summary>WebSocket 地址前缀。</summary>
        public const string WsPrefix = "ws://";

        // ---- 房间规则 ----
        /// <summary>保留的公共房间号。</summary>
        public const string PublicRoomId = "0000";

        /// <summary>公共房间名称。</summary>
        public const string PublicRoomName = "公共聊天区";

        /// <summary>用户 ID 总长度（4 随机字符 + 注册码后 16 位）。</summary>
        public const int UserIdLength = 20;

        /// <summary>0000 房间默认每小时发言上限（0 表示不限）。</summary>
        public const int DefaultPublicRoomHourlyLimit = 200;

        /// <summary>加入房间时拉取的历史消息条数。</summary>
        public const int HistoryCountOnJoin = 10;

        /// <summary>图片缩略图边长（px）。</summary>
        public const int ImageThumbnailSize = 120;

        /// <summary>确保目录存在，返回目录路径。</summary>
        public static string EnsureDirectory(string dir)
        {
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
