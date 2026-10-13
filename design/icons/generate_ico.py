"""由 zzzip.svg 的设计生成多尺寸 .ico 与 PNG 预览。

不依赖 SVG 光栅化库：直接用 Pillow 按同一套几何参数重绘，
这样 16px 小尺寸下也能手动控制笔画粗细，避免缩小后糊成一团。
"""
from PIL import Image, ImageDraw, ImageFont
import os

N = 256
S = 4                      # 4 倍超采样后再缩小，获得抗锯齿边缘
W = N * S
ACCENT_TOP = (0x2B, 0xBF, 0x6E)
ACCENT_BOT = (0x12, 0x8C, 0x48)
ACCENT = (0x12, 0x8C, 0x48)


def s(v):
    return int(round(v * S))


def build(size_scale=1.0):
    """size_scale < 1 时加粗关键笔画，保证小尺寸可辨识。"""
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))

    # 1) 绿色渐变圆角底
    grad = Image.new("RGB", (1, W))
    for y in range(W):
        t = y / (W - 1)
        grad.putpixel((0, y), tuple(
            int(ACCENT_TOP[i] + (ACCENT_BOT[i] - ACCENT_TOP[i]) * t) for i in range(3)))
    grad = grad.resize((W, W))

    mask = Image.new("L", (W, W), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [s(12), s(12), s(244), s(244)], radius=s(46), fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)

    # 2) 白色压缩包本体
    d.rounded_rectangle([s(72), s(52), s(184), s(204)], radius=s(10),
                        fill=(255, 255, 255, 245))

    # 3) 竖直拉链条
    zipper_w = s(12 * size_scale)
    cx = s(128)
    d.rectangle([cx - zipper_w // 2, s(52), cx + zipper_w // 2, s(204)], fill=ACCENT)

    # 4) 拉链齿
    tooth_w = max(s(4), 2)
    y = s(62)
    while y < s(198):
        d.rectangle([cx - tooth_w // 2, y, cx + tooth_w // 2, y + s(8)], fill=(255, 255, 255))
        y += s(16)

    # 5) 拉链头
    r_outer = s(13 * size_scale)
    d.ellipse([cx - r_outer, s(214) - r_outer, cx + r_outer, s(214) + r_outer],
              fill=(255, 255, 255))
    r_inner = s(6 * size_scale)
    d.ellipse([cx - r_inner, s(214) - r_inner, cx + r_inner, s(214) + r_inner], fill=ACCENT)

    # 6) 左侧字母 z
    font_size = s(64 * size_scale)
    font = None
    for path in (r"C:\Windows\Fonts\segoeuib.ttf", r"C:\Windows\Fonts\arialbd.ttf",
                 r"C:\Windows\Fonts\msyhbd.ttc"):
        if os.path.exists(path):
            try:
                font = ImageFont.truetype(path, font_size)
                break
            except OSError:
                continue
    if font is None:
        font = ImageFont.load_default()

    d.text((s(98), s(108)), "z", font=font, fill=ACCENT, anchor="mm")

    return img.resize((N, N), Image.LANCZOS)


def main():
    # 脚本位于 <root>/design/icons/，需上溯三级到仓库根
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    icon_dir = os.path.join(root, "design", "icons")
    assets_dir = os.path.join(root, "src", "zzzip.App", "Assets")
    os.makedirs(assets_dir, exist_ok=True)

    base = build(1.0)
    small = build(1.45)     # 小尺寸加粗笔画

    png_path = os.path.join(icon_dir, "zzzip.png")
    base.save(png_path, "PNG")
    print("PNG:", png_path)

    # 多尺寸 ICO：16/24/32/48 用加粗版，64 以上用标准版
    frames = []
    for sz in (16, 24, 32, 48):
        frames.append(small.resize((sz, sz), Image.LANCZOS))
    for sz in (64, 128, 256):
        frames.append(base.resize((sz, sz), Image.LANCZOS))

    ico_design = os.path.join(icon_dir, "zzzip.ico")
    frames[-1].save(ico_design, format="ICO",
                    sizes=[(f.width, f.height) for f in frames],
                    append_images=frames[:-1])
    print("ICO(design):", ico_design)

    ico_assets = os.path.join(assets_dir, "zzzip.ico")
    frames[-1].save(ico_assets, format="ICO",
                    sizes=[(f.width, f.height) for f in frames],
                    append_images=frames[:-1])
    print("ICO(app):", ico_assets)
    print("sizes:", [f.size for f in frames])


if __name__ == "__main__":
    main()
