from pathlib import Path
import tempfile
import unittest

import numpy as np
from PIL import Image

import grayscale_layers as gl
from grayscale_layers import make_thresholds, split_grayscale_layers, validate_gray_level_range


def write_gray(path: Path, values: np.ndarray) -> None:
    Image.fromarray(values.astype(np.uint8)).save(path)


class MakeThresholdsTests(unittest.TestCase):
    def test_default_matches_legacy_sequence(self):
        self.assertEqual(
            make_thresholds(10),
            [255, 230, 204, 178, 153, 128, 102, 76, 51, 26],
        )

    def test_single_layer_uses_upper_bound(self):
        self.assertEqual(make_thresholds(1), [255])
        self.assertEqual(make_thresholds(1, 100, 200), [200])

    def test_thresholds_stay_inside_requested_range(self):
        thresholds = make_thresholds(5, 100, 200)
        self.assertEqual(thresholds, [200, 180, 160, 140, 120])
        self.assertTrue(all(100 < value <= 200 for value in thresholds))
        self.assertEqual(len(set(thresholds)), 5)

    def test_range_equals_layer_count_uses_every_level(self):
        self.assertEqual(make_thresholds(5, 100, 105), [105, 104, 103, 102, 101])

    def test_thresholds_are_descending(self):
        thresholds = make_thresholds(32, 40, 240)
        self.assertTrue(all(a > b for a, b in zip(thresholds, thresholds[1:])))

    def test_rejects_invalid_combinations(self):
        with self.assertRaises(ValueError):
            make_thresholds(0)
        with self.assertRaises(ValueError):
            make_thresholds(256)
        with self.assertRaises(ValueError):
            make_thresholds(4, 120, 120)
        with self.assertRaises(ValueError):
            make_thresholds(4, 200, 100)
        with self.assertRaises(ValueError):
            make_thresholds(6, 100, 105)
        with self.assertRaises(ValueError):
            make_thresholds(4, -1, 200)
        with self.assertRaises(ValueError):
            make_thresholds(4, 100, 256)


class ValidateGrayLevelRangeTests(unittest.TestCase):
    def test_accepts_valid_range(self):
        self.assertIsNone(validate_gray_level_range(0, 255, 255))

    def test_rejects_range_narrower_than_layers(self):
        with self.assertRaisesRegex(ValueError, "不足以分成 5 层"):
            validate_gray_level_range(100, 104, 5)


class SplitGrayscaleLayersTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.tmp = Path(self._tmp.name)
        self.addCleanup(self._tmp.cleanup)
        # 每列一个灰阶值：50 / 120 / 150 / 200 / 250
        self.values = np.array([[50, 120, 150, 200, 250]], dtype=np.uint8)
        self.source = self.tmp / "ramp.png"
        write_gray(self.source, self.values)

    def read_layers(self, directory: Path):
        layers = []
        for path in sorted(directory.glob("layer_*.tiff")):
            with Image.open(path) as image:
                layers.append(np.asarray(image.convert("L"), dtype=np.uint8)[0].tolist())
        return layers

    def test_pixels_below_lower_bound_are_always_black(self):
        out = self.tmp / "out"
        split_grayscale_layers(self.source, out, 5, min_level=100, max_level=200)
        layers = self.read_layers(out)
        self.assertEqual(len(layers), 5)
        for row in layers:
            self.assertEqual(row[0], 0, "灰阶 50 低于下限，应每层都是黑色")

    def test_pixels_at_or_above_upper_bound_are_always_white(self):
        out = self.tmp / "out"
        split_grayscale_layers(self.source, out, 5, min_level=100, max_level=200)
        for row in self.read_layers(out):
            self.assertEqual(row[4], 255, "灰阶 250 高于上限，应每层都是白色")
            self.assertEqual(row[3], 255, "灰阶 200 等于上限，应每层都是白色")

    def test_intermediate_pixel_follows_threshold_order(self):
        out = self.tmp / "out"
        split_grayscale_layers(self.source, out, 5, min_level=100, max_level=200)
        # 灰阶 120：只在最后一层（阈值 120）变白，其余层为黑
        column = [row[1] for row in self.read_layers(out)]
        self.assertEqual(column, [0, 0, 0, 0, 255])

    def test_default_range_matches_legacy_behaviour(self):
        out = self.tmp / "out"
        split_grayscale_layers(self.source, out, 5)
        column = [row[2] for row in self.read_layers(out)]  # 灰阶 150
        self.assertEqual(column, [0, 0, 0, 255, 255])

    def test_below_is_white_inverts_pixels(self):
        out = self.tmp / "out"
        split_grayscale_layers(self.source, out, 5, below_is_black=False)
        column = [row[0] for row in self.read_layers(out)]
        self.assertEqual(column, [255, 255, 255, 255, 255])

    def test_below_is_white_reverses_layer_order(self):
        out = self.tmp / "out"
        paths = split_grayscale_layers(
            self.source, out, 5, below_is_black=False, min_level=100, max_level=200
        )
        self.assertEqual(
            [path.name for path in paths],
            [
                "layer_1_gray_lt_120.tiff",
                "layer_2_gray_lt_140.tiff",
                "layer_3_gray_lt_160.tiff",
                "layer_4_gray_lt_180.tiff",
                "layer_5_gray_lt_200.tiff",
            ],
        )
        # 灰阶 120：第一层阈值 120，不小于阈值 → 黑色；后续阈值更高 → 白色
        column = [row[1] for row in self.read_layers(out)]
        self.assertEqual(column, [0, 255, 255, 255, 255])

    def test_output_names_encode_threshold(self):
        out = self.tmp / "out"
        paths = split_grayscale_layers(self.source, out, 5, min_level=100, max_level=200)
        self.assertEqual(
            [path.name for path in paths],
            [
                "layer_1_gray_lt_200.tiff",
                "layer_2_gray_lt_180.tiff",
                "layer_3_gray_lt_160.tiff",
                "layer_4_gray_lt_140.tiff",
                "layer_5_gray_lt_120.tiff",
            ],
        )

    def test_invalid_range_is_rejected_before_writing(self):
        out = self.tmp / "out"
        with self.assertRaises(ValueError):
            split_grayscale_layers(self.source, out, 5, min_level=200, max_level=100)
        self.assertFalse(out.exists())


if __name__ == "__main__":
    unittest.main()
