#ifndef SourceDir
  #error SourceDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#define AppVersion GetVersionNumbersString(SourceDir + "\LouisaWiFi.exe")
[Setup]
AppId={{42F3CB95-DA46-4755-89AA-CE1B82673662}
AppName=Louisa Wi-Fi for Windows
AppVersion={#AppVersion}
AppPublisher=05zhi
AppPublisherURL=https://github.com/05zhi/louisa-wifi-windows
DefaultDirName={localappdata}\Programs\LouisaWiFi
DefaultGroupName=Louisa Wi-Fi
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename=LouisaWiFi-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\LouisaWiFi.exe
CloseApplications=yes
RestartApplications=no
SetupMutex=Local\LouisaWiFi.Setup
LicenseFile=..\LICENSE
[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Louisa Wi-Fi"; Filename: "{app}\LouisaWiFi.exe"
Name: "{group}\解除安裝 Louisa Wi-Fi"; Filename: "{uninstallexe}"
[Run]
Filename: "{app}\LouisaWiFi.exe"; Description: "開啟 Louisa Wi-Fi"; Flags: nowait postinstall skipifsilent
[UninstallDelete]
Type: files; Name: "{userstartup}\Louisa Wi-Fi.lnk"
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "LouisaWiFi"; Flags: uninsdeletevalue
