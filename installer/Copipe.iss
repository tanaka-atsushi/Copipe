; Copipe のインストーラー (Inno Setup 6)
;
; build.ps1 -Installer から呼ぶ。バージョンは bin\Copipe.exe から読んで /DAppVersion=1.0.9 で渡す。
; 管理者権限なしで、ユーザーごとに %LOCALAPPDATA%\Programs\Copipe へ入れる。
; 設定・履歴・定型文 (%LOCALAPPDATA%\Copipe) は、アンインストールのときに消すかどうかを聞く (既定は消さない)。
;
; このファイルは BOM 付き UTF-8 (BOM が無いと ISCC は ANSI として読む)。

#ifndef AppVersion
  #error AppVersion が渡されていません (build.ps1 -Installer から呼ぶ)
#endif

[Setup]
; AppId は上書き更新・アンインストールで同じアプリと見分けるためのもの。変えない
AppId={{E4D2C626-6C3A-49E5-80BB-E99DE048CA11}
AppName=Copipe
AppVersion={#AppVersion}
AppVerName=Copipe {#AppVersion}
AppPublisher=Lonesome BBQ
VersionInfoVersion={#AppVersion}
PrivilegesRequired=lowest
DefaultDirName={autopf}\Copipe
DisableProgramGroupPage=yes
MinVersion=10.0
OutputDir=..\bin
OutputBaseFilename=Copipe-Setup-{#AppVersion}
SetupIconFile=..\assets\icon\copipe.ico
UninstallDisplayIcon={app}\Copipe.exe
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
; Windows の表示言語が日本語・英語ならそれを使い、どちらでもなければ選ぶ画面を出す
ShowLanguageDialog=auto

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"

[Files]
Source: "..\bin\Copipe.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Copipe"; Filename: "{app}\Copipe.exe"

[CustomMessages]
en.DeleteDataPrompt=Also delete the settings, clipboard history and snippets?%n%n%1%n%nChoose No to keep them for a future reinstall.
ja.DeleteDataPrompt=設定・クリップボードの履歴・定型文も削除しますか?%n%n%1%n%nもう一度インストールするときのために残すなら「いいえ」を選んでください。

[Run]
Filename: "{app}\Copipe.exe"; Description: "{cm:LaunchProgram,Copipe}"; Flags: nowait postinstall skipifsilent

[Code]
// 動いている Copipe を、上書き・アンインストールの前に終わらせる (exe がロックされているため)。
// tools\Stop-Copipe.ps1 と同じく、小窓 (タイトル "Copipe") に WM_CLOSE を送って正常に終わらせる。
// 強制終了するとトレイにアイコンの抜け殻が残るので、終わらなかったときだけ強制終了する。
const
  WM_CLOSE = $0010;
  PROCESS_TERMINATE = $0001;
  SYNCHRONIZE = $00100000;
  WAIT_TIMEOUT = $00000102;

// ClassName は NULL を渡すため Longint にしている
function FindWindowEx(Parent, ChildAfter: HWND; ClassName: Longint; WindowName: String): HWND;
  external 'FindWindowExW@user32.dll stdcall';
function GetClassName(Wnd: HWND; ClassName: String; MaxCount: Integer): Integer;
  external 'GetClassNameW@user32.dll stdcall';
function GetWindowThreadProcessId(Wnd: HWND; var ProcessId: DWORD): DWORD;
  external 'GetWindowThreadProcessId@user32.dll stdcall';
function OpenProcess(Access: DWORD; InheritHandle: LongBool; ProcessId: DWORD): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: DWORD): DWORD;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function TerminateProcess(Handle: THandle; ExitCode: UINT): LongBool;
  external 'TerminateProcess@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): LongBool;
  external 'CloseHandle@kernel32.dll stdcall';

procedure StopCopipe;
var
  Wnd, Prev: HWND;
  Pid: DWORD;
  Proc: THandle;
  Cls: String;
begin
  Prev := 0;
  repeat
    Wnd := FindWindowEx(0, Prev, 0, 'Copipe');
    if Wnd <> 0 then
    begin
      // 同じタイトルのエクスプローラー (Copipe フォルダーを開いた窓) などは除く。小窓は WinForms の窓
      SetLength(Cls, 256);
      SetLength(Cls, GetClassName(Wnd, Cls, 256));
      if Pos('WindowsForms10.', Cls) = 1 then
      begin
        Pid := 0;
        GetWindowThreadProcessId(Wnd, Pid);
        Proc := OpenProcess(SYNCHRONIZE or PROCESS_TERMINATE, False, Pid);
        PostMessage(Wnd, WM_CLOSE, 0, 0);
        if Proc <> 0 then
        begin
          if WaitForSingleObject(Proc, 5000) = WAIT_TIMEOUT then
          begin
            Log(Format('Copipe (PID %d) が正常に終了しなかったので強制終了します', [Pid]));
            TerminateProcess(Proc, 1);
            WaitForSingleObject(Proc, 5000);
          end;
          CloseHandle(Proc);
        end;
      end;
      Prev := Wnd;
    end;
  until Wnd = 0;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopCopipe;
  Result := '';
end;

// アンインストールの最後に、設定・履歴・定型文を消すか聞く。
// 既定のボタンは「いいえ」。サイレント (/SUPPRESSMSGBOXES) のときも「いいえ」として、消さない
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  DataDir := ExpandConstant('{localappdata}\Copipe');
  if not DirExists(DataDir) then Exit;
  if SuppressibleMsgBox(FmtMessage(CustomMessage('DeleteDataPrompt'), [DataDir]),
       mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
  begin
    if not DelTree(DataDir, True, True, True) then
      Log('データのフォルダーを消せませんでした: ' + DataDir);
  end;
end;

function InitializeUninstall(): Boolean;
begin
  StopCopipe;
  Result := True;
end;
