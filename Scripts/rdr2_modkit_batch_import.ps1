# RDR2 → ModKit 批量导入：一批一次调用
#
# 一次调用 = 一个批次的完整动作：
#   A1 杀进程 → A2 起 launcher → A3 PLAY → CAUTION/SafeMode（谁先弹都行）
#   → A4 等主菜单 → A5 点「编辑器」→ A6 Window→Show Resource Browser
#   → B1 定位 Assets\<目标> → B2 空白处右键 → B3 Import new asset
#   → B4 「打开」对话框：粘贴源目录路径 + 回车 → Ctrl+A → 回车
#   → C 等跑完（贴图：不确认；网格：拉 rdr2_fbx_auto_import.ps1 跟着按回车）
#
# 用法（贴图，一批）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File rdr2_modkit_batch_import.ps1 `
#     -SourceDir "...\textures\_todo_albedo\batch_02_retry01" -DestRel "textures\albedo" -Kind texture
#
# 用法（网格，一批）：
#   ... -SourceDir "...\rdr2_models\bldg_01_retry01" -DestRel "models\bldg" -Kind mesh
#
# 参数：
#   -SourceDir      要导的源目录（全路径，目录内文件全选）
#   -DestRel        编辑器里的目标目录，相对 Modules\RDR2\Assets，例 "textures\albedo"
#   -Kind           texture | mesh
#   -NoRestart      编辑器已在跑就跳过 A1~A6（默认会重启）
#   -KeepEditorOpen 跑完不杀进程
#   -TimeoutMin     等导入完成的上限（默认 25 分钟）
param(
    [Parameter(Mandatory=$true)][string]$SourceDir,
    [Parameter(Mandatory=$true)][string]$DestRel,
    [ValidateSet('texture','mesh')][string]$Kind = 'texture',
    [string]$Launcher = 'H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\bin\Win64_Shipping_wEditor\TaleWorlds.MountAndBlade.Launcher.exe',
    [string]$ModuleRoot = 'H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\RDR2',
    [string]$AutoConfirm = 'H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Scripts\rdr2_geometry_auto_import.ps1',
    [string]$LogDir = 'H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\RD2构造相关',
    [switch]$NoRestart,
    [switch]$KeepEditorOpen,
    [int]$TimeoutMin = 25
)
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class MK {
    public delegate bool P(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(P cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder t, int m);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder t, int m);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(int a, int b, bool f);
    [DllImport("kernel32.dll")] public static extern int GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    public struct RECT { public int L,T,R,B; }
    public static string T(IntPtr h){ var s=new StringBuilder(512); GetWindowText(h,s,s.Capacity); return s.ToString(); }
    public static string C(IntPtr h){ var s=new StringBuilder(256); GetClassName(h,s,s.Capacity); return s.ToString(); }
    public static List<IntPtr> All(){ var l=new List<IntPtr>(); EnumWindows(delegate(IntPtr h, IntPtr x){ l.Add(h); return true; }, IntPtr.Zero); return l; }
    public static IntPtr Find(string title){ foreach(var h in All()){ if(IsWindowVisible(h) && T(h)==title) return h; } return IntPtr.Zero; }
    public static IntPtr FindPrefix(string p){ foreach(var h in All()){ if(IsWindowVisible(h) && T(h).StartsWith(p)) return h; } return IntPtr.Zero; }
    public static bool Act(IntPtr h){
      int d; int fgT = GetWindowThreadProcessId(GetForegroundWindow(), out d); int myT = GetCurrentThreadId();
      bool att = (fgT != myT) && AttachThreadInput(myT, fgT, true);
      if (IsIconic(h)) ShowWindow(h, 9);
      BringWindowToTop(h); SetForegroundWindow(h);
      if (att) AttachThreadInput(myT, fgT, false);
      System.Threading.Thread.Sleep(220); return GetForegroundWindow()==h;
    }
    public static IntPtr At(int x,int y){ var p=new POINT(); p.X=x; p.Y=y; return WindowFromPoint(p); }
    public static IntPtr Root(IntPtr h){ return GetAncestor(h, 2); }
    public static void Click(int x,int y,int button){
      SetCursorPos(x,y); System.Threading.Thread.Sleep(130);
      uint down = button==2 ? 0x0008u : 0x0002u; uint up = button==2 ? 0x0010u : 0x0004u;
      mouse_event(down,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(70); mouse_event(up,0,0,0,IntPtr.Zero);
    }
    public static void DblClick(int x,int y){
      SetCursorPos(x,y); System.Threading.Thread.Sleep(120);
      for (int i=0;i<2;i++){ mouse_event(0x0002,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(35); mouse_event(0x0004,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(35); }
    }
}
"@
[MK]::SetProcessDPIAware() | Out-Null

# ---------------- 基础设施 ----------------
$script:LogFile = $null
function Log([string]$m) {
    $line = "[{0:HH:mm:ss}] {1}" -f (Get-Date), $m
    Write-Host $line
    if ($script:LogFile) { Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8 }
}
function Rect([IntPtr]$h) { $r = New-Object MK+RECT; [MK]::GetWindowRect($h, [ref]$r) | Out-Null; return $r }
function ClickRel([IntPtr]$h, [int]$ix, [int]$iy, [int]$btn = 1) { $r = Rect $h; [MK]::Click($r.L + $ix, $r.T + $iy, $btn) }
function Shot([IntPtr]$h, [string]$name) {
    $r = Rect $h; $w = $r.R - $r.L; $hh = $r.B - $r.T
    if ($w -le 0 -or $hh -le 0) { return $null }
    $bmp = New-Object System.Drawing.Bitmap($w, $hh)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size($w, $hh)))
    if ($script:LogDir -and $name) {
        if (-not (Test-Path $script:LogDir)) { New-Item -ItemType Directory -Path $script:LogDir -Force | Out-Null }
        $p = Join-Path $script:LogDir $name
        $bmp.Save($p, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $g.Dispose()
    return $bmp
}
function Wait-Window([string]$title, [int]$sec, [switch]$Prefix) {
    $end = (Get-Date).AddSeconds($sec)
    while ((Get-Date) -lt $end) {
        $h = if ($Prefix) { [MK]::FindPrefix($title) } else { [MK]::Find($title) }
        if ($h -ne [IntPtr]::Zero) { return $h }
        Start-Sleep -Milliseconds 400
    }
    return [IntPtr]::Zero
}
function Count-Assets([string]$kind) {
    $pat = if ($kind -eq 'mesh') { '*_geo.tpac' } else { '*_tex.tpac' }
    $roots = if ($kind -eq 'mesh') { @("$ModuleRoot\Assets\models", "$ModuleRoot\Assets\decal_models") } else { @("$ModuleRoot\Assets\textures") }
    $n = 0
    foreach ($r in $roots) { if (Test-Path $r) { $n += (Get-ChildItem $r -Recurse -File -Filter $pat | Measure-Object).Count } }
    return $n
}

# 关 Safe Mode 对话框：UIA 按名字点「否」→ Alt+N → 客户区坐标三层兜底
function Dismiss-SafeMode([IntPtr]$h) {
    try {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)
        foreach ($b in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
            $n = $b.Current.Name
            if ($n -match '否|No|N\)') {
                try { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Log ("Safe Mode: UIA 点「{0}」" -f $n); return $true } catch { }
            }
        }
        Log 'Safe Mode: UIA 没找到「否」，改键盘'
    } catch { Log ("Safe Mode: UIA 异常 " + $_.Exception.Message) }

    [MK]::Act($h) | Out-Null
    Start-Sleep -Milliseconds 250
    [System.Windows.Forms.SendKeys]::SendWait('%n')
    Start-Sleep -Milliseconds 900
    if ([MK]::Find('Safe Mode') -eq [IntPtr]::Zero) { Log 'Safe Mode: Alt+N 关掉了'; return $true }

    # 客户区坐标兜底（按客户区而不是窗口矩形算，避免标题栏偏移）
    $cr = New-Object MK+RECT
    [MK]::GetClientRect($h, [ref]$cr) | Out-Null
    $p = New-Object MK+POINT; $p.X = 0; $p.Y = 0
    [MK]::ClientToScreen($h, [ref]$p) | Out-Null
    $cx = $p.X + ($cr.R - 60); $cy = $p.Y + ($cr.B - 20)
    Log ("Safe Mode: 客户区坐标点击 ({0},{1})，客户区 {2}x{3}" -f $cx, $cy, $cr.R, $cr.B)
    [MK]::Click($cx, $cy, 1)
    Start-Sleep -Milliseconds 900
    return ([MK]::Find('Safe Mode') -eq [IntPtr]::Zero)
}

# 关掉任何阻塞启动的模态框（RGL CONTENT WARNING / 错误框等），Safe Mode 走专门逻辑
# 🔴 必须防「窗口无响应把脚本拖死」：2026-11-10 02:20 游戏崩溃后留下
#    "The application faced a problem" 提示框，SetForegroundWindow/SendKeys.SendWait
#    对死窗口会永久阻塞 —— 脚本一挂就是 5 小时。这里改成：
#      · 先查拥有者进程 Responding，不响应就只记录、绝不 Act/Send
#      · 发键改用非阻塞的 Send()
# 🔴 2026-11-10 07:30 真正的坑在这里：导入的网格引用**不存在的材质**时，编辑器加载
#    会对每个网格弹一次 "Unable to find material for mesh xxx"，几百个，一个接一个。
#    原来「一次调用只关一个框」根本追不上 → 永远进不了编辑器。改成**循环清空直到没有**。
function Clear-BlockingDialogs {
    $dismissed = 0
    for ($round = 0; $round -lt 400; $round++) {
        $hit = $false
        foreach ($h in [MK]::All()) {
            if (-not [MK]::IsWindowVisible($h)) { continue }
            if ([MK]::C($h) -notmatch '32770') { continue }
            $t = [MK]::T($h)
            if ($t -match 'Safe Mode') { [void](Dismiss-SafeMode $h); $hit = $true; break }
            if ($t -notmatch '(?i)warning|error|警告|错误|失败|faced a problem') { continue }

            $procId = 0
            [MK]::GetWindowThreadProcessId($h, [ref]$procId) | Out-Null
            $alive = $true
            try { $alive = (Get-Process -Id $procId -ErrorAction Stop).Responding } catch { $alive = $false }
            if (-not $alive) { continue }

            [MK]::Act($h) | Out-Null
            Start-Sleep -Milliseconds 120
            [System.Windows.Forms.SendKeys]::Send('{ENTER}')
            $dismissed++
            $hit = $true
            if ($dismissed % 5 -eq 1) { Log ("关掉阻塞提示框 #{0}: {1}" -f $dismissed, $t) }
            Start-Sleep -Milliseconds 220
            break
        }
        if (-not $hit) { break }
    }
    if ($dismissed -gt 0) { Log ("本轮共关掉 {0} 个阻塞提示框" -f $dismissed) }
    return $dismissed
}

# ---------------- A. 启动链 ----------------
function Start-Editor {
    $p = Get-Process | Where-Object { $_.ProcessName -match 'TaleWorlds|Watchdog' }
    if ($p) { Log ("杀进程: " + (($p | ForEach-Object { "$($_.ProcessName)[$($_.Id)]" }) -join ', ')); $p | Stop-Process -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 3 }

    Log "启动 launcher"
    Start-Process -FilePath $Launcher -WorkingDirectory (Split-Path $Launcher) | Out-Null
    $lw = Wait-Window 'M&B II: Bannerlord' 40
    if ($lw -eq [IntPtr]::Zero) { Log 'FAIL 没等到 launcher 窗口'; return $false }
    Start-Sleep -Seconds 6

    $r = Rect $lw
    $game = [IntPtr]::Zero
    foreach ($cand in @(@(240,615), @(240,600), @(240,573), @(240,630))) {
        [MK]::Act($lw) | Out-Null; Start-Sleep -Milliseconds 350
        [MK]::Click($r.L + $cand[0], $r.T + $cand[1], 1)
        Log ("点 PLAY ({0},{1})" -f $cand[0], $cand[1])
        # 3 秒内处理可能出现的弹窗（CAUTION / Safe Mode，顺序不写死）
        $deadline = (Get-Date).AddSeconds(8)
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 600
            $sm = [MK]::Find('Safe Mode')
            if ($sm -ne [IntPtr]::Zero) {
                Log 'Safe Mode 出现 → 关它'
                [void](Dismiss-SafeMode $sm)
                Start-Sleep -Milliseconds 800
                continue
            }
            $game = [MK]::FindPrefix('Mount and Blade II Bannerlord')
            if ($game -ne [IntPtr]::Zero) { break }
            # CAUTION 只有点 PLAY 后 2~4 秒出现，用固定位置兜底点一次
            [MK]::Click($r.L + 596, $r.T + 620, 1)
        }
        if ($game -eq [IntPtr]::Zero) { $game = [MK]::FindPrefix('Mount and Blade II Bannerlord') }
        if ($game -ne [IntPtr]::Zero) { break }
    }
    if ($game -eq [IntPtr]::Zero) {
        Log '游戏窗口还没出现，继续轮询 150 秒'
        $game = Wait-Window 'Mount and Blade II Bannerlord' 150 -Prefix
    }
    if ($game -eq [IntPtr]::Zero) { Log 'FAIL 游戏窗口没出现'; return $false }
    Log '游戏窗口出现，等主菜单加载（25 秒 + 截图留证）'
    Start-Sleep -Seconds 25
    Clear-BlockingDialogs
    Shot $game 'game_menu.png' | Out-Null

    # 点「编辑器」（失败就截图 + 检查游戏进程是否还活着）
    $ed = [IntPtr]::Zero
    $attempt = 0
    foreach ($yy in @(310, 305, 315, 320, 300, 290, 310, 305)) {
        $attempt++
        Clear-BlockingDialogs
        $alive = Get-Process -Id (Get-Process | Where-Object { $_.ProcessName -match 'TaleWorlds' } | Select-Object -First 1).Id -ErrorAction SilentlyContinue
        if (-not $alive) { Log 'FAIL 游戏进程已消失（疑似崩溃）'; return $false }
        [MK]::Act($game) | Out-Null; Start-Sleep -Milliseconds 400
        [MK]::Click(285, $yy, 1)
        Log ("点「编辑器」({0}) 第 {1} 次" -f $yy, $attempt)
        $ed = Wait-Window 'Edit Mode' 25
        if ($ed -ne [IntPtr]::Zero) { break }
        Clear-BlockingDialogs
        if ($attempt -in @(2, 5)) { Shot $game ("game_fail_{0}.png" -f $attempt) | Out-Null }
    }
    if ($ed -eq [IntPtr]::Zero) { Log 'FAIL 编辑器没起来（已存 game_fail_*.png）'; return $false }
    Log 'Edit Mode 出现，等加载 30 秒'
    Start-Sleep -Seconds 30

    # 打开资源浏览器（两套坐标，按窗口尺寸选）
    $rb = [MK]::Find('Resource Browser')
    if ($rb -eq [IntPtr]::Zero) {
        [MK]::Act($ed) | Out-Null; Start-Sleep -Milliseconds 400
        foreach ($set in @(@(@(122,37),@(181,214)), @(@(100,35),@(181,214)))) {
            ClickRel $ed $set[0][0] $set[0][1]
            Start-Sleep -Milliseconds 700
            ClickRel $ed $set[1][0] $set[1][1]
            $rb = Wait-Window 'Resource Browser' 6
            if ($rb -ne [IntPtr]::Zero) { break }
            Log ("菜单坐标 {0} 没打开，换下一套" -f ($set[0] -join ','))
        }
    }
    if ($rb -eq [IntPtr]::Zero) { Log 'FAIL 资源浏览器没打开'; return $false }
    Log '资源浏览器已打开'
    return $true
}

# ---------------- 资源浏览器 ----------------
function Get-RB { [MK]::Find('Resource Browser') }

function Maximize-RB([IntPtr]$rb) {
    [MK]::ShowWindow($rb, 3) | Out-Null   # SW_MAXIMIZE
    Start-Sleep -Milliseconds 900
    $r = Rect $rb
    Log ("资源浏览器最大化 → ({0},{1}) {2}x{3}" -f $r.L, $r.T, ($r.R - $r.L), ($r.B - $r.T))
}

# 在图标网格里找一块“整块纯色”的空像素（两文件之间），避免右键点到文件上
function Find-EmptySpot([IntPtr]$rb) {
    $bmp = Shot $rb
    if ($bmp -eq $null) { return $null }
    $w = $bmp.Width; $h = $bmp.Height
    $x0 = 330; $x1 = [Math]::Min($w - 20, 1460)
    $y0 = 135; $y1 = [Math]::Min($h - 60, 1000)
    $found = $null
    for ($y = $y1; $y -ge $y0 -and -not $found; $y -= 7) {
        for ($x = $x1; $x -ge $x0; $x -= 7) {
            $c0 = $bmp.GetPixel($x, $y)
            if ($c0.R -gt 90 -or $c0.G -gt 90 -or $c0.B -gt 90) { continue }   # 背景是深色
            $ok = $true
            for ($dy = -6; $dy -le 6 -and $ok; $dy += 3) {
                for ($dx = -6; $dx -le 6; $dx += 3) {
                    $c = $bmp.GetPixel($x + $dx, $y + $dy)
                    if ([Math]::Abs($c.R - $c0.R) -gt 6 -or [Math]::Abs($c.G - $c0.G) -gt 6 -or [Math]::Abs($c.B - $c0.B) -gt 6) { $ok = $false; break }
                }
            }
            if ($ok) { $found = @($x, $y); break }
        }
    }
    $bmp.Dispose()
    return $found
}

# ---- UIA：读面包屑（例 "Modules/RDR2/Assets/textures/albedo"），用于校验当前目录 ----
function Get-RBCrumb([IntPtr]$rb) {
    try {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($rb)
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button)
        $els = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
        $items = @()
        foreach ($e in $els) {
            $n = $e.Current.Name
            if ($n -and $n.EndsWith('/')) { $items += , @([int]$e.Current.BoundingRectangle.X, $n.TrimEnd('/')) }
        }
        $items = $items | Sort-Object { $_[0] }
        return (($items | ForEach-Object { $_[1] }) -join '/')
    } catch { return '' }
}

