#!/usr/bin/env python3
"""将灰度纹理图按累计阈值输出为多张黑白二值图。"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image


def make_thresholds(layers: int) -> list[int]:
    """生成从 255 递减的等间隔累计阈值。

    例如 layers=10 时返回：
    [255, 230, 204, 178, 153, 128, 102, 76, 51, 26]
    """
    if layers < 1 or layers > 255:
        raise ValueError("layers 必须在 1 到 255 之间")

    values = np.linspace(255, 255 / layers, layers)
    return np.rint(values).astype(np.uint8).tolist()


def split_grayscale_layers(
    input_path: Path,
    output_dir: Path,
    layers: int = 10,
    *,
    below_is_black: bool = True,
) -> list[Path]:
    """生成累计阈值二值图并返回输出文件路径。"""
    output_dir.mkdir(parents=True, exist_ok=True)

    with Image.open(input_path) as source:
        gray = np.asarray(source.convert("L"), dtype=np.uint8)
        dpi = source.info.get("dpi")

    thresholds = make_thresholds(layers)
    digits = len(str(layers))
    output_paths: list[Path] = []

    for index, threshold in enumerate(thresholds, start=1):
        is_below = gray < threshold
        if below_is_black:
            binary = np.where(is_below, 0, 255).astype(np.uint8)
        else:
            binary = np.where(is_below, 255, 0).astype(np.uint8)

        output_path = output_dir / (
            f"layer_{index:0{digits}d}_gray_lt_{threshold:03d}.tiff"
        )
        image = Image.fromarray(binary)

        save_options: dict[str, object] = {"compression": "tiff_lzw"}
        if dpi:
            save_options["dpi"] = dpi
        image.save(output_path, **save_options)
        output_paths.append(output_path)

        black_pixels = int(np.count_nonzero(binary == 0))
        ratio = black_pixels / binary.size * 100
        print(
            f"[{index:>{digits}}/{layers}] 阈值 < {threshold:3d}: "
            f"黑色像素 {ratio:6.2f}% -> {output_path.name}"
        )

    return output_paths


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="将灰度图按等间隔累计阈值输出为多张黑白 TIFF。"
    )
    parser.add_argument("input", type=Path, help="输入灰度图路径")
    parser.add_argument("output_dir", type=Path, help="输出目录")
    parser.add_argument(
        "-n",
        "--layers",
        type=int,
        default=10,
        help="分层数量，默认 10",
    )
    parser.add_argument(
        "--below-is-white",
        action="store_true",
        help="将低于阈值的区域设为白色（默认设为黑色）",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    split_grayscale_layers(
        args.input,
        args.output_dir,
        args.layers,
        below_is_black=not args.below_is_white,
    )


if __name__ == "__main__":
    main()
