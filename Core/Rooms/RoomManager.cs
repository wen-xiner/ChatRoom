using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ChatRoom.Config;

namespace ChatRoom.Core.Rooms
{
    /// <summary>房间管理器：创建/关闭/查询/随机房间号；0000 公共房间启动即存在且不可删。</summary>
    public class RoomManager
    {
        private readonly ConcurrentDictionary<string, Room> _rooms =
            new ConcurrentDictionary<string, Room>(StringComparer.OrdinalIgnoreCase);

        public RoomManager()
        {
            // 0000 公共房间：服务器启动后自动存在
            _rooms[AppConfig.PublicRoomId] = new Room(AppConfig.PublicRoomId, AppConfig.PublicRoomName, isPublic: true);
        }

        public IEnumerable<Room> AllRooms => _rooms.Values;
        public Room PublicRoom => _rooms[AppConfig.PublicRoomId];
        public int Count => _rooms.Count;

        public Room Get(string roomId)
        {
            if (string.IsNullOrWhiteSpace(roomId)) return null;
            return _rooms.TryGetValue(roomId, out var r) ? r : null;
        }

        /// <summary>
        /// 创建房间：房间号 4~6 位数字，不能是 0000，不能与已存在房间重号。
        /// </summary>
        public bool TryCreate(string roomId, string name, out Room room, out string error)
        {
            room = null;
            error = null;

            if (string.IsNullOrWhiteSpace(roomId) || roomId.Length < 4 || roomId.Length > 6 || !roomId.All(char.IsDigit))
            {
                error = "房间号必须为 4~6 位数字。";
                return false;
            }
            if (roomId == AppConfig.PublicRoomId)
            {
                error = "0000 是保留的公共房间号，不可创建。";
                return false;
            }
            if (_rooms.ContainsKey(roomId))
            {
                error = $"房间 {roomId} 已存在。";
                return false;
            }

            room = new Room(roomId, string.IsNullOrWhiteSpace(name) ? roomId : name.Trim(), isPublic: false);
            if (!_rooms.TryAdd(roomId, room))
            {
                error = "创建失败，请重试。";
                room = null;
                return false;
            }
            return true;
        }

        /// <summary>随机生成 4~6 位数字房间号，避开已存在房间与 0000。</summary>
        public string GenerateRandomId()
        {
            var rnd = new Random();
            for (var i = 0; i < 100; i++)
            {
                var len = rnd.Next(4, 7);
                var sb = new StringBuilder(len);
                for (var j = 0; j < len; j++)
                    sb.Append(rnd.Next(0, 10));
                var id = sb.ToString();
                if (id != AppConfig.PublicRoomId && !_rooms.ContainsKey(id))
                    return id;
            }
            return rnd.Next(100000, 1000000).ToString(); // 兜底
        }

        /// <summary>关闭房间（0000 不允许）。关闭后客户端不能加入，成员收到 RoomClosed。</summary>
        public bool TryClose(string roomId, out Room room, out string error)
        {
            room = null;
            error = null;
            if (roomId == AppConfig.PublicRoomId)
            {
                error = "0000 公共房间不可关闭。";
                return false;
            }
            if (!_rooms.TryRemove(roomId, out room))
            {
                error = $"房间 {roomId} 不存在。";
                return false;
            }
            room.Close();
            return true;
        }
    }
}
