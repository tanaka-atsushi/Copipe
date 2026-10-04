<#
    Copipe のダイアログのスクリーンショットを、日本語と英語の両方で撮る (文字の切れ・はみ出しの確認用)

    撮るダイアログ: 設定画面・定型文の登録と編集・グループの作成と名前の変更・Copipe について。
    確認のメッセージ (MessageBox) は Windows が文字に合わせて大きさを決めるので撮らない。
    ダイアログを画面の外に出し、DrawToBitmap でウインドウ自身に描かせて png にする。
    キーは tools\verify.ps1 の検証 3b と同じく ProcessCmdKey を直接呼んで入れるので、
    マウス・キーボードは使わない (ユーザーに確認せずに実行してよい。CLAUDE.md)。
    撮った画像は自分の目で見て確かめること。下の自動の判定は目安で、見落としもある。

    設定画面で撮る状態: 開いた直後・CapsLock・半角/全角・英数 (どちらも使えないキー)・数字キー・モードキーの 半角/全角・
    モードキーをホットキーと同じキーにしたとき (どれも、赤字の説明が出る状態)
    定型文・グループのダイアログは、空のときと、長めの場所・表示名・本文・パスが入っているとき

    使い方:  tools\Shoot-Dialogs.ps1                   bin\Copipe.exe を撮り、%TEMP%\CopipeShots に保存
             tools\Shoot-Dialogs.ps1 -OutDir <フォルダー>

    文字がはみ出していそうなものを見つけたら一覧を出し、終了コード 1 を返す。
#>
[CmdletBinding()]
param(
    [string]$OutDir = (Join-Path $env:TEMP 'CopipeShots'),
    [string]$ExePath,
    # 内部用。STA の Windows PowerShell で、読み込んだ exe を撮る側として動く
    [switch]$Child
)

$ErrorActionPreference = 'Stop'

if (-not $Child) {
    $root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
    if (-not $ExePath) { $ExePath = Join-Path $root 'bin\Copipe.exe' }
    if (-not (Test-Path -LiteralPath $ExePath)) { throw "Copipe.exe が見つかりません: $ExePath (先に build.ps1 でビルドする)" }
    if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }
    Get-ChildItem -LiteralPath $OutDir -Filter '*.png' | Remove-Item -Force
    # 読み込んだ exe はプロセスが終わるまでロックされるので、コピーを読む (ビルドを邪魔しない)
    $copy = Join-Path $OutDir 'Copipe.exe'
    Copy-Item -LiteralPath $ExePath -Destination $copy -Force
    # WinForms の画面を作るので STA で動かす。exe は .NET Framework 向けなので Windows PowerShell で読む
    & powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File $MyInvocation.MyCommand.Path -Child -ExePath $copy -OutDir $OutDir
    $code = $LASTEXITCODE
    Remove-Item -LiteralPath $copy -Force -ErrorAction SilentlyContinue
    Write-Host "保存先: $OutDir"
    exit $code
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -Namespace CopipeShots -Name Dpi -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();'
# Copipe と同じく DPI 対応を宣言する (しないと、拡大率が 100% でない画面でぼやけた絵になる)
[void][CopipeShots.Dpi]::SetProcessDPIAware()

$asm = [Reflection.Assembly]::LoadFrom($ExePath)
$K = [System.Windows.Forms.Keys]
$F = [Reflection.BindingFlags]'Public,NonPublic,Instance'
# SettingsDialog は internal なので、型は exe から名前で取り出す
$DT = $asm.GetType('Copipe.UI.SettingsDialog', $true)
$LangType = $asm.GetType('Copipe.Lang', $true)
$UL = $asm.GetType('Copipe.UiLanguage', $true)
$IC = $asm.GetType('Copipe.Services.InsertClick', $true)
$PD = $asm.GetType('Copipe.UI.PhraseDialog', $true)
$AD = $asm.GetType('Copipe.UI.AboutDialog', $true)
$SF = [Reflection.BindingFlags]'Public,NonPublic,Static'
$script:problems = 0

function Get-DialogField($d, [string]$Name) { return $DT.GetField($Name, $F).GetValue($d) }

function Send-DialogKey($d, [string]$Box, [System.Windows.Forms.Keys]$KeyData) {
    $d.ActiveControl = Get-DialogField $d $Box
    $msg = [System.Windows.Forms.Message]::Create($d.Handle, 0x0100, [IntPtr]([int]($KeyData -band $K::KeyCode)), [IntPtr]::Zero)
    [void]$DT.GetMethod('ProcessCmdKey', $F).Invoke($d, [object[]]@($msg, $KeyData))
    [System.Windows.Forms.Application]::DoEvents()
}

