namespace ChatRoom.Core.Net
{
    /// <summary>WebSocket 消息类型，统一走这套协议。</summary>
    public enum WsMessageType
    {
        /// <summary>客户端→服务器：先握手，把 userId、userName 报上来。</summary>
        Hello,
        /// <summary>服务器→客户端：握手成功。</summary>
        HelloAck,
        /// <summary>服务器→客户端：出错了，带 reason。</summary>
        Error,

        /// <summary>客户端→服务器：加入房间，携带 roomId。</summary>
        JoinRoom,
        /// <summary>服务器→客户端：加房结果，带 ok/reason/roomId/name/users/history。</summary>
        JoinAck,
        /// <summary>客户端→服务器：退出房间，携带 roomId。</summary>
        LeaveRoom,
        /// <summary>服务器→房间成员：有人进来了，带 roomId/user。</summary>
        UserJoined,
        /// <summary>服务器→房间成员：用户离开，携带 roomId/userId。</summary>
        UserLeft,
        /// <summary>服务器广播：房间被关闭，携带 roomId。</summary>
        RoomClosed,
        /// <summary>服务器→客户端：被踢了，带 roomId/reason。</summary>
        Kick,
        /// <summary>服务器→客户端：非当前房间有新动静，只带 roomId（没内容）。</summary>
        RoomNotify,
        /// <summary>客户端→服务器：切换当前查看房间，携带 roomId。</summary>
        SetActiveRoom,

        /// <summary>文本消息。</summary>
        Chat,
        /// <summary>客户端→服务器：开始传文件/图片。</summary>
        UploadStart,
        /// <summary>客户端→服务器：文件数据块。</summary>
        FileChunk,
        /// <summary>服务器→房间：文件消息（可下载）。</summary>
        FileMeta,
        /// <summary>服务器→房间：图片消息（带缩略图）。</summary>
        ImageMeta,
        /// <summary>客户端→服务器：请求下载原文件/原图。</summary>
        FileRequest,
        /// <summary>服务器→客户端：文件数据块（下载）。</summary>
        FileData,
        /// <summary>客户端→服务器：要更早历史（往上翻页），带 roomId/beforeTime。</summary>
        HistoryRequest,
        /// <summary>服务器→客户端：更早历史消息，携带 roomId/messages。</summary>
        HistoryAck,
    }
}
