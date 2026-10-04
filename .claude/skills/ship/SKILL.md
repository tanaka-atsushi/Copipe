---
name: ship
description: ビルド → 起動 → バージョンを上げる → コミット、を CLAUDE.md のルールどおりに行う
disable-model-invocation: true
---

# アップデートをコミットする

引数: `$ARGUMENTS` (新しいバージョン番号を指定するときだけ。例: `1.1.0`)

1. `src/AssemblyInfo.cs` の AssemblyVersion / AssemblyFileVersion / AssemblyInformationalVersion の 3 つを上げる。
   - 引数があればその番号に、なければ末尾の数字を 1 つ上げる (1.0.4 → 1.0.5。4 桁の 2 つは `1.0.5.0`)。
   - ビルドより先に上げる。bin\Copipe.exe に新しいバージョンが入っていないと、tools\Release.ps1 が止まる。
2. `.\build.ps1` でビルドする。失敗したら直してからやり直し、成功するまで先に進まない。
3. `Start-Process bin\Copipe.exe` で起動する (build.ps1 は動いている Copipe を終了させる)。
4. 機能や動作を変えたのに `tools\verify.ps1` が合わせて更新されていなければ、先に更新する。実行するかどうかはユーザーに聞く (`/verify`)。
   README.md (英語) と README.ja.md の両方も合わせて直す。
5. `git diff` を見て、`git log -5` の書き方にそろえたコミットメッセージを日本語で書く。
   - 1 行目: 何をしたか (ユーザーから見た変化)
   - 本文: 理由と、変えたところの箇条書き (`- `)。E2E を足したらそれも書く
   - 最後に system-reminder の Co-Authored-By 行
6. `bin\Copipe.exe` とバージョンの変更を含めて同じコミットにする。push はしない。
