namespace ChatRoom.Core.Net
{
    /// <summary>WebSocket 消息类型（统一协议）。</summary>
    public enum WsMessageType
    {
        /// <summary>客户端→服务器：握手，携带 userId、userName。</summary>
        Hello,
        /// <summary>服务器→客户端：握手成功。</summary>
        HelloAck,
        /// <summary>服务器→客户端：错误，携带 reason。</summary>
        Error,

        /// <summary>客户端→服务器：加入房间，携带 roomId。</summary>
        JoinRoom,
        /// <summary>服务器→客户端：加入结果，携带 ok/reason/roomId/name/users/history。</summary>
        JoinAck,
        /// <summary>客户端→服务器：退出房间，携带 roomId。</summary>
        LeaveRoom,
        /// <summary>服务器→房间成员：新用户加入，携带 roomId/user。</summary>
        UserJoined,
        /// <summary>服务器→房间成员：用户离开，携带 roomId/userId。</summary>
        UserLeft,
        /// <summary>服务器广播：房间被关闭，携带 roomId。</summary>
        RoomClosed,
        /// <summary>服务器→客户端：被踢出房间，携带 roomId/reason。</summary>
        Kick,
        /// <summary>服务器→客户端：已加入但非当前房间有新动态，仅携带 roomId（不含内容）。</summary>
        RoomNotify,
        /// <summary>客户端→服务器：切换当前查看房间，携带 roomId。</summary>
        SetActiveRoom,

        /// <summary>文本消息。</summary>
        Chat,
        /// <summary>客户端→服务器：开始上传文件/图片。</summary>
        UploadStart,
        /// <summary>客户端→服务器：文件数据块。</summary>
        FileChunk,
        /// <summary>服务器→房间：文件消息（可下载）。</summary>
        FileMeta,
        /// <summary>服务器→房间：图片消息（含缩略图）。</summary>
        ImageMeta,
        /// <summary>客户端→服务器：请求下载原文件/原图。</summary>
        FileRequest,
        /// <summary>服务器→客户端：文件数据块（下载）。</summary>
        FileData,
    }
}
