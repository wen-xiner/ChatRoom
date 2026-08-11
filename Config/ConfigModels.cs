using System;
using System.Collections.Generic;
using ChatRoom.Config;

namespace ChatRoom.Core.Storage
{
    /// <summary>服务器配置（JSON 持久化）。</summary>
    public class ServerConfig
    {
        public int Port { get; set; } = AppConfig.DefaultPort;
        public int PublicRoomHourlyLimit { get; set; } = AppConfig.DefaultPublicRoomHourlyLimit;
        /// <summary>文件/图片存储目录；空 = 默认 Data\Files。</summary>
        public string FileStoragePath { get; set; } = "";
    }

    /// <summary>用户记录（users.json）：见过面的用户。</summary>
    public class UserRecord
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string LastIp { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
    }

    /// <summary>房间+用户列表（room-users.json）：各房间当前成员快照。</summary>
    public class RoomUsersRecord
    {
        public string RoomId { get; set; }
        public string RoomName { get; set; }
        public List<UserRecord> Users { get; set; } = new List<UserRecord>();
    }

    /// <summary>服务器持久化的房间记录（非公共房，重启后重建）。</summary>
    public class RoomRecord
    {
        public string RoomId { get; set; }
        public string Name { get; set; }
    }

    /// <summary>客户端连服务器的配置。</summary>
    public class ClientConfig
    {
        public string ServerIp { get; set; } = "127.0.0.1";
        public int Port { get; set; } = AppConfig.DefaultPort;
        public string UserName { get; set; } = "";
        /// <summary>
        /// 按用户 ID 分开存已加房间：userId -> 房间列表。
        /// 存到本机 client-config.json；连接后只显示列表、不自动进，点了才重新进。
        /// 按账号隔开，免得一台电脑多个账号互相串房。
        /// </summary>
        public Dictionary<string, List<string>> JoinedRoomsByUser { get; set; } = new Dictionary<string, List<string>>();
    }

    /// <summary>界面配置：窗口大小、表格列宽。</summary>
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
