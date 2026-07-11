; TEMPLATE: Inno Setup Script for Command Palette Extensions
;
; To use this template for a new extension:
; 1. Copy this file to your extension's project folder as "setup-template.iss"
; 2. Replace EXTENSION_NAME with your extension name (e.g., CmdPalMyExtension)
; 3. Replace DISPLAY_NAME with your extension's display name (e.g., My Extension)
; 4. Replace DEVELOPER_NAME with your name (e.g., Your Name Here)
; 5. Replace CLSID-HERE with extensions CLSID
; 6. Update the default version to match your project file

#define AppVersion "0.1.0"

[Setup]
AppId={{57f71a86-6251-4177-b305-d214ed87fb10}}
AppName=Little Agents
AppVersion={#AppVersion}
AppPublisher=Bennett Yang
DefaultDirName={autopf}\LittleAgentsExtension
OutputDir=bin\Release\installer
OutputBaseFilename=LittleAgentsExtension-Setup-{#AppVersion}
Compression=lzma
SolidCompression=yes
MinVersion=10.0.19041

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "bin\Release\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\Little Agents"; Filename: "{app}\LittleAgentsExtension.exe"

[Registry]
Root: HKCU; Subkey: "SOFTWARE\Classes\CLSID\{{36fde8e8-87f6-4677-a559-bc8ac65d97c4}}"; ValueData: "LittleAgentsExtension"
Root: HKCU; Subkey: "SOFTWARE\Classes\CLSID\{{36fde8e8-87f6-4677-a559-bc8ac65d97c4}}\LocalServer32"; ValueData: "{app}\LittleAgentsExtension.exe -RegisterProcessAsComServer"