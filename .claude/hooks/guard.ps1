<#
    PreToolUse フック (PowerShell / Bash)。CLAUDE.md のルールのうち、破ると後始末が要るものを止める。
    exit 2 で止め、stderr の文が Claude に返る。
#>
[Console]::InputEncoding  = [Text.Encoding]::UTF8
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$cmd = ([Console]::In.ReadToEnd() | ConvertFrom-Json).tool_input.command
if (-not $cmd) { exit 0 }

# verify.ps1 の出力を途中で止まるパイプにつなぐと、finally が動かず設定・履歴・クリップボードが戻らない
if ($cmd -match 'verify\.ps1[^\r\n]*\|\s*(Select-Object\s+(-First|-Index)|select\s+-f|head\b)') {
    [Console]::Error.WriteLine('verify.ps1 の出力を途中で止まるパイプ (Select-Object -First / head) につながないでください。finally が動かず、settings.ini・history.json・クリップボードがテストの値のまま残ります。ファイルにリダイレクトするか、最後まで読んでください (Select-String ... | Select-Object -Last N など)。')
    exit 2
}

# src/ を変えるコミットでは src/AssemblyInfo.cs のバージョンを上げる (同じコミットに入れる)
if ($cmd -match '\bgit\b[^\r\n]*\bcommit\b') {
    Set-Location $env:CLAUDE_PROJECT_DIR
    # git add と同じコマンドでコミットすることもあるので、未ステージの変更も見る
    if ((git status --porcelain -- src) -and -not (git status --porcelain -- src/AssemblyInfo.cs)) {
        [Console]::Error.WriteLine('src/AssemblyInfo.cs のバージョンが上がっていません。AssemblyVersion / AssemblyFileVersion / AssemblyInformationalVersion の 3 つを上げ (指示がなければ末尾を 1 つ)、同じコミットに入れてください。')
        exit 2
    }
}
exit 0
