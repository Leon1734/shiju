# -*- coding: utf-8 -*-
"""纯标准库生成应用/托盘图标 icon.ico（16/24/32/48/64 BMP + 256 PNG）。
图形：蓝紫渐变圆角方块 + 白色引号（两根圆角竖条 + 两个圆点）。
"""
import math
import os
import struct
import zlib

OUT = r"E:\Workspace_AI\ZCODE\demo_0820\QuoteWidget\Assets\icon.ico"
SS = 4  # 超采样倍数，用于抗锯齿

TOP = (0x5B, 0x8D, 0xEF)   # 顶部颜色
BOT = (0x8A, 0x6C, 0xF5)   # 底部颜色


def clamp(v, a, b):
    return a if v < a else b if v > b else v


def sd_round_box(px, py, cx, cy, hw, hh, r):
    dx, dy = abs(px - cx) - hw, abs(py - cy) - hh
    ox, oy = max(dx, 0.0), max(dy, 0.0)
    return math.hypot(ox, oy) + min(max(dx, dy), 0.0) - r


def sd_circle(px, py, cx, cy, r):
    return math.hypot(px - cx, py - cy) - r


def render(size):
    """返回 [(b, g, r, a), ...] 的 size*size 像素列表（抗锯齿）。"""
    S = size * SS
    half = size / 2.0
    corner = 0.24 * size
    mx1, mx2 = size * 0.335, size * 0.665   # 两个引号的中线
    bw = size * 0.09                        # 圆角条宽度
    bh = size * 0.17                        # 圆角条高度
    bar_cy = size * 0.37
    ball_cy = size * 0.55
    br = size * 0.088                       # 圆点半径

    # 累加器：带 alpha 加权的降采样，避免直通色带来的边缘杂色
    acc_a = [0.0] * (size * size)
    acc = [[0.0, 0.0, 0.0] for _ in range(size * size)]

    for y in range(S):
        fy = (y + 0.5) / SS
        t = fy / size
        cr = TOP[0] + (BOT[0] - TOP[0]) * t
        cg = TOP[1] + (BOT[1] - TOP[1]) * t
        cb = TOP[2] + (BOT[2] - TOP[2]) * t
        for x in range(S):
            fx = (x + 0.5) / SS
            bg = clamp(0.5 - sd_round_box(fx, fy, half, half, half - 1, half - 1, corner) * SS, 0.0, 1.0)
            if bg <= 0.0:
                continue
            fg = min(
                sd_round_box(fx, fy, mx1, bar_cy, bw / 2, bh / 2, bw / 2),
                sd_circle(fx, fy, mx1, ball_cy, br),
                sd_round_box(fx, fy, mx2, bar_cy, bw / 2, bh / 2, bw / 2),
                sd_circle(fx, fy, mx2, ball_cy, br),
            )
            fg = clamp(0.5 - fg * SS, 0.0, 1.0)
            a = fg if fg > bg else bg
            if fg > 0.0:
                r_ = 255.0 * fg + cr * bg * (1.0 - fg)
                g_ = 255.0 * fg + cg * bg * (1.0 - fg)
                b_ = 255.0 * fg + cb * bg * (1.0 - fg)
            else:
                r_, g_, b_ = cr, cg, cb
            ix, iy = int(fx), int(fy)
            idx = iy * size + ix
            acc_a[idx] += a
            acc[idx][0] += b_ * a
            acc[idx][1] += g_ * a
            acc[idx][2] += r_ * a

    px = []
    for i in range(size * size):
        s = acc_a[i]
        if s <= 0.0:
            px.append((0, 0, 0, 0))
        else:
            px.append((
                int(round(acc[i][0] / s)),
                int(round(acc[i][1] / s)),
                int(round(acc[i][2] / s)),
                int(round(s / (SS * SS) * 255)),
            ))
    return px


def bmp_entry(size, px):
    """ICO 内嵌 32bpp BMP（自下而上 + AND 掩码）。"""
    header = struct.pack(
        "<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0,
        size * size * 4 + ((size + 31) // 32) * 4 * size, 0, 0, 0, 0
    )
    rows = []
    for y in range(size - 1, -1, -1):
        row = bytearray()
        for x in range(size):
            b, g, r, a = px[y * size + x]
            row += struct.pack("BBBB", b, g, r, a)
        rows.append(bytes(row))
    mask_stride = ((size + 31) // 32) * 4
    mask = b"\x00" * (mask_stride * size)
    return header + b"".join(rows) + mask


def png_entry(size, px):
    def chunk(typ, data):
        c = struct.pack(">I", len(data)) + typ + data
        return c + struct.pack(">I", zlib.crc32(typ + data) & 0xFFFFFFFF)

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    raw = bytearray()
    for y in range(size):
        raw.append(0)
        for x in range(size):
            raw += struct.pack("BBBB", *px[y * size + x])
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", ihdr)
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + chunk(b"IEND", b""))


def main():
    sizes = [16, 24, 32, 48, 64, 256]
    entries = []
    images = []
    offset = 6 + 16 * len(sizes)
    for s in sizes:
        px = render(s)
        data = png_entry(s, px) if s == 256 else bmp_entry(s, px)
        entries.append((s, len(data), offset))
        images.append(data)
        offset += len(data)

    ico = struct.pack("<HHH", 0, 1, len(sizes))
    for s, n, off in entries:
        w = 0 if s >= 256 else s
        ico += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, n, off)
    ico += b"".join(images)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "wb") as f:
        f.write(ico)
    print(f"icon.ico written: {len(ico)} bytes, sizes={sizes}")


if __name__ == "__main__":
    main()
