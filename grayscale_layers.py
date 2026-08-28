#!/usr/bin/env python3
"""将灰度纹理图按累计阈值输出为多张黑白二值图。

可以指定灰阶下限与上限，让分层只在关心的灰阶区间内进行：
区间以下的像素在每一层都是黑色（必定被加工），区间以上的像素
在每一层都是白色（永远不加工）。
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image

MIN_GRAY_LEVEL = 0
MAX_GRAY_LEVEL = 255


def make_thresholds(
    layers: int,
    low: int = MIN_GRAY_LEVEL,
    high: int = MAX_GRAY_LEVEL,
) -> list[int]:
    """在 [low, high] 区间内生成从 high 递减的等间隔累计阈值。

    阈值按 (high - low) / layers 的步长下降，最低的一层停在 low 之上一步，
    因此 low 以下的灰阶在所有分层里都是黑色，high 及以上恒为白色。

    例如 layers=10、low=0、high=255 时返回：
    [255, 230, 204, 178, 153, 128, 102, 76, 51, 26]
    """
    if layers < 1 or layers > 255:
        raise ValueError("layers 必须在 1 到 255 之间")
    validate_gray_level_range(low, high, layers)

    values = np.linspace(high, low + (high - low) / layers, layers)
    return np.rint(values).astype(np.uint8).tolist()


def validate_gray_level_range(low: int, high: int, layers: int) -> None:
    """校验灰阶区间是否可用；不合法时抛出 ValueError。"""
    if not MIN_GRAY_LEVEL <= low <= MAX_GRAY_LEVEL:
        raise ValueError(
            f"灰阶下限必须在 {MIN_GRAY_LEVEL}–{MAX_GRAY_LEVEL - 1} 之间，当前为 {low}"
        )
    if not MIN_GRAY_LEVEL + 1 <= high <= MAX_GRAY_LEVEL:
        raise ValueError(
            f"灰阶上限必须在 {MIN_GRAY_LEVEL + 1}–{MAX_GRAY_LEVEL} 之间，当前为 {high}"
        )
    if low >= high:
        raise ValueError(f"灰阶上限必须大于下限，当前为 [{low}, {high}]")
    if high - low < layers:
        raise ValueError(
            f"灰阶区间 [{low}, {high}] 只有 {high - low} 个灰阶，不足以分成 {layers} 层；"
            f"请减少分层数量（最多 {high - low} 层）或放宽灰阶范围。"
        )


def split_grayscale_layers(
    input_path: Path,
    output_dir: Path,
    layers: int = 10,
    *,
    below_is_black: bool = True,
    min_level: int = MIN_GRAY_LEVEL,
    max_level: int = MAX_GRAY_LEVEL,
) -> list[Path]:
    """生成累计阈值二值图并返回输出文件路径。

    min_level / max_level 指定参与分层的灰阶区间（下限含、上限含）。

    默认（below_is_black=True）时阈值从上限递减到下限，层号 1 对应最大黑色
    区域；勾选"低于阈值设为白色"（below_is_black=False）时，层号 1 改为对应
    最小黑色区域，即阈值从下限递增到上限，使加工顺序与层号一致。
    """
    validate_gray_level_range(min_level, max_level, layers)
    output_dir.mkdir(parents=True, exist_ok=True)

    with Image.open(input_path) as source:
        gray = np.asarray(source.convert("L"), dtype=np.uint8)
        dpi = source.info.get("dpi")

    thresholds = make_thresholds(layers, min_level, max_level)
    if not below_is_black:
        thresholds.reverse()
    digits = len(str(layers))
    output_paths: list[Path] = []
    print(
        f"灰阶区间：[{min_level}, {max_level}]，"
        f"分层数量：{layers}，阈值步长：{(max_level - min_level) / layers:.2f}"
    )

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
    parser.add_argument(
        "--min-level",
        type=int,
        default=MIN_GRAY_LEVEL,
        help=f"灰阶下限，{MIN_GRAY_LEVEL}–{MAX_GRAY_LEVEL - 1}，默认 {MIN_GRAY_LEVEL}；"
        "低于该值的像素在每一层都是黑色",
    )
    parser.add_argument(
        "--max-level",
        type=int,
        default=MAX_GRAY_LEVEL,
        help=f"灰阶上限，{MIN_GRAY_LEVEL + 1}–{MAX_GRAY_LEVEL}，默认 {MAX_GRAY_LEVEL}；"
        "高于该值的像素在每一层都是白色",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    split_grayscale_layers(
        args.input,
        args.output_dir,
        args.layers,
        below_is_black=not args.below_is_white,
        min_level=args.min_level,
        max_level=args.max_level,
    )


if __name__ == "__main__":
    main()
