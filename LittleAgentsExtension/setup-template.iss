#define AppVersion "0.1.0.0"
#define ExtensionName "LittleAgentsExtension"
#define DisplayName "Little Agents"
#define PublisherName "Bennett Yang"

[Setup]
AppId={{57f71a86-6251-4177-b305-d214ed87fb10}
AppName={#DisplayName}
AppVersion={#AppVersion}
AppPublisher={#PublisherName}
AppPublisherURL=https://github.com/Bennett-Yang
AppSupportURL=https://github.com/Bennett-Yang/LittleAgentsExtensionForCmdPal/issues
AppUpdatesURL=https://github.com/Bennett-Yang/LittleAgentsExtensionForCmdPal/releases
DefaultDirName={autopf}\{#ExtensionName}
DefaultGroupName={#DisplayName}
OutputDir=bin\Release\installer
OutputBaseFilename={#ExtensionName}-Setup-{#AppVersion}
Compression=lzma
SolidCompression=yes
MinVersion=10.0.19041
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#DisplayName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "bin\Release\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#DisplayName}"; Filename: "{app}\{#ExtensionName}.exe"

[Registry]
Root: HKCU; Subkey: "Software\Classes\CLSID\{{36fde8e8-87f6-4677-a559-bc8ac65d97c4}"; ValueType: string; ValueData: "{#ExtensionName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CLSID\{{36fde8e8-87f6-4677-a559-bc8ac65d97c4}\LocalServer32"; ValueType: string; ValueData: """{app}\{#ExtensionName}.exe"" -RegisterProcessAsComServer"; Flags: uninsdeletekey
