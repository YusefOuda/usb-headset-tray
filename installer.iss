#define AppName "usb-headset-tray"
#define AppPublisher "Yusef Ouda"
#define AppURL "https://github.com/YusefOuda/usb-headset-tray"
#define AppExeName "UsbHeadsetTray.exe"

[Setup]
AppId={{B3E2C3D4-5F6A-7B8C-9D0E-1F2A3B4C5D6E}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={localappdata}\{#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=publish
OutputBaseFilename=usb-headset-tray-setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "publish\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "publish\headsetcontrol.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userstartmenu}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill"; Parameters: "/f /im {#AppExeName}"; Flags: runhidden waituntilterminated