# ---- UIA：按名字双击一个图标项（文件夹） ----
function Open-RBFolderByName([IntPtr]$rb, [string]$name) {
    try {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($rb)
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)
        $els = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
        foreach ($e in $els) {
            $b = $e.Current.BoundingRectangle
            if ($b.Width -gt 10 -and $b.Height -gt 10) {
                [MK]::DblClick([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2))
                return $true
            }
        }
    } catch { Log ("UIA 找 {0} 失败: {1}" -f $name, $_.Exception.Message) }
    return $false
}

# 导航到 Modules/RDR2/Assets/<destRel>，每一步都用面包屑校验；不达标直接返回 $false
function Nav-ToAssets([IntPtr]$rb, [string]$destRel) {
    $expect = 'Modules/RDR2/Assets/' + (($destRel -split '\\') -join '/')
    Log ("目标面包屑 = {0}" -f $expect)
    $crumb = Get-RBCrumb $rb
    Log ("当前面包屑 = {0}" -f $crumb)
    if ($crumb -eq $expect) { Log '已在目标目录'; return $true }

    # 回 Modules
    $clicked = $false
    try {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($rb)
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, 'Modules/')
        $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($el -ne $null) {
            $b = $el.Current.BoundingRectangle
            [MK]::Click([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2), 1)
            $clicked = $true
            Log '点了面包屑 Modules/'
        }
    } catch { }
    if (-not $clicked) { ClickRel $rb 396 117 }
    Start-Sleep -Milliseconds 1500

    $segs = @('RDR2', 'Assets') + (($destRel -split '\\') | Where-Object { $_ })
    foreach ($s in $segs) {
        $crumb = Get-RBCrumb $rb
        if ($crumb -match ('(^|/)' + [regex]::Escape($s) + '$')) { continue }
        $ok = $false
        for ($try = 1; $try -le 3; $try++) {
            if (-not (Open-RBFolderByName $rb $s)) { Log ("找不到图标项「{0}」" -f $s); Start-Sleep -Milliseconds 800; continue }
            Start-Sleep -Milliseconds 1600
            $now = Get-RBCrumb $rb
            Log ("  进「{0}」→ {1}" -f $s, $now)
            if ($now -match ([regex]::Escape($s) + '$')) { $ok = $true; break }
        }
        if (-not $ok) { Log ("FAIL 进不去「{0}」，当前 {1}" -f $s, (Get-RBCrumb $rb)); return $false }
    }
    $crumb = Get-RBCrumb $rb
    if ($crumb -ne $expect) { Log ("FAIL 面包屑不符：期望 {0}，实际 {1}" -f $expect, $crumb); return $false }
    Log ("导航完成：{0}" -f $crumb)
    return $true
}

