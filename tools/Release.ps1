<#
    GitHub Releases に新しいバージョンを公開する

    使い方:  .\tools\Release.ps1 -DryRun       インストーラーと zip を作り、リリースノートを見せるだけ (公開しない)
             .\tools\Release.ps1               公開する (タグ v<版> を付けて gh release create)
             .\tools\Release.ps1 -UpdateNotes  公開済みのリリースのノートだけを書き直す

    前に済ませておくこと:
      1. バージョンを上げて build.ps1 でビルドし、tools\Build-Installer.ps1 でインストーラーを作り、bin の exe も含めてコミットする
      2. release-notes\v<版>.md に、そのバージョンで変わったことを英語で書いてコミットする
      3. main を push する

    exe とインストーラーは作り直さない。ビルドのたびに中身が変わるので、コミットしたものをそのまま配る。
    リリースノートは、ダウンロード・SmartScreen・カンパ・ライセンスの定型の文の間に release-notes\v<版>.md を挟んで作る。
    gh (GitHub CLI) にログインしている必要がある。
#>
[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$UpdateNotes
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$repo = (git -C $root remote get-url origin) -replace '^(https://github[.]com/|git@github[.]com:)|[.]git$', ''

Push-Location $root
try {
    # --- 公開してよいかの確認 ---------------------------------------------
    # -DryRun のときは止めずに、公開を止める理由として並べるだけにする
    $problems = New-Object System.Collections.Generic.List[string]

    $exe = Join-Path $root 'bin\Copipe.exe'
    $version = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
    $info = [IO.File]::ReadAllText((Join-Path $root 'src\AssemblyInfo.cs'))
    if ($info -notmatch 'AssemblyInformationalVersion\("([^"]+)"\)') {
        throw 'src\AssemblyInfo.cs に AssemblyInformationalVersion が見つかりません'
    }
    if ($Matches[1] -ne $version) {
        $problems.Add("bin\Copipe.exe のバージョン ($version) が AssemblyInfo.cs ($($Matches[1])) と違う。build.ps1 でビルドしてコミットする")
    }
    $tag = "v$version"

    $notesFile = Join-Path $root "release-notes\$tag.md"
    if (-not (Test-Path -LiteralPath $notesFile)) {
        throw "リリースノートがありません: $notesFile (そのバージョンで変わったことを英語で書く)"
    }

    if (git status --porcelain) {
        $problems.Add('コミットしていない変更がある')
    }
    if ((git rev-parse --abbrev-ref HEAD) -ne 'main') {
        $problems.Add('main ブランチではない')
    }
    git fetch -q origin
    if ($LASTEXITCODE -ne 0) { throw 'git fetch に失敗しました' }
    if ((git rev-parse HEAD) -ne (git rev-parse origin/main)) {
        $problems.Add('main が origin/main と同じではない (push していない、または pull していない)')
    }

    $tagExists = [bool](git ls-remote --tags origin "refs/tags/$tag") -or [bool](git tag -l $tag)
    if ($UpdateNotes) {
        if (-not $tagExists) { throw "$tag はまだ公開していません" }
    } elseif ($tagExists) {
        $problems.Add("タグ $tag はもうある。バージョンを上げる")
    }

    if ($problems.Count -gt 0) {
        $text = ($problems | ForEach-Object { "  - $_" }) -join "`n"
        if ($DryRun) {
            Write-Host "公開するときは、次の理由で止まります:`n$text" -ForegroundColor Yellow
        } elseif ($UpdateNotes) {
            # ノートの書き直しはファイルを配らないので、タグがあること以外は問わない
        } else {
            throw "公開できません:`n$text"
        }
    }

    # --- リリースノート ---------------------------------------------------
    $changes = [IO.File]::ReadAllText($notesFile).Trim()
    $notes = @"
Copipe shows your clipboard history next to the mouse cursor while you hold a hotkey.
Click an item (or press its number key) to type it into the app you are using.

## Download

| File | |
|---|---|
| **Copipe-Setup-$version.exe** | Installer. Installs for the current user, no admin rights needed. Uninstall from *Settings → Apps → Installed apps*. |
| **Copipe-$version.zip** | Portable. Unzip and run ``Copipe.exe``. |

Requires Windows 10 or 11 (.NET Framework 4.8, which comes with Windows).
The app is in English, or in Japanese when Windows is set to Japanese.

> **"Windows protected your PC"?** The files are not code-signed yet, so SmartScreen may warn you.
> Click **More info** → **Run anyway**.

## What's changed

$changes

## Support

If Copipe saves you time, you can [buy me a coffee](https://buymeacoffee.com/bigcomi) ☕

License: [GPL-3.0](https://github.com/$repo/blob/main/LICENSE) · © 2026 Atsushi Tanaka
"@
    $notesOut = Join-Path $env:TEMP "Copipe-release-notes-$tag.md"
    [IO.File]::WriteAllText($notesOut, $notes.Replace("`r`n", "`n"), (New-Object Text.UTF8Encoding $false))

    if ($UpdateNotes) {
        gh release edit $tag -R $repo --notes-file $notesOut
        if ($LASTEXITCODE -ne 0) { throw "リリースノートの書き直しに失敗しました (exit $LASTEXITCODE)" }
        Write-Host "リリースノートを書き直しました: https://github.com/$repo/releases/tag/$tag" -ForegroundColor Green
        return
    }

    # --- 配るファイル -----------------------------------------------------
    # インストーラーも作り直さず、コミットしたものを配る (作り直すと中身が変わる)
    $setup = Join-Path $root "bin\Copipe-Setup-$version.exe"
    if (-not (Test-Path -LiteralPath $setup)) { throw "インストーラーがありません: $setup (tools\Build-Installer.ps1 で作ってコミットする)" }
    # GPL-3.0 では、バイナリと一緒にライセンス本文を渡す
    $zip = Join-Path $root "bin\Copipe-$version.zip"
    Compress-Archive -Path $exe, (Join-Path $root 'LICENSE') -DestinationPath $zip -Force
    Write-Host "zip 作成成功: $zip" -ForegroundColor Green

    if ($DryRun) {
        Write-Host "`n-DryRun なので公開しません。リリースノート: $notesOut" -ForegroundColor Cyan
        Write-Host $notes
        return
    }

    # --- 公開 -------------------------------------------------------------
    gh release create $tag $setup $zip -R $repo --target (git rev-parse HEAD) --title "Copipe $version" --notes-file $notesOut --latest
    if ($LASTEXITCODE -ne 0) { throw "リリースの作成に失敗しました (exit $LASTEXITCODE)" }
    Write-Host "公開しました: https://github.com/$repo/releases/tag/$tag" -ForegroundColor Green
} finally {
    Pop-Location
}
