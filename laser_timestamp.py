"""Deterministic seven-segment timestamp glyphs with horizontal hatch fill."""

from __future__ import annotations

import math
import re

import numpy as np


_DIGIT_SEGMENTS = {
    "0": "ab cdef".replace(" ", ""),
    "1": "bc",
    "2": "abdeg",
    "3": "abcdg",
    "4": "bcfg",
    "5": "acdfg",
    "6": "acdefg",
    "7": "abc",
    "8": "abcdefg",
    "9": "abcdfg",
}
_SEGMENT_RECTS = {
    "a": (0.18, 0.00, 0.82, 0.16),
    "b": (0.80, 0.10, 0.96, 0.86),
    "c": (0.80, 0.94, 0.96, 1.70),
    "d": (0.18, 1.64, 0.82, 1.80),
    "e": (0.04, 0.94, 0.20, 1.70),
    "f": (0.04, 0.10, 0.20, 0.86),
    "g": (0.18, 0.82, 0.82, 0.98),
}
_GLYPH_HEIGHT = 1.8
_GLYPH_ADVANCE = 1.18


def _positive_finite(value: object, label: str) -> float:
    if type(value) not in (int, float) or not math.isfinite(float(value)) or float(value) <= 0:
        raise ValueError(f"{label} must be a finite positive number")
    return float(value)


def _merge_intervals(intervals: list[tuple[float, float]]) -> list[tuple[float, float]]:
    merged: list[tuple[float, float]] = []
    for start, end in sorted(intervals):
        if not merged or start > merged[-1][1]:
            merged.append((start, end))
        else:
            merged[-1] = (merged[-1][0], max(merged[-1][1], end))
    return merged


def generate_timestamp_patch(
    text: str,
    width: float,
    height: float,
    hatch_spacing: float,
) -> np.ndarray:
    """Return controller-compatible ``<f4`` line segments in local machine coordinates."""
    if type(text) is not str or re.fullmatch(r"[0-9]{8}", text) is None:
        raise ValueError("timestamp text must contain exactly eight ASCII digits")
    width = _positive_finite(width, "timestamp width")
    height = _positive_finite(height, "timestamp height")
    hatch_spacing = _positive_finite(hatch_spacing, "timestamp hatch spacing")
    total_units = 1 + _GLYPH_ADVANCE * (len(text) - 1)
    x_scale = width / total_units
    y_scale = height / _GLYPH_HEIGHT
    lines: list[tuple[float, float, float, float, float, float]] = []
    preview_y = hatch_spacing / 2
    while preview_y < height:
        unit_y = preview_y / y_scale
        intervals: list[tuple[float, float]] = []
        for index, digit in enumerate(text):
            offset = index * _GLYPH_ADVANCE
            for segment in _DIGIT_SEGMENTS[digit]:
                left, top, right, bottom = _SEGMENT_RECTS[segment]
                if top <= unit_y < bottom:
                    intervals.append(((offset + left) * x_scale, (offset + right) * x_scale))
        for start, end in _merge_intervals(intervals):
            if end > start:
                machine_y = -preview_y
                lines.append((start, machine_y, 0.0, end, machine_y, 0.0))
        preview_y += hatch_spacing
    if not lines:
        raise ValueError("timestamp hatch spacing produces no machining lines")
    patch = np.asarray(lines, dtype="<f4")
    if patch.ndim != 2 or patch.shape[1] != 6 or not np.isfinite(patch).all():
        raise ValueError("timestamp patch is invalid")
    return patch
