---
name: verify
description: tools\verify.ps1 を CLAUDE.md のルールどおりに実行する (確認・リダイレクト・中断時の復元)
disable-model-invocation: true
---

# Copipe の検証

引数: `$ARGUMENTS` (例: `-SkipE2E`、`-E2E History`、`-E2E Phrases`。空なら全部)

1. 実行する前に、AskUserQuestion でユーザーに確認する。
   - 何を流すか: 引数どおりのモードと、それにかかる時間 (全部の E2E は数分。`-SkipE2E` は数秒)
   - E2E はマウス・キーボードを使い、他のウインドウを最小化するので、その間は PC に触れないこと
   - 軽い選択肢として `-SkipE2E` (マウス・キーボードを使わない) を必ず挙げる
2. `bin\Copipe.exe` が最新のソースからビルドされていなければ、先に `.\build.ps1` を実行する。
3. Windows PowerShell 5.1 で実行し、出力はファイルにリダイレクトする。**途中で止まるパイプ (`Select-Object -First` など) にはつながない。**
   ```
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\verify.ps1 <引数> *> "$env:TEMP\copipe-verify.log"
   ```
   E2E を含むときは timeout を長め (600000 ms) にする。
4. ログの最後の `結果:` 行と、`[FAIL]` の行 (前後も) を読んで報告する。失敗したら、その出力をそのまま示す。
5. 実行が中断されたとき (ツール呼び出しを拒否された・タイムアウトした場合も同じ):
   - `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Restore-CopipeData.ps1` を実行して元に戻し、ユーザーにそのことを伝える。
   - もう一度実行する前に、クリップボードを空にし (`powershell.exe -NoProfile -STA -Command "Add-Type -A System.Windows.Forms; [Windows.Forms.Clipboard]::Clear()"`)、Copipe を起動し直す。
6. 終わったら `Start-Process bin\Copipe.exe` で Copipe を起動し直しておく。
