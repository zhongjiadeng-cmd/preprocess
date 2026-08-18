from pathlib import Path
import math
import tempfile
import unittest

import numpy as np

from texture_to_hatch_dxf import (
    create_constrained_voronoi_blocks,
    export_horizontal_hatch_dxf,
)


def read_line_coordinates(path: Path) -> list[tuple[float, float, float, float]]:
    pairs = path.read_text(encoding="ascii").splitlines()
    entities: list[tuple[float, float, float, float]] = []
    index = 0
    while index + 1 < len(pairs):
        if pairs[index] == "0" and pairs[index + 1] == "LINE":
            values: dict[str, float] = {}
            index += 2
            while index + 1 < len(pairs) and pairs[index] != "0":
                if pairs[index] in {"10", "20", "11", "21"}:
                    values[pairs[index]] = float(pairs[index + 1])
                index += 2
            entities.append((values["10"], values["20"], values["11"], values["21"]))
            continue
        index += 2
    return entities


class AngledHatchTests(unittest.TestCase):
    def test_angled_hatch_remains_compatible_with_voronoi_blocks(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "blocked.dxf"
            blocks = create_constrained_voronoi_blocks(
                10,
                10,
                3,
                random_seed=7,
                min_block_area_mm2=0,
                max_block_area_mm2=100,
            )
            line_count, block_counts = export_horizontal_hatch_dxf(
                np.ones((20, 20), dtype=bool),
                output,
                10,
                10,
                0.5,
                0.5,
                1,
                hatch_angle_deg=30,
                voronoi_blocks=blocks,
                boundary_blur_mm=0.2,
            )

            self.assertEqual(sum(block_counts), line_count)
            self.assertEqual(len(block_counts), 3)
            self.assertTrue(all(count > 0 for count in block_counts))

    def test_thirty_degree_hatch_has_requested_direction_and_exact_bounds(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "thirty.dxf"
            export_horizontal_hatch_dxf(
                np.ones((20, 20), dtype=bool),
                output,
                10,
                10,
                0.5,
                0.5,
                1,
                hatch_angle_deg=30,
            )

            lines = read_line_coordinates(output)
            self.assertGreater(len(lines), 0)
            for x1, y1, x2, y2 in lines:
                observed_angle = math.degrees(math.atan2(y2 - y1, x2 - x1)) % 180
                self.assertAlmostEqual(observed_angle, 30, places=4)
                for coordinate in (x1, y1, x2, y2):
                    self.assertGreaterEqual(coordinate, -5.0)
                    self.assertLessEqual(coordinate, 5.0)

    def test_ninety_degree_hatch_is_vertical_and_stays_inside_mask(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "vertical.dxf"
            line_count, _ = export_horizontal_hatch_dxf(
                np.ones((4, 4), dtype=bool),
                output,
                4,
                4,
                1,
                1,
                1,
                hatch_angle_deg=90,
            )

            lines = read_line_coordinates(output)
            self.assertEqual(line_count, 4)
            self.assertEqual(len(lines), 4)
            for x1, y1, x2, y2 in lines:
                self.assertAlmostEqual(x1, x2, places=6)
                self.assertGreaterEqual(min(x1, x2, y1, y2), -2.0)
                self.assertLessEqual(max(x1, x2, y1, y2), 2.0)

    def test_one_hundred_eighty_degrees_is_equivalent_to_horizontal(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            mask = np.array([[True, True], [False, True]], dtype=bool)
            first = root / "zero.dxf"
            second = root / "one-eighty.dxf"
            export_horizontal_hatch_dxf(mask, first, 2, 2, 1, 1, 1, hatch_angle_deg=0)
            export_horizontal_hatch_dxf(mask, second, 2, 2, 1, 1, 1, hatch_angle_deg=180)
            self.assertEqual(first.read_bytes(), second.read_bytes())

    def test_rejects_non_finite_angle(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            for angle in (math.nan, math.inf, -math.inf):
                with self.subTest(angle=angle), self.assertRaises(ValueError):
                    export_horizontal_hatch_dxf(
                        np.ones((1, 1), dtype=bool),
                        Path(directory) / "bad.dxf",
                        1,
                        1,
                        1,
                        1,
                        hatch_angle_deg=angle,
                    )


if __name__ == "__main__":
    unittest.main()
