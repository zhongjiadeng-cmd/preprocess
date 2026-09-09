from __future__ import annotations

import numpy as np
import pytest

import laser_timestamp


def test_generates_deterministic_horizontal_fill_patch() -> None:
    first = laser_timestamp.generate_timestamp_patch("08310712", 24, 5, 0.2)
    second = laser_timestamp.generate_timestamp_patch("08310712", 24, 5, 0.2)

    assert first.dtype.str == "<f4"
    assert first.ndim == 2 and first.shape[1] == 6 and first.shape[0] > 0
    assert np.array_equal(first, second)
    assert np.all(first[:, 0] < first[:, 3])
    assert np.array_equal(first[:, 1], first[:, 4])
    assert np.all(first[:, 1] <= 0)
    assert np.all(first[:, [2, 5]] == 0)
    assert float(first[:, [0, 3]].min()) >= 0
    assert float(first[:, [0, 3]].max()) <= 24
    assert float(first[:, [1, 4]].min()) >= -5


def test_zero_contains_separate_left_and_right_fill_intervals() -> None:
    patch = laser_timestamp.generate_timestamp_patch("00000000", 24, 6, 0.15)
    rows: dict[float, int] = {}
    for line in patch:
        rows[round(float(line[1]), 4)] = rows.get(round(float(line[1]), 4), 0) + 1

    assert max(rows.values()) >= 16


def test_scaling_and_spacing_change_physical_output() -> None:
    compact = laser_timestamp.generate_timestamp_patch("12345678", 16, 4, 0.2)
    large = laser_timestamp.generate_timestamp_patch("12345678", 32, 8, 0.2)
    sparse = laser_timestamp.generate_timestamp_patch("12345678", 32, 8, 0.4)

    assert float(large[:, [0, 3]].max()) > float(compact[:, [0, 3]].max())
    assert float(large[:, [1, 4]].min()) < float(compact[:, [1, 4]].min())
    assert sparse.shape[0] < large.shape[0]


@pytest.mark.parametrize(
    ("text", "width", "height", "spacing"),
    (("123", 10, 3, 0.1), ("1234567x", 10, 3, 0.1), ("12345678", 0, 3, 0.1),
     ("12345678", 10, -1, 0.1), ("12345678", 10, 3, 0)),
)
def test_rejects_invalid_timestamp_geometry(
    text: str, width: float, height: float, spacing: float
) -> None:
    with pytest.raises(ValueError):
        laser_timestamp.generate_timestamp_patch(text, width, height, spacing)
