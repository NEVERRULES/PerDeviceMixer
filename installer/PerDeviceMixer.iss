#ifndef AppVersion
  #define AppVersion "0.3.2-preview.1"
#endif
#ifndef VersionInfoVersion
  #define VersionInfoVersion "0.3.2.1"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\release\0.3.2-preview.1\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\release\0.3.2-preview.1\assets"
#endif
#ifndef OutputBaseFilename
  #define OutputBaseFilename "PerDeviceMixer-0.3.2-preview.1-win-x64-Setup"
#endif
#ifndef ChineseMessagesFile
  #define ChineseMessagesFile "compiler:Languages\ChineseSimplified.isl"
#endif

[Setup]
AppId={{10F8FE46-850C-4F65-8951-87439A4ED6AB}
AppName=PerDeviceMixer
AppVersion={#AppVersion}
AppVerName=PerDeviceMixer {#AppVersion}
AppPublisher=NEVERRULES
AppPublisherURL=https://github.com/NEVERRULES/PerDeviceMixer
AppSupportURL=https://github.com/NEVERRULES/PerDeviceMixer/issues
AppUpdatesURL=https://github.com/NEVERRULES/PerDeviceMixer/releases
VersionInfoVersion={#VersionInfoVersion}
VersionInfoCompany=NEVERRULES
VersionInfoDescription=PerDeviceMixer installer
VersionInfoProductName=PerDeviceMixer
DefaultDirName={localappdata}\Programs\PerDeviceMixer
DefaultGroupName=PerDeviceMixer
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile=..\src\PerDeviceMixer.App\Assets\PerDeviceMixer.ico
UninstallDisplayIcon={app}\PerDeviceMixer.App.exe
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
AppMutex=Local\PerDeviceMixer.Singleton
CloseApplications=yes
CloseApplicationsFilter=PerDeviceMixer.App.exe
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "{#ChineseMessagesFile}"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "其他任务："; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\PerDeviceMixer"; Filename: "{app}\PerDeviceMixer.App.exe"
Name: "{autodesktop}\PerDeviceMixer"; Filename: "{app}\PerDeviceMixer.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PerDeviceMixer.App.exe"; Description: "启动 PerDeviceMixer"; Flags: nowait postinstall skipifsilent
