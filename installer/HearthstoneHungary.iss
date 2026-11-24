; Inno Setup 6 telepítő szkript a Hearthstone Hungary alkalmazáshoz.
;
; Használat (helyben vagy GitHub Actions-ben):
;   1. dotnet build src\HearthstoneHungary\HearthstoneHungary.csproj -c Release
;   2. ISCC.exe /DAppVersion=3.0.2 installer\HearthstoneHungary.iss
;
; Kimenet: dist\HearthstoneHungary-Setup.exe
;
; Fontos: a telepítő a felhasználó saját mappájába (%LocalAppData%\HearthstoneHungary)
; telepít rendszergazdai jog nélkül, így az alkalmazás beépített automatikus frissítése
; (AppUpdater) továbbra is gond nélkül működik.

#ifndef AppVersion
  #define AppVersion "3.0.2"
#endif

[Setup]
AppId={{9F6C4B2A-7E1D-4C8A-B3F5-2A8D6E1C4B90}
AppName=Hearthstone Hungary
AppVersion={#AppVersion}
AppVerName=Hearthstone Hungary {#AppVersion}
AppPublisher=Hearthstone Hungary
AppPublisherURL=https://hearthstonehungary.hu
AppSupportURL=https://github.com/Phoenixy/Hearthstone_Card_Export
AppUpdatesURL=https://github.com/Phoenixy/Hearthstone_Card_Export/releases
DefaultDirName={localappdata}\HearthstoneHungary
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=HearthstoneHungary-Setup
SetupIconFile=..\src\HearthstoneHungary\Resources\hshu-icon.ico
UninstallDisplayIcon={app}\HearthstoneHungary.exe
UninstallDisplayName=Hearthstone Hungary
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "hungarian"; MessagesFile: "compiler:Languages\Hungarian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; A dotnet build -c Release által összerakott, tiszta kiadási fájlok (ugyanaz, mint a ZIP-ben).
Source: "..\src\HearthstoneHungary\obj\zip\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Hearthstone Hungary"; Filename: "{app}\HearthstoneHungary.exe"
Name: "{autodesktop}\Hearthstone Hungary"; Filename: "{app}\HearthstoneHungary.exe"; Tasks: desktopicon

[Registry]
; Eltávolításkor törli az alkalmazás által létrehozott Windows-indítási bejegyzést, telepítéskor nem nyúl hozzá.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "HearthstoneHungary"; Flags: uninsdeletevalue

[UninstallDelete]
; Az automatikus frissítőből esetleg megmaradt .old fájlok takarítása eltávolításkor.
Type: files; Name: "{app}\*.old"

[Run]
Filename: "{app}\HearthstoneHungary.exe"; Description: "{cm:LaunchProgram,Hearthstone Hungary}"; Flags: nowait postinstall skipifsilent

[Code]
procedure StopRunningApp();
var
  ResultCode: Integer;
begin
  // A program bezáráskor a tálcára kerül, így a fájlok cseréje/törlése előtt leállítjuk a futó példányt.
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM HearthstoneHungary.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningApp();
  Result := True;
end;