# 文字が欄に収まっているかの目安。大きさが自動 (AutoSize) のものは画面の右端を越えないか、
# 大きさが決まっているラベル (赤字の説明など) は折り返した高さが収まるか、ボタンは 1 行で幅に収まるかを見る
function Test-TextFit($d, [string]$Name) {
    foreach ($c in $d.Controls) {
        if (-not $c.Text) { continue }
        if ($c.AutoSize) {
            if ($c.Right -gt $d.ClientSize.Width) {
                Write-Host "  [$Name] 右端からはみ出し: '$($c.Text)' right=$($c.Right) width=$($d.ClientSize.Width)" -ForegroundColor Red
                $script:problems++
            }
            continue
        }
        if ($c -is [System.Windows.Forms.Label]) {
            $need = [System.Windows.Forms.TextRenderer]::MeasureText($c.Text, $c.Font, (New-Object System.Drawing.Size $c.Width, 0),
                                                                      [System.Windows.Forms.TextFormatFlags]'WordBreak').Height
            if ($need -gt $c.Height) {
                Write-Host "  [$Name] 高さが足りない: '$($c.Text)' need=$need height=$($c.Height)" -ForegroundColor Red
                $script:problems++
            }
        } elseif ($c -is [System.Windows.Forms.Button]) {
            $need = [System.Windows.Forms.TextRenderer]::MeasureText($c.Text, $c.Font).Width
            if ($need -gt $c.Width - 8) {
                Write-Host "  [$Name] 幅が足りない: '$($c.Text)' need=$need width=$($c.Width)" -ForegroundColor Red
                $script:problems++
            }
        }
    }
}

# ダイアログを画面の外に出して png にし、はみ出しの目安を調べる。$Prepare は表示した後に状態を作る (無ければ $null)
function Save-Shot($d, [string]$Name, [scriptblock]$Prepare) {
    try {
        $d.StartPosition = 'Manual'
        $d.Location = New-Object System.Drawing.Point -4000, 100
        $d.Show()
        [System.Windows.Forms.Application]::DoEvents()
        if ($Prepare) { & $Prepare $d }
        $bmp = New-Object System.Drawing.Bitmap $d.Width, $d.Height
        try {
            $d.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle 0, 0, $d.Width, $d.Height))
            $bmp.Save((Join-Path $OutDir "$Name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $bmp.Dispose() }
        Write-Host "$Name.png"
        Test-TextFit $d $Name
    } finally {
        $d.Close()
        $d.Dispose()
    }
}

foreach ($langName in 'Japanese', 'English') {
    $LangType.GetMethod('Apply').Invoke($null, @([Enum]::Parse($UL, $langName)))
    # 名前、キーを入れる欄、押すキー
    $states = @(
        @('initial', $null, $null),
        @('capslock', '_hotkeyBox', $K::Capital),
        @('zenkaku', '_hotkeyBox', [System.Windows.Forms.Keys]0xF3),
        @('eisu', '_hotkeyBox', [System.Windows.Forms.Keys]0xF0),
        @('digit', '_hotkeyBox', $K::D1),
        @('mode-zenkaku', '_modeKeyBox', [System.Windows.Forms.Keys]0xF4),
        @('mode-same', '_modeKeyBox', $K::Pause)
    )
    foreach ($st in $states) {
        $d = [Activator]::CreateInstance($DT, $F, $null,
            [object[]]@($K::Pause, $K::Tab, [Enum]::Parse($IC, 'Double'), [Enum]::Parse($UL, $langName)), $null)
        $prepare = $null
        if ($st[1]) {
            $box = $st[1]; $key = $st[2]
            $prepare = { param($dlg) Send-DialogKey $dlg $box $key }.GetNewClosure()
        }
        Save-Shot $d "$langName-settings-$($st[0])" $prepare
    }

    # 定型文・グループのダイアログ (ForPhrase・ForGroup は internal なので、名前で呼ぶ)。題と場所は CopipeApp と同じ形にする
    $ja = $langName -eq 'Japanese'
    $forPhrase = $PD.GetMethod('ForPhrase', $SF)
    $forGroup = $PD.GetMethod('ForGroup', $SF)
    if ($ja) {
        $first = '定型文 の 1 番'
        $deep = '定型文 > 社外のお客さま > 挨拶とお礼の定型文 の 10 番'
        $title = 'いつもお世話になっております (社外向けの長めの挨拶)'
        $groupName = '社外のお客さま向けの挨拶とお礼'
    } else {
        $first = 'Snippets, slot 1'
        $deep = 'Snippets > External customers > Greetings and thanks, slot 10'
        $title = 'Thank you for your continued support (long greeting)'
        $groupName = 'Greetings and thanks for customers'
    }
    $text = "いつもお世話になっております。`r`n株式会社サンプルの山田です。`r`n`r`n先日はありがとうございました。"
    $path = 'C:\Users\example\Documents\テンプレート\お客さま向け\挨拶とお礼の文例集.docx'
    Save-Shot ($forPhrase.Invoke($null, [object[]]@($(if ($ja) { '定型文を登録' } else { 'New snippet' }), $first, '', '', ''))) "$langName-phrase-new" $null
    Save-Shot ($forPhrase.Invoke($null, [object[]]@($(if ($ja) { '定型文を編集' } else { 'Edit snippet' }), $deep, $title, $text, $path))) "$langName-phrase-edit" $null
    Save-Shot ($forGroup.Invoke($null, [object[]]@($(if ($ja) { 'グループを作成' } else { 'New group' }), $first, ''))) "$langName-group-new" $null
    Save-Shot ($forGroup.Invoke($null, [object[]]@($(if ($ja) { 'グループの名前を変更' } else { 'Rename group' }), $deep, $groupName))) "$langName-group-rename" $null

    # Copipe について
    Save-Shot ([Activator]::CreateInstance($AD, $F, $null, [object[]]@(), $null)) "$langName-about" $null
}

if ($script:problems -gt 0) {
    Write-Host "文字が収まっていないかもしれないものが $($script:problems) 件あります。画像で確かめてください。" -ForegroundColor Red
    exit 1
}
Write-Host '自動の判定では、はみ出しは見つかりませんでした (画像でも確かめてください)。' -ForegroundColor Green
exit 0