function Invoke-ImportMenu([IntPtr]$rb) {
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $spot = Find-EmptySpot $rb
        if ($spot -eq $null) { Log 'FAIL 没找到空白像素'; return $false }
        Log ("右键空白点（窗口内）= ({0},{1})  第 {2} 次" -f $spot[0], $spot[1], $attempt)

        $before = Shot $rb
        ClickRel $rb $spot[0] $spot[1] 2
        Start-Sleep -Milliseconds 900
        $after = Shot $rb
        if ($before -eq $null -or $after -eq $null) { return $false }

        # 像素差找菜单矩形（新出现的那一块）
        $minX = 99999; $minY = 99999; $maxX = -1; $maxY = -1
        for ($y = 120; $y -lt [Math]::Min($after.Height - 30, 1010); $y += 3) {
            for ($x = 300; $x -lt $after.Width - 4; $x += 3) {
                $a = $before.GetPixel($x, $y); $b = $after.GetPixel($x, $y)
                if (([Math]::Abs($a.R - $b.R) + [Math]::Abs($a.G - $b.G) + [Math]::Abs($a.B - $b.B)) -gt 60) {
                    if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
                    if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
                }
            }
        }
        $before.Dispose(); $after.Dispose()

        if ($maxX -lt 0) { Log '右键后画面无变化（菜单没出来）'; [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); continue }
        $mw = $maxX - $minX; $mh = $maxY - $minY
        Log ("变化矩形 = ({0},{1}) {2}x{3}" -f $minX, $minY, $mw, $mh)
        if ($mw -lt 60 -or $mw -gt 700 -or $mh -lt 40 -or $mh -gt 600) { Log '变化区域不像菜单，重试'; [System.Windows.Forms.SendKeys]::SendWait('{ESC}'); continue }

        # 最后一项 = Import new asset（菜单最底部，往上收 14px）
        $rr = Rect $rb
        $cx = [int]($minX + $mw / 2); $cy = [int]($maxY - 14)
        [MK]::Click($rr.L + $cx, $rr.T + $cy, 1)
        Log ("点菜单最后一项 @({0},{1})" -f $cx, $cy)

        if ((Wait-Window '打开' 10) -ne [IntPtr]::Zero) { Log '「打开」对话框已出现'; return $true }
        Log '没等到「打开」对话框，重试'
        [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
        Start-Sleep -Milliseconds 600
    }
    Log 'FAIL 没能打开导入对话框'
    return $false
}

