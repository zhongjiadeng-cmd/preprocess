#!/usr/bin/env python3
"""将单层黑白纹理裁剪/平铺到目标尺寸，并把黑区输出为 DXF 阴影线。

坐标约定：
  * DXF 单位为毫米。
  * 坐标原点位于加工区域中心，与参考 DXF 一致。
  * 左下角为 (-width/2, -height/2)，右上角为 (width/2, height/2)。
  * 黑色像素（默认灰度 < 128）是需要填充阴影线的加工区域。
"""

from __future__ import annotations

import argparse
import json
import math
import os
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import TextIO

import numpy as np
from PIL import Image


MM_PER_INCH = 25.4


@dataclass(frozen=True)
class HatchSegment:
    """一条已经归属到某个加工块的水平加工线。"""

    block_index: int
    x1: float
    y1: float
    x2: float
    y2: float


@dataclass(frozen=True)
class VoronoiBlock:
    """裁剪到加工幅面内的一个凸 Voronoi 加工块。"""

    index: int
    seed_x: float
    seed_y: float
    polygon: tuple[tuple[float, float], ...]
    area: float


def block_metadata_path(dxf_path: Path) -> Path:
    return dxf_path.with_suffix(".blocks.json")


def build_block_metadata(
    voronoi_blocks: list[VoronoiBlock],
    block_order: list[int],
    ordered_block_counts: list[int],
    border_line_count: int,
) -> dict[str, object]:
    if len(block_order) != len(ordered_block_counts):
        raise ValueError("block_order and ordered_block_counts must have equal lengths")
    return {
        "version": 1,
        "border_line_count": border_line_count,
        "blocks": [
            {
                "block_index": voronoi_blocks[index].index,
                "center_x": voronoi_blocks[index].seed_x,
                "center_y": voronoi_blocks[index].seed_y,
                "line_count": count,
            }
            for index, count in zip(block_order, ordered_block_counts)
        ],
    }


def write_block_metadata(path: Path, document: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary_path: Path | None = None
    try:
        file_descriptor, temporary_name = tempfile.mkstemp(
            prefix=f".{path.name}.",
            suffix=".tmp",
            dir=path.parent,
        )
        temporary_path = Path(temporary_name)
        with os.fdopen(
            file_descriptor,
            "w",
            encoding="utf-8",
            newline="",
        ) as stream:
            json.dump(
                document,
                stream,
                ensure_ascii=False,
                allow_nan=False,
                indent=4,
            )
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary_path, path)
        temporary_path = None
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)


def read_binary_texture(
    image_path: Path,
    black_threshold: int = 128,
    fallback_dpi: float | None = None,
) -> tuple[np.ndarray, float, float]:
    """读取纹理，返回黑区掩膜和 X/Y 方向的 mm/像素。"""
    with Image.open(image_path) as image:
        gray = np.asarray(image.convert("L"), dtype=np.uint8)
        dpi = image.info.get("dpi")

    if dpi and len(dpi) >= 2 and dpi[0] > 0 and dpi[1] > 0:
        dpi_x, dpi_y = float(dpi[0]), float(dpi[1])
    elif fallback_dpi and fallback_dpi > 0:
        dpi_x = dpi_y = float(fallback_dpi)
    else:
        raise ValueError(
            "图片没有有效 DPI 元数据；请通过 --dpi 指定分辨率。"
        )

    black_mask = gray < black_threshold
    return black_mask, MM_PER_INCH / dpi_x, MM_PER_INCH / dpi_y


def _axis_tokens(source: np.ndarray, axis: int) -> np.ndarray:
    """把每一列或每一行压缩为整数标记，用于快速检测重复周期。"""
    packed = (
        np.packbits(source, axis=0).T
        if axis == 1
        else np.packbits(source, axis=1)
    )
    _, tokens = np.unique(packed, axis=0, return_inverse=True)
    return tokens


