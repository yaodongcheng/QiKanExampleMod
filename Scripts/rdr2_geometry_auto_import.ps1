# RDR2 导入确认器（UIA 按按钮名点，支持多种弹窗）
#
# 为什么不用回车：Qt 这些窗口回车未必触发默认按钮，实测会「框一直在、一分钟才前进一个」。
# 用 UIA 直接 Invoke 后，6 个模型 2 秒内全部导完（2026-11-09 22:58）。
#
# 处理两类弹窗（按标题前缀匹配）：
#   Geometry File Import Settings → Import  （每个 TRF 必弹）
#   File already exists           → Update  （源镜像里已有同名 TRF 时弹，必须替换，不能 Cancel）
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File "…\rdr2_geometry_auto_import.ps1" -IdleStopSec 1800 -TimeoutMin 240
param(
    [int]$IdleStopSec = 600,
    [int]$TimeoutMin = 240,
    [int]$GapMs = 250,
    [string]$Log = 'H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\RD2构造相关\geometry_auto_import.log'
)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class CI {
  public delegate bool P(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(P cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder t, int m);
  public static string T(IntPtr h){ var s=new StringBuilder(300); GetWindowText(h,s,300); return s.ToString(); }
  public static List<IntPtr> All(){ var l=new List<IntPtr>(); EnumWindows(delegate(IntPtr h, IntPtr x){ l.Add(h); return true; }, IntPtr.Zero); return l; }
  public static IntPtr FindPrefix(string p){ foreach(var h in All()){ if(IsWindowVisible(h) && T(h).StartsWith(p)) return h; } return IntPtr.Zero; }
}
"@

# 标题前缀 → 要点哪个按钮
$rules = @(
    @{ Title = 'Geometry File Import Settings'; Button = 'Import' },
    @{ Title = 'File already exists';           Button = 'Update' },
    @{ Title = 'Asset already exists';          Button = 'Update' }
)

$dir = Split-Path $Log -Parent
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
function LogLine([string]$m) { Add-Content -LiteralPath $Log -Value ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $m) -Encoding UTF8 }

LogLine ("start: {0} rules, idle={1}s timeout={2}min" -f $rules.Count, $IdleStopSec, $TimeoutMin)
$deadline = (Get-Date).AddMinutes($TimeoutMin)
$clicks = 0
$lastClick = Get-Date
$lastLog = Get-Date
$perRule = @{}

while ((Get-Date) -lt $deadline) {
    if (Test-Path (Join-Path $env:TEMP 'RDR2_IMPORT_STOP')) { LogLine 'stop flag detected, exit'; break }
    $did = $false
    foreach ($rule in $rules) {
        $hw = [CI]::FindPrefix($rule.Title)
        if ($hw -eq [IntPtr]::Zero) { continue }
        try {
            $root = [System.Windows.Automation.AutomationElement]::FromHandle($hw)
            $cond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty, $rule.Button)
            $btn = $null
            foreach ($b in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
                if ($b.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $b.Current.IsEnabled) { $btn = $b; break }
            }
            if ($btn -eq $null) { continue }
            $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            $clicks++
            $lastClick = Get-Date
            if (-not $perRule.ContainsKey($rule.Title)) { $perRule[$rule.Title] = 0 }
            $perRule[$rule.Title] = $perRule[$rule.Title] + 1
            $did = $true
            break
        } catch { LogLine ("invoke error [" + $rule.Title + "]: " + $_.Exception.Message) }
    }
    if ($did) {
        if (((Get-Date) - $lastLog).TotalSeconds -ge 30) {
            $detail = ($perRule.GetEnumerator() | ForEach-Object { "{0}={1}" -f $_.Key, $_.Value }) -join ' '
            LogLine ("clicks = {0}  ({1})" -f $clicks, $detail)
            $lastLog = Get-Date
        }
        Start-Sleep -Milliseconds $GapMs
        continue
    }
    if ($clicks -gt 0 -and ((Get-Date) - $lastClick).TotalSeconds -ge $IdleStopSec) {
        LogLine ("idle {0}s after {1} clicks, exit" -f $IdleStopSec, $clicks); break
    }
    Start-Sleep -Milliseconds 400
}
$detail = ($perRule.GetEnumerator() | ForEach-Object { "{0}={1}" -f $_.Key, $_.Value }) -join ' '
LogLine ("done: {0} clicks  ({1})" -f $clicks, $detail)
