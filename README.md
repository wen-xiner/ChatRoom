# ChatRoom 聊天室

一个基于 **.NET Framework 4.6.2 + WPF + WebSocket** 的局域网聊天室程序。单进程内可切换「客户端 / 服务器 / 客户端+服务器」三种运行模式，支持多房间、消息隔离、文件与图片传输、认证门禁、踢人/黑名单、历史消息与 JSON 持久化。

---

## 一、功能总览

| 模块 | 功能 |
|---|---|
| 运行模式 | 客户端 / 服务器 / 客户端+服务器（同机互连） |
| 服务器管理 | 创建/关闭服务器、创建/关闭房间、房间列表、**0000 公共房间**（自动存在、不可删）、0000 发言上限 |
| 客户端房间 | 加入/退出/切换房间、已加入房间列表（新消息红点、可用性） |
| 聊天室 | 文本消息、**文件上传/下载**、**图片预览/查看原图/复制/下载** |
| 消息隔离 | 房间间消息完全隔离；非当前房间只收"新消息"通知，不接收内容 |
| 房间用户 | 用户列表（在线状态点、本人标记）、进出房间通知 |
| 服务器管理 | **踢出用户、房间黑名单**（按用户 ID / IP） |
| 历史消息 | 每个房间独立保存，加入时拉取最近 10 条（重启恢复） |
| 服务器状态 | 连接数、在线用户（按 ID 去重）、房间数、各房间在线人数 |

## 二、技术栈

- **框架**：.NET Framework 4.6.2（WPF）
- **网络**：WebSocket — 服务器 `Fleck`，客户端 `System.Net.WebSockets.ClientWebSocket`
- **序列化**：Newtonsoft.Json（统一 camelCase 协议）
- **持久化**：JSON 文件（`Data/` 目录）
- **打包**：Inno Setup 6
- **架构**：MVVM + Code-behind 混合（数据绑定走 ViewModel，按钮动作用 Click 事件调用 VM 方法）

## 三、快速开始

### 方式一：直接运行（开发）
1. 用 VS2022 打开 `ChatRoom.sln` 编译运行；或运行 `bin\Debug\ChatRoom.exe`。
2. 顶部选择运行模式：
   - **客户端 + 服务器**（最常用）：左侧启动服务器，右侧以 `127.0.0.1:8080` 连接本机。
   - **服务器**：仅启动服务器，供局域网其他电脑连接。
   - **客户端**：连接指定 IP/端口（本机或局域网其他服务器）。
3. 服务器面板点「创建服务器」→ 客户端面板填用户名 →「连接」→ 输入房间号（如 `0000`）→「加入房间」。

### 方式二：安装包
1. 运行 `InnoSetup\Output\ChatRoomSetup.exe`（可选安装路径、桌面/开始菜单图标）。
2. 安装时自动写入注册表用户身份；首次运行自动建 `Data` 目录。

## 四、端口约定

- **默认端口 8080**（服务器面板可改，配置文件会记住）。
- 局域网使用：查本机 IP（`ipconfig`），放行防火墙端口后让其他人以「客户端」模式连接。
  ```
  netsh advfirewall firewall add rule name="ChatRoom8080" dir=in action=allow protocol=TCP localport=8080
  ```

## 五、注册流程（解锁 文件/图片 收发）

认证基于**机器指纹**，未认证用户只能发文本、看图/文件消息占位提示，无法下载/查看原图；认证后解锁。

| 步骤 | 操作 |
|---|---|
| 1 | 主程序顶部点「注 册」→ 弹窗显示**本机注册码**（64 位，由 CPU 序列号+主板序列号+系统安装时间经 SHA256 生成） |
| 2 | 运行注册机 `KeyGen\bin\Debug\KeyGen.exe`（**不随软件安装**）→ 粘贴注册码 → 回车 → 得到**认证码**（32 位，MD5(注册码+盐)） |
| 3 | 把认证码粘回注册框 →「注册」→ 认证状态变「已认证」 |

> 注册码按**每台机器**计算，换电脑需重新生成；认证码写入注册表 `HKCU\Software\ChatRoom\Auth`。

## 六、用户身份

- **用户 ID**（20 位）= 安装时写入注册表的 **4 位随机字符** + **注册码后 16 位**。
- 客户端连接时携带用户 ID，服务器校验长度（=20），不合法拒绝连接。
- 服务器在线用户数按用户 ID 去重统计。

## 七、数据与配置文件（`Data/` 目录，均 JSON）

| 文件 | 内容 |
|---|---|
| `Config/server-config.json` | 端口、0000 发言上限 |
| `Config/rooms.json` | 手动创建的房间（重启恢复） |
| `Config/blacklist.json` | 房间黑名单 |
| `Config/client-config.json` | 客户端 IP/端口/用户名/已加入房间（重连自动再加入） |
| `Config/ui-config.json` | 窗口大小、表格列宽 |
| `History/{roomId}.json` | 各房间历史消息（按房间隔离） |
| `Files/{roomId}/{fileId}/` | 上传的文件/图片（含缩略图） |

## 八、目录结构

```
ChatRoom/
├── App.xaml / MainWindow.xaml / Styles.xaml   # 界面与全局样式
├── Config/        AppConfig、配置模型
├── Core/
│   ├── Net/       ChatServer(Fleck)、ChatClient、协议
│   ├── Rooms/     Room、RoomManager、BlacklistStore
│   ├── Users/     Session 与会话管理
│   ├── Auth/      注册码/认证码/机器指纹/注册表
│   ├── Storage/   JsonStore 泛型读写
│   └── Messages/  消息路由（隔离核心）
├── Models/        消息/房间/用户/黑名单实体
├── ViewModels/    MVVM 视图模型
├── Views/         注册/图片查看对话框
├── Data/          运行期 JSON 与上传文件（git 隔离）
├── InnoSetup/     ChatRoomSetup.iss + 中文语言包（安装脚本随 git 上传）
└── KeyGen/        注册机（独立控制台项目，不随软件安装）
```

## 九、构建与打包

```bash
# 编译（Debug / Release）
MSBuild ChatRoom.sln -p:Configuration=Release

# 打安装包（需本机装有 Inno Setup 6）
ISCC.exe InnoSetup\ChatRoomSetup.iss
# 产物：InnoSetup\Output\ChatRoomSetup.exe
```

- 安装包为**每用户安装**（不弹 UAC），默认安装到 `%LocalAppData%\Programs\ChatRoom 聊天室`。
- 缺 .NET Framework 4.6.2 时安装向导会提示下载安装。

## 十、注意事项

- 同一台电脑多开实例共享用户 ID（规格如此），"本人/他人"通过会话 ID 区分显示。
- 开发阶段文档（`plan.md` / `day1.md` / `day2.md`）为本地记录，**不随本仓库上传**。
