# Fallback only (first-run screens the bridge cannot reach): real keyboard/mouse input via SendInput.
#   input.ps1 -Key 0x39            press a key by scan code (0x39 Space, 0x1C Enter, 0x01 Esc)
#   input.ps1 -FX 0.5 -FY 0.8      left-click at a fraction of the primary screen
param([int]$Key = -1, [double]$FX = -1, [double]$FY = -1)
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class In2 {
  [StructLayout(LayoutKind.Explicit, Size = 40)]
  public struct INPUT {
    [FieldOffset(0)] public uint type;
    // MOUSEINPUT at 8: dx(4) dy(4) mouseData(4) dwFlags(4) time(4) pad(4) extra(8)
    [FieldOffset(8)] public int mdx; [FieldOffset(12)] public int mdy; [FieldOffset(16)] public uint mData; [FieldOffset(20)] public uint mFlags;
    // KEYBDINPUT at 8: wVk(2) wScan(2) dwFlags(4) time(4) pad(4) extra(8)
    [FieldOffset(8)] public ushort kVk; [FieldOffset(10)] public ushort kScan; [FieldOffset(12)] public uint kFlags;
  }
  [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  static uint One(INPUT i) { return SendInput(1, new[]{ i }, Marshal.SizeOf(typeof(INPUT))); }
  public static uint Key(ushort scan) {
    var d = new INPUT(); d.type = 1; d.kScan = scan; d.kFlags = 0x0008;
    var u = new INPUT(); u.type = 1; u.kScan = scan; u.kFlags = 0x0008 | 0x0002;
    uint r = One(d); System.Threading.Thread.Sleep(90); return r + One(u);
  }
  public static uint Click(int x, int y) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(120);
    var d = new INPUT(); d.type = 0; d.mFlags = 0x0002;
    var u = new INPUT(); u.type = 0; u.mFlags = 0x0004;
    uint r = One(d); System.Threading.Thread.Sleep(90); return r + One(u);
  }
}
"@
$p = Get-Process Cities2 -ErrorAction SilentlyContinue | Select-Object -First 1
if ($p) { [In2]::SetForegroundWindow($p.MainWindowHandle) | Out-Null; Start-Sleep -Milliseconds 300 }
if ($Key -ge 0) { Write-Output ("key {0}: {1} events" -f $Key, [In2]::Key([uint16]$Key)) }
if ($FX -ge 0) {
  $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $x = [int]($b.X + $b.Width * $FX); $y = [int]($b.Y + $b.Height * $FY)
  Write-Output ("click {0},{1}: {2} events" -f $x, $y, [In2]::Click($x, $y))
}