def _detect_axis_period(
    source: np.ndarray,
    axis: int,
    *,
    min_similarity: float = 0.98,
) -> tuple[int, float]:
    """检测指定方向的最小重复周期，返回周期像素数和匹配率。"""
    tokens = _axis_tokens(source, axis)
    length = tokens.size
    if length < 4:
        raise ValueError("纹理尺寸太小，无法识别重复单元")

    similarities: list[float] = []
    for period in range(2, length // 2 + 1):
        similarities.append(float(np.mean(tokens[period:] == tokens[:-period])))

    # 优先选择完全匹配的最小周期，避免稀疏纹理在很小位移下因大量
    # 空白列/行相同而被误判为更小周期。
    for index, similarity in enumerate(similarities):
        if similarity >= 1.0 - 1e-12:
            return index + 2, similarity

    # 对含少量噪声或压缩误差的图片，接受明显的局部匹配峰值。
    for index, similarity in enumerate(similarities):
        left = similarities[index - 1] if index > 0 else -1.0
        right = similarities[index + 1] if index + 1 < len(similarities) else -1.0
        if similarity >= min_similarity and similarity >= left and similarity >= right:
            return index + 2, similarity

    direction = "横向" if axis == 1 else "纵向"
    raise ValueError(
        f"无法可靠识别{direction}最小重复周期；"
        "图片中至少需要包含两个重复单元，且重复单元应基本一致。"
    )


def _best_seam(source: np.ndarray, period: int, axis: int) -> tuple[int, float]:
    """在一个周期内寻找黑色像素最少的单元分界线。"""
    length = source.shape[axis]
    best_phase = 0
    best_score = math.inf

    for phase in range(period):
        positions = np.arange(phase, length, period)
        positions = positions[(positions > 0) & (positions < length)]
        if positions.size == 0:
            continue
        if axis == 1:
            boundary = source[:, positions] & source[:, positions - 1]
        else:
            boundary = source[positions, :] & source[positions - 1, :]
        score = float(np.mean(boundary))
        if score < best_score:
            best_phase, best_score = phase, score

    return best_phase, best_score


def detect_repeating_unit(
    source: np.ndarray,
) -> tuple[np.ndarray, int, int, int, int, float, float]:
    """识别并提取一个边界切割最少的最小矩形重复单元。"""
    period_width, similarity_x = _detect_axis_period(source, axis=1)
    period_height, similarity_y = _detect_axis_period(source, axis=0)
    start_x, seam_x = _best_seam(source, period_width, axis=1)
    start_y, seam_y = _best_seam(source, period_height, axis=0)

    if start_x + period_width > source.shape[1]:
        start_x -= period_width
    if start_y + period_height > source.shape[0]:
        start_y -= period_height
    if start_x < 0 or start_y < 0:
        raise ValueError("纹理中没有足够空间提取一个完整重复单元")

    unit = source[
        start_y : start_y + period_height,
        start_x : start_x + period_width,
    ].copy()
    return (
        unit,
        period_width,
        period_height,
        start_x,
        start_y,
        min(similarity_x, similarity_y),
        max(seam_x, seam_y),
    )


def fit_complete_units_to_size(
    unit: np.ndarray,
    target_width_px: int,
    target_height_px: int,
    *,
    crop_anchor: str = "center",
) -> tuple[np.ndarray, int, int]:
    """仅放置整数个完整重复单元，剩余目标区域保持为空白。"""
    unit_height, unit_width = unit.shape
    columns = target_width_px // unit_width
    rows = target_height_px // unit_height
    if columns < 1 or rows < 1:
        raise ValueError(
            f"目标栅格 {target_width_px}×{target_height_px} px 小于最小重复单元 "
            f"{unit_width}×{unit_height} px，无法放入一个完整加工单元。"
        )

    tiled = np.tile(unit, (rows, columns))
    fitted = np.zeros((target_height_px, target_width_px), dtype=bool)
    if crop_anchor == "center":
        start_x = (target_width_px - tiled.shape[1]) // 2
        start_y = (target_height_px - tiled.shape[0]) // 2
    elif crop_anchor == "top-left":
        start_x = start_y = 0
    else:
        raise ValueError(f"不支持的裁剪锚点：{crop_anchor}")
    fitted[
        start_y : start_y + tiled.shape[0],
        start_x : start_x + tiled.shape[1],
    ] = tiled
    return fitted, columns, rows


def fit_texture_to_size(
    source: np.ndarray,
    target_width_px: int,
    target_height_px: int,
    *,
    crop_anchor: str = "center",
    tile_mode: str = "repeat",
) -> np.ndarray:
    """平铺到足够大后裁剪为目标像素尺寸。

    这是保留给已知无缝纹理的传统重复/镜像拼接路径。
    """
    if target_width_px < 1 or target_height_px < 1:
        raise ValueError("目标像素尺寸必须大于 0")

    source_height, source_width = source.shape

    if crop_anchor == "center":
        # 小图居中裁剪；大图让完整源纹理从左上角开始周期重复。
        start_x = max((source_width - target_width_px) // 2, 0)
        start_y = max((source_height - target_height_px) // 2, 0)
    elif crop_anchor == "top-left":
        start_x = start_y = 0
    else:
        raise ValueError(f"不支持的裁剪锚点：{crop_anchor}")

    def tile_indices(length: int, target_length: int, start: int) -> np.ndarray:
        positions = start + np.arange(target_length)
        if tile_mode == "repeat":
            return positions % length
        if tile_mode == "mirror":
            if length == 1:
                return np.zeros(target_length, dtype=np.intp)
            period = 2 * length - 2
            folded = positions % period
            return np.where(folded < length, folded, period - folded)
        raise ValueError(f"不支持的拼接模式：{tile_mode}")

    x_indices = tile_indices(source_width, target_width_px, start_x)
    y_indices = tile_indices(source_height, target_height_px, start_y)
    return source[np.ix_(y_indices, x_indices)]


def black_runs(row: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """返回布尔行中连续黑区的 [start, end) 像素索引。"""
    padded = np.empty(row.size + 2, dtype=np.int8)
    padded[0] = padded[-1] = 0
    padded[1:-1] = row
    changes = np.diff(padded)
    starts = np.flatnonzero(changes == 1)
    ends = np.flatnonzero(changes == -1)
    return starts, ends


def dxf_pair(stream: TextIO, code: int, value: object) -> None:
    """使用 drawing.dxf 相同的无缩进组码和 LF 行尾。"""
    stream.write(f"{code}\n{value}\n")


def write_dxf_header(
    stream: TextIO,
    target_width_mm: float,
    target_height_mm: float,
) -> None:
    # drawing.dxf 的极简结构不包含 HEADER、TABLES 或 BLOCKS。
    # target_* 参数保留在接口中，便于以后切换输出格式。
    del target_width_mm, target_height_mm
    dxf_pair(stream, 0, "SECTION")
    dxf_pair(stream, 2, "ENTITIES")


def write_line(
    stream: TextIO,
    handle: int,
    x1: float,
    y1: float,
    x2: float,
    y2: float,
) -> None:
    del handle  # 极简 DXF 不写实体句柄
    dxf_pair(stream, 0, "LINE")
    dxf_pair(stream, 10, f"{x1:.6f}")
    dxf_pair(stream, 20, f"{y1:.6f}")
    dxf_pair(stream, 30, "0.0")
    dxf_pair(stream, 11, f"{x2:.6f}")
    dxf_pair(stream, 21, f"{y2:.6f}")
    dxf_pair(stream, 31, "0.0")


def write_border(
    stream: TextIO,
    first_handle: int,
    target_width_mm: float,
    target_height_mm: float,
) -> int:
    """写入目标加工区域边框，便于在 CAD 中检查尺寸。"""
    left, right = -target_width_mm / 2, target_width_mm / 2
    bottom, top = -target_height_mm / 2, target_height_mm / 2
    edges = (
        (left, bottom, right, bottom),
        (right, bottom, right, top),
        (right, top, left, top),
        (left, top, left, bottom),
    )
    handle = first_handle
    for x1, y1, x2, y2 in edges:
        write_line(stream, handle, x1, y1, x2, y2)
        handle += 1
    return handle


def _clip_polygon_to_half_plane(
    polygon: list[tuple[float, float]],
    a: float,
    b: float,
    c: float,
) -> list[tuple[float, float]]:
    """用 a*x+b*y<=c 裁剪凸多边形。"""
    if not polygon:
        return []

    clipped: list[tuple[float, float]] = []
    for index, point in enumerate(polygon):
        next_point = polygon[(index + 1) % len(polygon)]
        value = a * point[0] + b * point[1] - c
        next_value = a * next_point[0] + b * next_point[1] - c
        point_inside = value <= 1e-9
        next_inside = next_value <= 1e-9

        if point_inside:
            clipped.append(point)
        if point_inside != next_inside:
            ratio = value / (value - next_value)
            clipped.append(
                (
                    point[0] + (next_point[0] - point[0]) * ratio,
                    point[1] + (next_point[1] - point[1]) * ratio,
                )
            )
    return clipped


def _polygon_area_and_centroid(
    polygon: list[tuple[float, float]],
) -> tuple[float, tuple[float, float]]:
    """返回多边形面积和形心。"""
    twice_signed_area = 0.0
    centroid_x_sum = 0.0
    centroid_y_sum = 0.0
    for index, point in enumerate(polygon):
        next_point = polygon[(index + 1) % len(polygon)]
        cross = point[0] * next_point[1] - next_point[0] * point[1]
        twice_signed_area += cross
        centroid_x_sum += (point[0] + next_point[0]) * cross
        centroid_y_sum += (point[1] + next_point[1]) * cross

    if abs(twice_signed_area) <= 1e-12:
        return 0.0, polygon[0]
    centroid = (
        centroid_x_sum / (3.0 * twice_signed_area),
        centroid_y_sum / (3.0 * twice_signed_area),
    )
    return abs(twice_signed_area) / 2.0, centroid


def _voronoi_polygons(
    seeds: np.ndarray,
    width_mm: float,
    height_mm: float,
) -> list[list[tuple[float, float]]]:
    """不依赖 scipy/shapely，把每个 Voronoi 单元裁剪到加工矩形。"""
    left, right = -width_mm / 2.0, width_mm / 2.0
    bottom, top = -height_mm / 2.0, height_mm / 2.0
    rectangle = [(left, bottom), (right, bottom), (right, top), (left, top)]
    polygons: list[list[tuple[float, float]]] = []

    for seed_index, seed in enumerate(seeds):
        polygon = rectangle.copy()
        for other_index, other in enumerate(seeds):
            if other_index == seed_index:
                continue
            # 到 seed 的距离不大于到 other 的距离。
            a = float(other[0] - seed[0])
            b = float(other[1] - seed[1])
            c = float(
                (
                    other[0] * other[0]
                    + other[1] * other[1]
                    - seed[0] * seed[0]
                    - seed[1] * seed[1]
                )
                / 2.0
            )
            polygon = _clip_polygon_to_half_plane(polygon, a, b, c)
            if not polygon:
                break
        polygons.append(polygon)
    return polygons


def create_constrained_voronoi_blocks(
    width_mm: float,
    height_mm: float,
    block_count: int,
    *,
    random_seed: int = 12345,
    min_block_area_mm2: float = 0.0,
    max_block_area_mm2: float | None = None,
    lloyd_iterations: int = 8,
    attempts: int = 80,
) -> list[VoronoiBlock]:
    """生成数量固定、面积受约束且尺寸较均匀的 Voronoi 加工块。"""
    if block_count < 1:
        raise ValueError("Voronoi 加工块数量必须至少为 1")
    total_area = width_mm * height_mm
    maximum_area = total_area if max_block_area_mm2 is None else max_block_area_mm2
    if min_block_area_mm2 < 0 or maximum_area <= 0:
        raise ValueError("加工块面积约束必须大于或等于 0")
    if min_block_area_mm2 > maximum_area:
        raise ValueError("单块最小面积不能大于单块最大面积")
    if min_block_area_mm2 * block_count > total_area + 1e-6:
        raise ValueError("单块最小面积 × 块数量超过了总加工面积")
    if maximum_area * block_count < total_area - 1e-6:
        raise ValueError("单块最大面积 × 块数量小于总加工面积")

    left, right = -width_mm / 2.0, width_mm / 2.0
    bottom, top = -height_mm / 2.0, height_mm / 2.0
    best: tuple[float, np.ndarray, list[list[tuple[float, float]]], list[float]] | None = None

    for attempt in range(attempts):
        rng = np.random.default_rng(random_seed + attempt * 104729)
        seeds = np.column_stack(
            (
                rng.uniform(left, right, size=block_count),
                rng.uniform(bottom, top, size=block_count),
            )
        )

        # Lloyd 松弛把种子移向单元形心，避免极小碎块。
        for _ in range(max(0, lloyd_iterations)):
            polygons = _voronoi_polygons(seeds, width_mm, height_mm)
            centroids = []
            for seed, polygon in zip(seeds, polygons):
                if len(polygon) < 3:
                    centroids.append((float(seed[0]), float(seed[1])))
                else:
                    _, centroid = _polygon_area_and_centroid(polygon)
                    centroids.append(centroid)
            seeds = np.asarray(centroids, dtype=float)

        polygons = _voronoi_polygons(seeds, width_mm, height_mm)
        areas = [
            _polygon_area_and_centroid(polygon)[0] if len(polygon) >= 3 else 0.0
            for polygon in polygons
        ]
        penalty = sum(
            max(0.0, min_block_area_mm2 - area)
            + max(0.0, area - maximum_area)
            for area in areas
        )
        if best is None or penalty < best[0]:
            best = (penalty, seeds.copy(), polygons, areas)
        if penalty <= 1e-7:
            break

    assert best is not None
    penalty, seeds, polygons, areas = best
    if penalty > 1e-6:
        raise ValueError(
            "无法在当前随机尝试次数内满足加工块面积约束；"
            f"得到的面积范围为 {min(areas):.3f}～{max(areas):.3f} mm²，"
            "请放宽约束或增加 --voronoi-attempts。"
        )

    return [
        VoronoiBlock(
            index=index,
            seed_x=float(seeds[index, 0]),
            seed_y=float(seeds[index, 1]),
            polygon=tuple(polygons[index]),
            area=areas[index],
        )
        for index in range(block_count)
    ]


def _horizontal_polygon_interval(
    polygon: tuple[tuple[float, float], ...],
    y_mm: float,
) -> tuple[float, float] | None:
    """返回凸多边形与水平扫描线的交区间。"""
    intersections: list[float] = []
    for index, point in enumerate(polygon):
        next_point = polygon[(index + 1) % len(polygon)]
        if (point[1] <= y_mm < next_point[1]) or (
            next_point[1] <= y_mm < point[1]
        ):
            ratio = (y_mm - point[1]) / (next_point[1] - point[1])
            intersections.append(point[0] + (next_point[0] - point[0]) * ratio)
    if len(intersections) < 2:
        return None
    return min(intersections), max(intersections)


def _hash_unit_interval(value: int) -> float:
    """可复现的整数哈希，返回 [0, 1)。"""
    value &= 0xFFFFFFFFFFFFFFFF
    value ^= value >> 30
    value = (value * 0xBF58476D1CE4E5B9) & 0xFFFFFFFFFFFFFFFF
    value ^= value >> 27
    value = (value * 0x94D049BB133111EB) & 0xFFFFFFFFFFFFFFFF
    value ^= value >> 31
    return (value & ((1 << 53) - 1)) / float(1 << 53)


def _smooth_boundary_noise(
    block_a: int,
    block_b: int,
    y_mm: float,
    *,
    random_seed: int,
    correlation_mm: float,
) -> float:
    """沿 Y 连续变化的值噪声；相邻块共享相同结果。"""
    first, second = sorted((block_a, block_b))
    position = y_mm / correlation_mm
    lower = math.floor(position)
    fraction = position - lower
    smooth_fraction = fraction * fraction * (3.0 - 2.0 * fraction)

    def sample(grid_index: int) -> float:
        key = (
            random_seed * 0x9E3779B1
            + first * 0x85EBCA77
            + second * 0xC2B2AE3D
            + grid_index * 0x27D4EB2F
        )
        return _hash_unit_interval(key) * 2.0 - 1.0

    return sample(lower) * (1.0 - smooth_fraction) + sample(lower + 1) * smooth_fraction


def _fuzzy_block_intervals(
    blocks: list[VoronoiBlock],
    y_mm: float,
    left: float,
    right: float,
    *,
    blur_width_mm: float,
    blur_correlation_mm: float,
    random_seed: int,
) -> list[tuple[int, float, float]]:
    """得到某一扫描行的分块区间；内部共享边界随机移动，外框不动。"""
    intervals = []
    for block in blocks:
        interval = _horizontal_polygon_interval(block.polygon, y_mm)
        if interval is not None and interval[1] - interval[0] > 1e-9:
            intervals.append((block.index, interval[0], interval[1]))
    intervals.sort(key=lambda item: (item[1], item[2]))
    if not intervals:
        return []

    # 旋转后的加工矩形位于扫描坐标包围盒内部，首尾边界必须取当前
    # 多边形的真实交点；0° 时它们仍分别等于 left/right。
    del left, right
    base_boundaries = [intervals[0][1]]
    for current, following in zip(intervals, intervals[1:]):
        base_boundaries.append((current[2] + following[1]) / 2.0)
    base_boundaries.append(intervals[-1][2])

    fuzzy_boundaries = [base_boundaries[0]]
    for boundary_index in range(1, len(base_boundaries) - 1):
        current_block = intervals[boundary_index - 1][0]
        following_block = intervals[boundary_index][0]
        offset = blur_width_mm * _smooth_boundary_noise(
            current_block,
            following_block,
            y_mm,
            random_seed=random_seed,
            correlation_mm=blur_correlation_mm,
        )
        # 防止两个相邻模糊边界交叉并产生负宽度加工块。
        lower_limit = (base_boundaries[boundary_index - 1] + base_boundaries[boundary_index]) / 2.0
        upper_limit = (base_boundaries[boundary_index] + base_boundaries[boundary_index + 1]) / 2.0
        fuzzy_boundaries.append(
            min(max(base_boundaries[boundary_index] + offset, lower_limit), upper_limit)
        )
    fuzzy_boundaries.append(base_boundaries[-1])

    return [
        (intervals[index][0], fuzzy_boundaries[index], fuzzy_boundaries[index + 1])
        for index in range(len(intervals))
    ]


def _intersect_intervals(
    x1: float,
    x2: float,
    clip_x1: float,
    clip_x2: float,
) -> tuple[float, float] | None:
    start = max(x1, clip_x1)
    end = min(x2, clip_x2)
    return (start, end) if end - start > 1e-9 else None


def export_horizontal_hatch_dxf(
    black_mask: np.ndarray,
    output_path: Path,
    target_width_mm: float,
    target_height_mm: float,
    pixel_width_mm: float,
    pixel_height_mm: float,
    hatch_spacing_mm: float = 0.02,
    *,
    hatch_angle_deg: float = 0.0,
    include_border: bool = False,
    bidirectional: bool = False,
    voronoi_blocks: list[VoronoiBlock] | None = None,
    block_metadata_output: Path | None = None,
    boundary_blur_mm: float = 0.0,
    boundary_correlation_mm: float = 1.0,
    random_seed: int = 12345,
) -> tuple[int, list[int]]:
    """按给定角度生成 LINE；0° 为水平，可按扫描行往返填充。"""
    if hatch_spacing_mm <= 0:
        raise ValueError("阴影线间距必须大于 0")
    if isinstance(hatch_angle_deg, bool) or not isinstance(
        hatch_angle_deg, (int, float, np.integer, np.floating)
    ) or not np.isfinite(hatch_angle_deg):
        raise ValueError("填充角度必须是有限数字")
    if boundary_blur_mm < 0:
        raise ValueError("边界扩散宽度不能小于 0")
    if boundary_correlation_mm <= 0:
        raise ValueError("边界随机变化的相关长度必须大于 0")

    height_px, width_px = black_mask.shape
    output_path.parent.mkdir(parents=True, exist_ok=True)
    normalized_angle = float(hatch_angle_deg) % 180.0
    angle_radians = math.radians(normalized_angle)
    cosine = math.cos(angle_radians)
    sine = math.sin(angle_radians)
    scan_width_mm = abs(target_width_mm * cosine) + abs(target_height_mm * sine)
    scan_height_mm = abs(target_width_mm * sine) + abs(target_height_mm * cosine)
    left, right = -scan_width_mm / 2.0, scan_width_mm / 2.0
    top = scan_height_mm / 2.0

    def to_scan(x_mm: float, y_mm: float) -> tuple[float, float]:
        return (
            x_mm * cosine + y_mm * sine,
            -x_mm * sine + y_mm * cosine,
        )

    def from_scan(u_mm: float, v_mm: float) -> tuple[float, float]:
        return (
            u_mm * cosine - v_mm * sine,
            u_mm * sine + v_mm * cosine,
        )

    def scanline_frame_interval(v_mm: float) -> tuple[float, float] | None:
        """Return the exact scan-coordinate interval inside the original frame."""
        lower, upper = -math.inf, math.inf
        constraints = (
            (cosine, v_mm * sine, -target_width_mm / 2.0, target_width_mm / 2.0),
            (sine, -v_mm * cosine, -target_height_mm / 2.0, target_height_mm / 2.0),
        )
        for coefficient, translated_minimum, minimum, maximum in constraints:
            if abs(coefficient) <= 1e-12:
                fixed_value = -translated_minimum
                if fixed_value < minimum - 1e-9 or fixed_value > maximum + 1e-9:
                    return None
                continue
            first = (minimum + translated_minimum) / coefficient
            second = (maximum + translated_minimum) / coefficient
            lower = max(lower, min(first, second))
            upper = min(upper, max(first, second))
        return (lower, upper) if upper - lower > 1e-9 else None

    scan_blocks = None
    if voronoi_blocks:
        scan_blocks = [
            VoronoiBlock(
                index=block.index,
                seed_x=to_scan(block.seed_x, block.seed_y)[0],
                seed_y=to_scan(block.seed_x, block.seed_y)[1],
                polygon=tuple(to_scan(x, y) for x, y in block.polygon),
                area=block.area,
            )
            for block in voronoi_blocks
        ]
    block_count = len(voronoi_blocks) if voronoi_blocks else 1
    grouped_segments: list[list[HatchSegment]] = [[] for _ in range(block_count)]

    # 从上向下生成，之后仍显式排序，保证 DXF 实体顺序稳定。
    hatch_count = math.floor(scan_height_mm / hatch_spacing_mm)
    for index_from_top in range(hatch_count):
        y_from_top_mm = (index_from_top + 0.5) * hatch_spacing_mm
        if y_from_top_mm >= scan_height_mm:
            break
        y_mm = top - y_from_top_mm

        if normalized_angle == 0.0:
            image_row = min(int(y_from_top_mm / pixel_height_mm), height_px - 1)
            starts, ends = black_runs(black_mask[image_row])
            source_intervals = [
                (
                    min(float(start_px) * pixel_width_mm, target_width_mm) + left,
                    min(float(end_px) * pixel_width_mm, target_width_mm) + left,
                )
                for start_px, end_px in zip(starts, ends)
            ]
        else:
            sample_step_mm = min(pixel_width_mm, pixel_height_mm)
            sample_count = max(1, math.ceil(scan_width_mm / sample_step_mm))
            actual_sample_width = scan_width_mm / sample_count
            u_centers = left + (np.arange(sample_count) + 0.5) * actual_sample_width
            x_values = u_centers * cosine - y_mm * sine
            y_values = u_centers * sine + y_mm * cosine
            columns = np.floor((x_values + target_width_mm / 2.0) / pixel_width_mm).astype(np.int64)
            rows = np.floor((target_height_mm / 2.0 - y_values) / pixel_height_mm).astype(np.int64)
            inside = (
                (columns >= 0) & (columns < width_px) &
                (rows >= 0) & (rows < height_px)
            )
            sampled_row = np.zeros(sample_count, dtype=bool)
            sampled_row[inside] = black_mask[rows[inside], columns[inside]]
            starts, ends = black_runs(sampled_row)
            source_intervals = [
                (left + start * actual_sample_width, left + end * actual_sample_width)
                for start, end in zip(starts, ends)
            ]

        frame_interval = scanline_frame_interval(y_mm)
        if frame_interval is None:
            continue
        source_intervals = [
            clipped
            for source_x1, source_x2 in source_intervals
            if (clipped := _intersect_intervals(
                source_x1,
                source_x2,
                frame_interval[0],
                frame_interval[1],
            )) is not None
        ]

        if scan_blocks:
            block_intervals = _fuzzy_block_intervals(
                scan_blocks,
                y_mm,
                left,
                right,
                blur_width_mm=boundary_blur_mm,
                blur_correlation_mm=boundary_correlation_mm,
                random_seed=random_seed,
            )
        else:
            block_intervals = [(0, left, right)]

        for block_index, block_x1, block_x2 in block_intervals:
            for source_x1, source_x2 in source_intervals:
                clipped = _intersect_intervals(
                    source_x1,
                    source_x2,
                    block_x1,
                    block_x2,
                )
                if clipped is not None:
                    segment_x1, segment_x2 = clipped
                    if bidirectional and index_from_top % 2 == 1:
                        segment_x1, segment_x2 = segment_x2, segment_x1
                    output_x1, output_y1 = from_scan(segment_x1, y_mm)
                    output_x2, output_y2 = from_scan(segment_x2, y_mm)
                    grouped_segments[block_index].append(
                        HatchSegment(
                            block_index,
                            output_x1,
                            output_y1,
                            output_x2,
                            output_y2,
                        )
                    )

    if scan_blocks:
        # 块的总体顺序按种子点从上到下、从左到右。
        block_order = sorted(
            range(block_count),
            key=lambda block_index: (
                -scan_blocks[block_index].seed_y,
                scan_blocks[block_index].seed_x,
            ),
        )
    else:
        block_order = [0]

    def segment_sort_key(segment: HatchSegment) -> tuple[float, float, float]:
        first_u, first_v = to_scan(segment.x1, segment.y1)
        second_u, _ = to_scan(segment.x2, segment.y2)
        row_index = round((top - first_v) / hatch_spacing_mm - 0.5)
        left_x = min(first_u, second_u)
        right_x = max(first_u, second_u)
        if bidirectional and row_index % 2 == 1:
            return (-first_v, -right_x, -left_x)
        return (-first_v, left_x, right_x)

    for segments in grouped_segments:
        segments.sort(key=segment_sort_key)

    line_count = sum(len(segments) for segments in grouped_segments)
    ordered_block_counts: list[int] = []
    with output_path.open("w", encoding="ascii", newline="") as stream:
        write_dxf_header(stream, target_width_mm, target_height_mm)
        handle = 0x100

        if include_border:
            handle = write_border(
                stream,
                handle,
                target_width_mm,
                target_height_mm,
            )

        for block_index in block_order:
            ordered_block_counts.append(len(grouped_segments[block_index]))
            for segment in grouped_segments[block_index]:
                write_line(
                    stream,
                    handle,
                    segment.x1,
                    segment.y1,
                    segment.x2,
                    segment.y2,
                )
                handle += 1

        dxf_pair(stream, 0, "ENDSEC")
        dxf_pair(stream, 0, "EOF")

    if block_metadata_output is not None and scan_blocks:
        write_block_metadata(
            block_metadata_output,
            build_block_metadata(
                voronoi_blocks,
                block_order,
                ordered_block_counts,
                4 if include_border else 0,
            ),
        )

    return line_count, ordered_block_counts


def convert_texture_to_dxf(
    input_path: Path,
    output_path: Path,
    target_width_mm: float,
    target_height_mm: float,
    hatch_spacing_mm: float = 0.02,
    *,
    hatch_angle_deg: float = 0.0,
    black_threshold: int = 128,
    fallback_dpi: float | None = None,
    crop_anchor: str = "center",
    tile_mode: str = "unit",
    include_border: bool = False,
    bidirectional: bool = False,
    voronoi_block_count: int = 0,
    min_block_area_mm2: float = 0.0,
    max_block_area_mm2: float | None = None,
    boundary_blur_mm: float = 0.0,
    boundary_correlation_mm: float = 1.0,
    random_seed: int = 12345,
    voronoi_lloyd_iterations: int = 8,
    voronoi_attempts: int = 80,
) -> None:
    if target_width_mm <= 0 or target_height_mm <= 0:
        raise ValueError("目标毫米尺寸必须大于 0")

    source, pixel_width_mm, pixel_height_mm = read_binary_texture(
        input_path,
        black_threshold=black_threshold,
        fallback_dpi=fallback_dpi,
    )

    target_width_px = max(1, round(target_width_mm / pixel_width_mm))
    target_height_px = max(1, round(target_height_mm / pixel_height_mm))
    repeat_info: tuple[int, int, int, int, float, float, int, int] | None = None
    if tile_mode == "unit":
        (
            unit,
            period_width,
            period_height,
            unit_x,
            unit_y,
            repeat_similarity,
            seam_score,
        ) = detect_repeating_unit(source)
        fitted, unit_columns, unit_rows = fit_complete_units_to_size(
            unit,
            target_width_px,
            target_height_px,
            crop_anchor=crop_anchor,
        )
        repeat_info = (
            period_width,
            period_height,
            unit_x,
            unit_y,
            repeat_similarity,
            seam_score,
            unit_columns,
            unit_rows,
        )
    else:
        fitted = fit_texture_to_size(
            source,
            target_width_px,
            target_height_px,
            crop_anchor=crop_anchor,
            tile_mode=tile_mode,
        )

    voronoi_blocks = (
        create_constrained_voronoi_blocks(
            target_width_mm,
            target_height_mm,
            voronoi_block_count,
            random_seed=random_seed,
            min_block_area_mm2=min_block_area_mm2,
            max_block_area_mm2=max_block_area_mm2,
            lloyd_iterations=voronoi_lloyd_iterations,
            attempts=voronoi_attempts,
        )
        if voronoi_block_count > 0
        else None
    )
    metadata_output = block_metadata_path(output_path) if voronoi_blocks else None

    line_count, block_line_counts = export_horizontal_hatch_dxf(
        fitted,
        output_path,
        target_width_mm,
        target_height_mm,
        pixel_width_mm,
        pixel_height_mm,
        hatch_spacing_mm,
        hatch_angle_deg=hatch_angle_deg,
        include_border=include_border,
        bidirectional=bidirectional,
        voronoi_blocks=voronoi_blocks,
        block_metadata_output=metadata_output,
        boundary_blur_mm=boundary_blur_mm,
        boundary_correlation_mm=boundary_correlation_mm,
        random_seed=random_seed,
    )

    print(
        "处理方式: 自动识别最小重复单元"
        if tile_mode == "unit"
        else "传统纹理拼接"
    )
    print(f"像素尺寸: {pixel_width_mm:.8f} × {pixel_height_mm:.8f} mm")
    print(f"目标栅格: {target_width_px} × {target_height_px} px")
    print(f"目标尺寸: {target_width_mm:g} × {target_height_mm:g} mm")
    mode_names = {
        "unit": "完整重复单元",
        "repeat": "周期重复",
        "mirror": "镜像拼接",
    }
    print(f"拼接模式: {mode_names[tile_mode]}")
    if repeat_info is not None:
        (
            period_width,
            period_height,
            unit_x,
            unit_y,
            repeat_similarity,
            seam_score,
            unit_columns,
            unit_rows,
        ) = repeat_info
        print(f"识别周期: {period_width} × {period_height} px")
        print(f"单元起点: ({unit_x}, {unit_y}) px")
        print(f"周期匹配率: {repeat_similarity * 100:.2f}%")
        print(f"边界黑色占比: {seam_score * 100:.2f}%")
        print(f"完整单元阵列: {unit_columns} 列 × {unit_rows} 行")
        if seam_score > 0.05:
            print("警告: 未找到完全空白的单元边界，已使用黑色像素最少的边界。")
    print(f"阴影线间距: {hatch_spacing_mm:g} mm")
    print(f"填充角度: {float(hatch_angle_deg) % 180:g}°")
    print(f"填充方向: {'往返交替' if bidirectional else '单向左到右'}")
    if voronoi_blocks:
        areas = [block.area for block in voronoi_blocks]
        print(f"Voronoi 加工块: {len(voronoi_blocks)}")
        print(f"加工块面积范围: {min(areas):.3f}～{max(areas):.3f} mm²")
        print(f"内部边界扩散宽度: ±{boundary_blur_mm:g} mm")
        print(f"边界随机相关长度: {boundary_correlation_mm:g} mm")
        print(f"随机种子: {random_seed}")
        print(
            "各块 LINE 数量（按输出顺序）: "
            + ", ".join(str(count) for count in block_line_counts)
        )
        print(f"块元数据: {metadata_output}")
    else:
        print("Voronoi 分块: 关闭")
    print(f"DXF LINE 数量: {line_count}")
    print(f"输出文件: {output_path}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="裁剪/拼接单层黑白纹理，并将黑区转换为 DXF 水平阴影线。"
    )
    parser.add_argument("input", type=Path, help="输入单层 TIFF/PNG")
    parser.add_argument("output", type=Path, help="输出 DXF")
    parser.add_argument(
        "--size",
        type=float,
        help="正方形边长（mm）；可替代 --width 和 --height",
    )
    parser.add_argument("--width", type=float, help="目标宽度（mm）")
    parser.add_argument("--height", type=float, help="目标高度（mm）")
    parser.add_argument(
        "--spacing",
        type=float,
        default=0.02,
        help="阴影线间距（mm），默认 0.02",
    )
    parser.add_argument(
        "--angle",
        type=float,
        default=0.0,
        help="填充线相对水平方向的角度（度），按 180° 循环，默认 0",
    )
    parser.add_argument(
        "--threshold",
        type=int,
        default=128,
        help="小于此灰度值的像素视为黑区，默认 128",
    )
    parser.add_argument(
        "--dpi",
        type=float,
        help="图片没有 DPI 元数据时使用的 DPI",
    )
    parser.add_argument(
        "--anchor",
        choices=("center", "top-left"),
        default="center",
        help="裁剪基准，默认 center",
    )
    parser.add_argument(
        "--tile-mode",
        choices=("unit", "repeat", "mirror"),
        default="unit",
        help="拼接方式；unit 自动识别最小重复单元并只输出完整单元，默认 unit",
    )
    parser.add_argument(
        "--border",
        action="store_true",
        help="额外写入加工区域边框（默认不写，避免成为加工线）",
    )
    parser.add_argument(
        "--bidirectional",
        action="store_true",
        help="相邻阴影线交替使用左到右、右到左方向，形成往返填充",
    )
    parser.add_argument(
        "--blocks",
        type=int,
        default=9,
        help="Voronoi 加工块数量；0 表示关闭分块，默认 9",
    )
    parser.add_argument(
        "--min-block-area",
        type=float,
        help="单个 Voronoi 加工块的最小面积（mm²），默认总面积的 5%%",
    )
    parser.add_argument(
        "--max-block-area",
        type=float,
        help="单个 Voronoi 加工块的最大面积（mm²），默认总面积的 18%%",
    )
    parser.add_argument(
        "--boundary-blur",
        type=float,
        default=3.0,
        help="内部共享边界向两侧随机扩散的最大宽度（mm），默认 3",
    )
    parser.add_argument(
        "--boundary-correlation",
        type=float,
        default=1.0,
        help="模糊边界沿扫描方向连续变化的相关长度（mm），默认 1",
    )
    parser.add_argument(
        "--seed",
        type=int,
        default=12345,
        help="Voronoi 和模糊边界随机种子，默认 12345",
    )
    parser.add_argument(
        "--voronoi-lloyd-iterations",
        type=int,
        default=8,
        help="使块面积更均匀的 Lloyd 松弛次数，默认 8",
    )
    parser.add_argument(
        "--voronoi-attempts",
        type=int,
        default=80,
        help="满足块面积约束时最多尝试的随机布局数量，默认 80",
    )
    args = parser.parse_args()

    if args.size is not None:
        args.width = args.height = args.size
    if args.width is None or args.height is None:
        parser.error("请使用 --size，或同时提供 --width 和 --height")
    if args.blocks < 0:
        parser.error("--blocks 不能小于 0")
    if args.blocks == 1:
        parser.error("--blocks 应为 0（关闭）或至少为 2")
    if args.boundary_blur > 0 and args.blocks == 0:
        args.boundary_blur = 0.0
    if args.blocks > 0:
        total_area = args.width * args.height
        if args.min_block_area is None:
            args.min_block_area = total_area * 0.05
        if args.max_block_area is None:
            args.max_block_area = total_area * 0.18
    else:
        args.min_block_area = 0.0
        args.max_block_area = None
    return args


def main() -> None:
    args = parse_args()
    convert_texture_to_dxf(
        args.input,
        args.output,
        args.width,
        args.height,
        args.spacing,
        hatch_angle_deg=args.angle,
        black_threshold=args.threshold,
        fallback_dpi=args.dpi,
        crop_anchor=args.anchor,
        tile_mode=args.tile_mode,
        include_border=args.border,
        bidirectional=args.bidirectional,
        voronoi_block_count=args.blocks,
        min_block_area_mm2=args.min_block_area,
        max_block_area_mm2=args.max_block_area,
        boundary_blur_mm=args.boundary_blur,
        boundary_correlation_mm=args.boundary_correlation,
        random_seed=args.seed,
        voronoi_lloyd_iterations=args.voronoi_lloyd_iterations,
        voronoi_attempts=args.voronoi_attempts,
    )


if __name__ == "__main__":
    main()
