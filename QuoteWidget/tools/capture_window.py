# -*- coding: utf-8 -*-
"""按窗口标题截取真实运行画面，输出 PNG（物理像素，DPI 感知）。"""
import sys
import ctypes
from ctypes import wintypes
import os

import ctypes
user32 = ctypes.windll.user32
dwmapi = ctypes.windll.dwmapi
user32.SetProcessDPIAware()


class RECT(ctypes.Structure):
    _fields_ = [("L", ctypes.c_long), ("T", ctypes.c_long),
                ("R", ctypes.c_long), ("B", ctypes.c_long)]


def find_window(title):
    hwnd = user32.FindWindowW(None, title)
    return hwnd or None


if __name__ == "__main__":
    title, out = sys.argv[1], sys.argv[2]
    hwnd = find_window(title)
    if not hwnd:
        print(f"WINDOW NOT FOUND: {title}")
        sys.exit(1)
    rc = RECT()
    if dwmapi.DwmGetWindowAttribute(hwnd, 9, ctypes.byref(rc), ctypes.sizeof(rc)) != 0:
        user32.GetWindowRect(hwnd, ctypes.byref(rc))
    w, h = rc.R - rc.L, rc.B - rc.T
    print(f"rect={rc.L},{rc.T},{rc.R},{rc.B} size={w}x{h}")
    # 用 PowerShell 完成实际截屏（System.Drawing 更省事）
    import subprocess
    ps = (
        "Add-Type -AssemblyName System.Drawing;"
        f"$bmp = New-Object System.Drawing.Bitmap({w}, {h});"
        f"$g = [System.Drawing.Graphics]::FromImage($bmp);"
        f"$g.CopyFromScreen({rc.L}, {rc.T}, 0, 0, (New-Object System.Drawing.Size({w}, {h})));"
        f"$bmp.Save('{out}', [System.Drawing.Imaging.ImageFormat]::Png);"
        "$g.Dispose(); $bmp.Dispose()"
    )
    r = subprocess.run(["powershell", "-NoProfile", "-Command", ps],
                       capture_output=True, text=True)
    ok = os.path.exists(out)
    print("SAVED" if ok else "SAVE FAILED", out)
    sys.exit(0 if ok else 1)
