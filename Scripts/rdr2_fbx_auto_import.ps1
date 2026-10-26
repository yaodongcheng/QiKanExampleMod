# RDR2 mesh batch import -- auto-click "Import" on the FBX Import Settings dialog.
#
# Why: ModKit asks for import settings ONCE for textures, but ONCE PER FILE for FBX
# (dialog title = "FBX Import Settings"; the default button is Import).
#
# Usage:
#   1) Start this in a separate PowerShell window:
#        powershell -ExecutionPolicy Bypass -File "H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Scripts\rdr2_fbx_auto_import.ps1"
#   2) In ModKit: multi-select FBX files -> Import.
#
# v2 (2026-10-26): armed/disarmed state machine.
#   Old version pressed Enter again if the dialog was still on screen after 700ms
#   (slow import under load) -> the editor started a SECOND import of the same file
#   -> "Item with same name already exists" warnings. Now we only re-arm after the
#   dialog has actually disappeared at least once.
#
# Params:
#   -Title           dialog title (default "FBX Import Settings")
#   -MaxCount        stop after N confirmations (0 = unlimited)
#   -TimeoutMinutes  give up after N minutes (default 180)
#   -DelayMs         minimum wait between confirmations (default 1500ms)
param(
    [string]$Title = "FBX Import Settings",
    [int]$MaxCount = 0,
    [int]$TimeoutMinutes = 180,
    [int]$DelayMs = 1500
)

Add-Type -AssemblyName System.Windows.Forms
$wsh = New-Object -ComObject WScript.Shell

$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$count = 0
$armed = $true
Write-Host "Watching for window: '$Title' (timeout ${TimeoutMinutes} min, Ctrl+C to stop)"

try {
    while ((Get-Date) -lt $deadline) {
        if ($MaxCount -gt 0 -and $count -ge $MaxCount) { break }
        $ok = $false
        try { $ok = $wsh.AppActivate($Title) } catch { $ok = $false }

        if (-not $ok) {
            # dialog gone -> allow the next confirmation
            $armed = $true
            Start-Sleep -Milliseconds 250
            continue
        }
        if (-not $armed) {
            # dialog still lingering after our Enter (editor busy): do NOT press again
            Start-Sleep -Milliseconds 300
            continue
        }

        Start-Sleep -Milliseconds 150
        [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
        $count++
        $armed = $false
        if ($count % 25 -eq 0) { Write-Host ("confirmed {0} at {1:HH:mm:ss}" -f $count, (Get-Date)) }
        Start-Sleep -Milliseconds $DelayMs
    }
} finally {
    Write-Host ("Done. Auto-confirmed {0} dialog(s)." -f $count)
}
