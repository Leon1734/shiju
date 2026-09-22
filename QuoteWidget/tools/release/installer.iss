; 拾句 · Inno Setup 安装包脚本
; 使用：安装 Inno Setup 6 (https://jrsoftware.org/isinfo.php) 后，
;       用 ISCC 编译本脚本，或在 Inno Setup Compiler 中打开并点 Compile。
; 产物：release\拾句_setup_v版本.exe（约 1-2MB）

#define MyAppName "拾句"
#define MyAppNameEn "QuoteWidget"
#define MyAppVersion "2.3.0"
#define MyAppPublisher "拾句"
#define MyAppExeName "QuoteWidget.exe"

[Setup]
AppId={{8E4B7C2A-5D31-4A9E-9B7F-QUOTEWIDGET01}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppNameEn}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=.
OutputBaseFilename=拾句_setup_v{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
; 便携数据（词库/词典）放在安装目录，重装不丢
UsePreviousAppDir=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "..\..\bin\Release\net10.0-windows\win-x64\publish\QuoteWidget.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\bin\Release\net10.0-windows\win-x64\词库\*"; DestDir: "{app}\词库"; Flags: recursesubdirs createallsubdirs uninsneveruninstall
Source: "..\..\bin\Release\net10.0-windows\win-x64\词典\*"; DestDir: "{app}\词典"; Flags: recursesubdirs createallsubdirs uninsneveruninstall
; 词库与词典随包分发（uninsneveruninstall：卸载时保留用户编辑的内容）

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："
Name: "autostart"; Description: "开机自动启动"; GroupDescription: "附加任务："; Flags: unchecked

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "QuoteWidget"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; \
    Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; 卸载时不清理 %APPDATA%\QuoteWidget（保留用户的收藏与设置），如需清理请手动删除该文件夹
