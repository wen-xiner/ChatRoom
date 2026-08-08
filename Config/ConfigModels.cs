using System.Collections.Generic;
using ChatRoom.Config;

namespace ChatRoom.Core.Storage
{
    /// <summary>服务器配置（JSON 持久化）。</summary>
    public class ServerConfig
    {
        public int Port { get; set; } = AppConfig.DefaultPort;
        public int PublicRoomHourlyLimit { get; set; } = AppConfig.DefaultPublicRoomHourlyLimit;
    }

    /// <summary>服务器持久化的房间记录（非公共房，重启后重建）。</summary>
    public class RoomRecord
    {
        public string RoomId { get; set; }
        public string Name { get; set; }
    }

    /// <summary>客户端连接配置。</summary>
    public class ClientConfig
    {
        public string ServerIp { get; set; } = "127.0.0.1";
        public int Port { get; set; } = AppConfig.DefaultPort;
        public string UserName { get; set; } = "";
        public List<string> JoinedRooms { get; set; } = new List<string>();
    }

    /// <summary>UI 配置：窗口大小、表格列宽。</summary>
    public class UiConfig
    {
        public double WindowWidth { get; set; } = 1280;
        public double WindowHeight { get; set; } = 800;
        public double ColRoomId { get; set; } = 70;
        public double ColName { get; set; } = 200;
        public double ColOnline { get; set; } = 55;
        public double ColTotal { get; set; } = 70;
        public double ColStatus { get; set; } = 70;
    }
}
