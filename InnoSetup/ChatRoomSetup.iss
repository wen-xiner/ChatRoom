; ChatRoom 聊天室 - Inno Setup 安装脚本
; 单文件安装包：缺 .NET 自动检测安装、安装时可选桌面/开始菜单图标、卸载干净且保留用户数据
; 编译：ISCC.exe ChatRoomSetup.iss
; 打包源：..\bin\Release\ （见 [Files]）

#define MyAppName "ChatRoom 聊天室"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Xiner"
#define MyAppExeName "ChatRoom.exe"
#define MyAppId "{{4F9A3B2C-1D5E-4A6B-8C7D-9E0F1A2B3C4D}"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
; 每用户安装到 LocalAppData，不弹 UAC，程序能在自己目录里写 Data（聊天记录/配置/上传文件）
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=ChatRoomSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
DisableProgramGroupPage=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"

[Files]
; ---- 主程序 ----
Source: "..\bin\Release\ChatRoom.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release\ChatRoom.exe.config"; DestDir: "{app}"; Flags: ignoreversion
; ---- 第三方托管 DLL ----
Source: "..\bin\Release\Fleck.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release\Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; 安装时写入 4 位随机字符（用户 ID 前半部分），客户端启动时与注册码后 16 位拼成 20 位用户 ID
Root: HKCU; Subkey: "Software\ChatRoom\Install"; ValueType: string; ValueName: "UserIdRandom"; \
  ValueData: "{code:GetRandom4}"; Flags: uninsdeletekeyifempty

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  DotNetReleaseRequired = 394802;  { .NET Framework 4.6.2 的 Release 注册表值 }
  DotNetUrl = 'https://download.microsoft.com/download/E/4/1/E4173890-A24A-4936-9FC9-6F9A85785680/NDP462-KB3151800-x86-x64-AllOS-ENU.exe';  { 官方 4.6.2 离线包 }
  DotNetFileName = 'NDP462-KB3151800-x86-x64-AllOS-ENU.exe';

{ 检查是否已安装 .NET Framework 4.6.2 或更高（Release 值见 https://learn.microsoft.com/dotnet/framework/migration-guide/how-to-determine-which-versions-are-installed）}
function IsDotNetSatisfied: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
    and (Release >= DotNetReleaseRequired);
end;

// 用 PowerShell WebClient 下载 .NET 离线包到临时目录（Win7 自带 PS2.0 + WebClient，不依赖 curl）
function DownloadDotNet: Boolean;
var
  TmpPath, PS: string;
  ResultCode: Integer;
begin
  Result := False;
  TmpPath := ExpandConstant('{tmp}\' + DotNetFileName);
  PS := '-NoProfile -ExecutionPolicy Bypass -Command ' +
    '"' + '[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; ' +
    '$c=New-Object System.Net.WebClient; ' +
    '$c.DownloadFile(''' + DotNetUrl + ''',''' + TmpPath + ''')' + '"';
  if Exec('powershell.exe', PS, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
    Result := FileExists(TmpPath);
end;

function InitializeSetup: Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if IsDotNetSatisfied then
    exit;

  if MsgBox('检测到缺少 .NET Framework 4.6.2 或更高版本（本程序运行必需）。' #13#10 +
    '是否现在从微软官网下载并安装？需联网，约 60MB。',
    mbConfirmation, MB_YESNO) = IDNO then
  begin
    Result := False;
    exit;
  end;

  if not DownloadDotNet then
  begin
    MsgBox('下载 .NET Framework 失败，请检查网络后手动安装：' #13#10 + DotNetUrl,
      mbError, MB_OK);
    Result := False;
    exit;
  end;

  { .NET 安装需要管理员权限，用 ShellExec 触发 UAC 提权 }
  if not ShellExec('open', ExpandConstant('{tmp}\' + DotNetFileName),
    '/quiet /norestart', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('启动 .NET 安装程序失败，请手动运行：' #13#10 + DotNetUrl, mbError, MB_OK);
    Result := False;
    exit;
  end;

  if not IsDotNetSatisfied then
  begin
    MsgBox('.NET Framework 安装未生效，请手动安装后重试：' #13#10 + DotNetUrl, mbError, MB_OK);
    Result := False;
  end;
end;

{ 生成 4 位随机字符（用户 ID 前半部分，安装时写入注册表）}
function GetRandom4(Param: string): string;
var
  i: Integer;
  Chars: string;
begin
  Chars := 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';  { 去掉易混淆的 I/O/0/1 }
  Result := '';
  for i := 1 to 4 do
    Result := Result + Chars[Random(Length(Chars)) + 1];
end;