function Select-FilesInDialog([string]$dir, [string]$wildcard) {
    $dlg = Wait-Window '打开' 20
    if ($dlg -eq [IntPtr]::Zero) { Log 'FAIL 没等到「打开」对话框'; return $false }
    Start-Sleep -Milliseconds 1000
    [MK]::Act($dlg) | Out-Null
    $r = Rect $dlg
    $target = ($dir.TrimEnd('\') + '\' + $wildcard)
    Set-Clipboard -Value $target

    # ① 文件名框：粘贴「完整路径 + 通配符」→ 回车（跳到该目录并选中匹配文件）
    [MK]::Click($r.L + 686, $r.B - 70, 1)
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait('^a')
    Start-Sleep -Milliseconds 150
    [System.Windows.Forms.SendKeys]::SendWait('^v')
    Start-Sleep -Milliseconds 700
    Shot $dlg 'dialog_pasted.png' | Out-Null
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Milliseconds 1800
    if ([MK]::Find('打开') -eq [IntPtr]::Zero) { Log ("已提交「打开」: {0}" -f $target); return $true }

    # ② 真实触发「打开」：焦点移到文件列表 → Ctrl+A 全选 → 回车
    for ($try = 1; $try -le 3; $try++) {
        [MK]::Click($r.L + 700, $r.T + 420, 1)      # 列表区
        Start-Sleep -Milliseconds 400
        [System.Windows.Forms.SendKeys]::SendWait('^a')
        Start-Sleep -Milliseconds 900
        Shot $dlg 'dialog_selected.png' | Out-Null
        [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
        Start-Sleep -Milliseconds 2200
        if ([MK]::Find('打开') -eq [IntPtr]::Zero) { Log ("已提交「打开」: {0}（第 {1} 次）" -f $target, $try); return $true }
        Log ("列表全选+回车没生效，第 {0} 次重试" -f $try)
    }
    Log 'FAIL 文件对话框没提交'
    return $false
}

# ---------------- 主流程 ----------------
$script:LogDir = $LogDir
if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$script:LogFile = Join-Path $LogDir ("import_{0}.log" -f $stamp)

Log ("==== 批次导入开始: {0} -> Assets\{1} ({2}) ====" -f $SourceDir, $DestRel, $Kind)
if (-not (Test-Path $SourceDir)) { Log 'FAIL 源目录不存在'; exit 2 }
$srcCount = (Get-ChildItem $SourceDir -File | Measure-Object).Count
Log ("源文件数 = {0}" -f $srcCount)
if ($srcCount -eq 0) { Log 'FAIL 源目录是空的'; exit 2 }

$before = Count-Assets $Kind
Log ("导入前资产数 = {0}" -f $before)

$rb = [IntPtr]::Zero
if (-not $NoRestart) {
    if (-not (Start-Editor)) { exit 10 }
    $rb = Get-RB
} else {
    $rb = Get-RB
    if ($rb -eq [IntPtr]::Zero) { Log 'FAIL -NoRestart 但资源浏览器不在'; exit 11 }
}
Maximize-RB $rb
if (-not (Nav-ToAssets $rb $DestRel)) {
    Shot $rb 'fail_nav.png' | Out-Null
    Log 'FAIL 导航没到目标目录，中止（不做右键、不导入）'
    exit 14
}
Shot $rb 'last_before_import.png' | Out-Null

$ok = Invoke-ImportMenu $rb
if (-not $ok) { Shot $rb 'fail_menu.png' | Out-Null; exit 12 }
$wild = if ($Kind -eq 'mesh') { '*.trf' } else { '*.png' }

# 网格：确认框必须在「打开」提交前就守上，否则第一个框会漏
#   用 UIA 点 Import 按钮的确认器（回车推不动某些框，踩过：一分钟才前进一个）
#   🔴 必须用「整串命令行 + cmd /c」拉起：路径含 `&`（Mount & Blade），
#      用 -ArgumentList 数组会被截断，确认器静默早退（连 start: 都不写）
$watcher = $null
if ($Kind -eq 'mesh') {
    Log '启动网格确认器（UIA 点 Import/Update）'
    # 先清掉上一批残留的确认器，避免多个同时抢同一个弹窗
    $me = $PID
    Get-CimInstance Win32_Process -Filter "Name like '%powershell%'" |
        Where-Object { $_.ProcessId -ne $me -and $_.CommandLine -match 'geometry_auto_import' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
    $confCmd = 'powershell -NoProfile -ExecutionPolicy Bypass -File "' + $AutoConfirm + '" -IdleStopSec 1800 -TimeoutMin ' + $TimeoutMin + ' -GapMs 250'
    $watcher = Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', $confCmd) -PassThru -WindowStyle Hidden
    Start-Sleep -Milliseconds 1500
    # 三条判据里的第一条：进程必须在
    $alive = Get-CimInstance Win32_Process -Filter "Name like '%powershell%'" |
        Where-Object { $_.CommandLine -match 'geometry_auto_import' }
    if ($alive) { Log ("确认器已拉起 pid={0}" -f (($alive | ForEach-Object { $_.ProcessId }) -join ',')) }
    else { Log 'FAIL 确认器没起来，中止（否则会卡在 Geometry 对话框）'; exit 15 }
}
if (-not (Select-FilesInDialog $SourceDir $wild)) { exit 13 }

# 等完成：数量稳定 + 没有 Importing file 窗口
$deadline = (Get-Date).AddMinutes($TimeoutMin)
$last = $before; $stable = 0; $done = $false
# 网格每个文件要编译几十秒，稳定判定必须拉长（贴图 18 秒；网格 2 分钟）
$needStable = if ($Kind -eq 'mesh') { 20 } else { 3 }
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 6
    $n = Count-Assets $Kind
    $importing = [MK]::Find('Importing file')
    $geoDlg = [MK]::Find('Geometry File Import Settings')
    if ($n -eq $last -and $importing -eq [IntPtr]::Zero -and $geoDlg -eq [IntPtr]::Zero) { $stable++ } else { $stable = 0 }
    $last = $n
    if (($stable -ge $needStable) -and ($n -gt $before)) { $done = $true; break }
    if ($stable -ge ($needStable * 2) -and $n -eq $before) { Log '数量一直没动，判失败/没导进去'; break }
}
if ($watcher -ne $null -and -not $watcher.HasExited) { Stop-Process -Id $watcher.Id -Force -ErrorAction SilentlyContinue }

$after = Count-Assets $Kind
Log ("导入后资产数 = {0}（新增 {1} / 源 {2}）" -f $after, ($after - $before), $srcCount)
if ($done) { Log 'DONE' } else { Log 'DONE_WITH_WARNING' }

if (-not $KeepEditorOpen) {
    $p = Get-Process | Where-Object { $_.ProcessName -match 'TaleWorlds|Watchdog' }
    if ($p) { Log '收尾：杀进程'; $p | Stop-Process -Force -ErrorAction SilentlyContinue }
}
exit 0
