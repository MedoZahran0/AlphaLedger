#define AppName "AlphaLedger"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppPublisher "AlphaLedger"
#define AppExeName "AlphaLedger.App.exe"

#ifndef AppSource
  #error AppSource must point to the self-contained application executable.
#endif

[Setup]
AppId={{8A02A4E8-B2E6-44DC-BD7A-2EAC4D0A7F87}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\Release
OutputBaseFilename=AlphaLedger-Setup
SetupIconFile=..\src\AlphaLedger.App\Resources\AlphaLedger.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[CustomMessages]
english.DesktopIconTask=Create a desktop shortcut
arabic.DesktopIconTask=إنشاء اختصار على سطح المكتب
english.DesktopIconGroup=Additional shortcuts:
arabic.DesktopIconGroup=اختصارات إضافية:
english.LaunchApp=Launch AlphaLedger
arabic.LaunchApp=تشغيل AlphaLedger

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIconTask}"; GroupDescription: "{cm:DesktopIconGroup}"; Flags: checkedonce

[Files]
Source: "{#AppSource}"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{app}\Uninstall-AlphaLedger.ps1"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\AlphaLedger');
end;
