from pathlib import Path
from contextlib import redirect_stdout
import io
import json
import math
import os
import tempfile
import unittest
from unittest import mock

import numpy as np
from PIL import Image

import texture_to_hatch_dxf as hatch
from texture_to_hatch_dxf import (
    VoronoiBlock,
    block_metadata_path,
    create_constrained_voronoi_blocks,
    convert_texture_to_dxf,
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


class BlockMetadataTests(unittest.TestCase):
    def export_blocked_pair(self, root: Path) -> tuple[Path, Path]:
        output = root / "layer_01.dxf"
        metadata = block_metadata_path(output)
        blocks = [
            VoronoiBlock(0, -2.0, 0.0, ((-5, -5), (0, -5), (0, 5), (-5, 5)), 50),
            VoronoiBlock(1, 2.0, 0.0, ((0, -5), (5, -5), (5, 5), (0, 5)), 50),
        ]
        export_horizontal_hatch_dxf(
            np.ones((10, 10), dtype=bool),
            output,
            10,
            10,
            1,
            1,
            1,
            include_border=True,
            voronoi_blocks=blocks,
            block_metadata_output=metadata,
        )
        return output, metadata

    def assert_no_pair_or_owned_temps(self, root: Path) -> None:
        self.assertEqual(list(root.iterdir()), [])

    def test_writes_centers_counts_in_actual_dxf_block_order(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "layer_01.dxf"
            metadata = block_metadata_path(output)
            blocks = [
                VoronoiBlock(0, -2.0, -1.0, ((-5, -5), (0, -5), (0, 5), (-5, 5)), 50),
                VoronoiBlock(1, 2.0, 1.0, ((0, -5), (5, -5), (5, 5), (0, 5)), 50),
            ]
            _, counts = export_horizontal_hatch_dxf(
                np.ones((10, 10), dtype=bool), output,
                10, 10, 1, 1, 1,
                voronoi_blocks=blocks,
                block_metadata_output=metadata,
            )

            document = json.loads(metadata.read_text(encoding="utf-8"))
            self.assertEqual(document["version"], 1)
            self.assertEqual(document["border_line_count"], 0)
            self.assertEqual([block["line_count"] for block in document["blocks"]], counts)
            self.assertEqual(
                [(block["center_x"], block["center_y"]) for block in document["blocks"]],
                [(2.0, 1.0), (-2.0, -1.0)],
            )

    def test_records_four_border_lines_before_block_lines(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "layer_01.dxf"
            metadata = block_metadata_path(output)
            blocks = create_constrained_voronoi_blocks(10, 10, 2, random_seed=7)
            export_horizontal_hatch_dxf(
                np.ones((10, 10), dtype=bool), output,
                10, 10, 1, 1, 1,
                include_border=True,
                voronoi_blocks=blocks,
                block_metadata_output=metadata,
            )
            document = json.loads(metadata.read_text(encoding="utf-8"))
            self.assertEqual(document["border_line_count"], 4)
            self.assertEqual(
                4 + sum(block["line_count"] for block in document["blocks"]),
                len(read_line_coordinates(output)),
            )

    def test_convert_writes_sidecar_only_when_blocks_are_enabled(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            input_path = root / "texture.tiff"
            Image.fromarray(np.zeros((10, 10), dtype=np.uint8)).save(
                input_path, dpi=(25.4, 25.4)
            )
            blocked = root / "blocked.dxf"
            plain = root / "plain.dxf"
            common = dict(
                target_width_mm=10,
                target_height_mm=10,
                hatch_spacing_mm=1,
                tile_mode="repeat",
                min_block_area_mm2=0,
                max_block_area_mm2=100,
            )
            convert_texture_to_dxf(
                input_path, blocked, voronoi_block_count=2, **common
            )
            convert_texture_to_dxf(
                input_path, plain, voronoi_block_count=0, **common
            )
            self.assertTrue(block_metadata_path(blocked).is_file())
            self.assertFalse(block_metadata_path(plain).exists())

    def test_metadata_write_failure_leaves_no_pair_or_owned_temps(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with mock.patch.object(
                hatch,
                "write_block_metadata",
                side_effect=OSError("injected metadata write failure"),
            ):
                with self.assertRaisesRegex(OSError, "injected metadata write failure"):
                    self.export_blocked_pair(root)

            self.assert_no_pair_or_owned_temps(root)

    def test_pair_validation_failure_leaves_no_pair_or_owned_temps(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with mock.patch.object(
                hatch,
                "validate_hatch_output_pair",
                create=True,
                side_effect=ValueError("injected pair validation failure"),
            ):
                with self.assertRaisesRegex(ValueError, "injected pair validation failure"):
                    self.export_blocked_pair(root)

            self.assert_no_pair_or_owned_temps(root)

    def test_second_publication_failure_rolls_back_both_owned_outputs(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            publication_count = 0

            def publish_then_fail(source: Path, destination: Path) -> None:
                nonlocal publication_count
                publication_count += 1
                os.link(source, destination, follow_symlinks=False)
                source.unlink()
                if publication_count == 2:
                    raise OSError("injected metadata publication failure")

            with mock.patch.object(
                hatch,
                "_publish_file_no_replace",
                create=True,
                side_effect=publish_then_fail,
            ):
                with self.assertRaisesRegex(
                    OSError,
                    "injected metadata publication failure",
                ):
                    self.export_blocked_pair(root)

            self.assertEqual(publication_count, 2)
            self.assert_no_pair_or_owned_temps(root)

    def test_successful_pair_is_content_consistent_and_has_no_owned_temps(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output, metadata = self.export_blocked_pair(root)

            document = json.loads(metadata.read_text(encoding="utf-8"))
            self.assertEqual(
                len(read_line_coordinates(output)),
                document["border_line_count"]
                + sum(block["line_count"] for block in document["blocks"]),
            )
            self.assertEqual(
                sorted(path.name for path in root.iterdir()),
                ["layer_01.blocks.json", "layer_01.dxf"],
            )

    def test_preexisting_pair_is_preserved_without_owned_temp_leftovers(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            output.write_bytes(b"preexisting dxf sentinel")
            metadata.write_bytes(b"preexisting metadata sentinel")

            with self.assertRaises(FileExistsError):
                self.export_blocked_pair(root)

            self.assertEqual(output.read_bytes(), b"preexisting dxf sentinel")
            self.assertEqual(metadata.read_bytes(), b"preexisting metadata sentinel")
            self.assertEqual(
                sorted(path.name for path in root.iterdir()),
                ["layer_01.blocks.json", "layer_01.dxf"],
            )

    def test_log_reports_exact_effective_and_empty_block_counts(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            input_path = root / "texture.tiff"
            Image.fromarray(np.zeros((2, 2), dtype=np.uint8)).save(
                input_path,
                dpi=(25.4, 25.4),
            )
            blocks = [
                VoronoiBlock(index, float(index), 0.0, ((0, 0),), 1.0)
                for index in range(4)
            ]
            output = io.StringIO()
            with (
                mock.patch.object(
                    hatch,
                    "create_constrained_voronoi_blocks",
                    return_value=blocks,
                ),
                mock.patch.object(
                    hatch,
                    "export_horizontal_hatch_dxf",
                    return_value=(4, [3, 0, 1, 0]),
                ),
                redirect_stdout(output),
            ):
                convert_texture_to_dxf(
                    input_path,
                    root / "layer_01.dxf",
                    2,
                    2,
                    1,
                    tile_mode="repeat",
                    voronoi_block_count=4,
                )

            lines = output.getvalue().splitlines()
            self.assertEqual([line for line in lines if "有效加工块" in line], ["有效加工块: 2"])
            self.assertEqual([line for line in lines if "空加工块" in line], ["空加工块: 2"])


class AvaloniaPairValidationSourceContractTests(unittest.TestCase):
    def test_validates_each_expected_pair_before_manifest_and_preview_acceptance(self) -> None:
        source = (
            Path(__file__).resolve().parents[1] / "GrayscaleLayersMac" / "MainWindow.cs"
        ).read_text(encoding="utf-8")
        hatch_success = source.index("if (hatchExitCode != 0)")
        validation = source.index("ValidateGeneratedLayerPair(", hatch_success)
        manifest_acceptance = source.index("currentRunDxfFiles.Add", hatch_success)
        preview_acceptance = source.index("new DxfPreviewItem", hatch_success)

        self.assertLess(validation, manifest_acceptance)
        self.assertLess(validation, preview_acceptance)

        helper_start = source.index("private static void ValidateGeneratedLayerPair")
        helper_end = source.index("\n    private ", helper_start + 1)
        helper = source[helper_start:helper_end]
        self.assertIn('Path.ChangeExtension(dxfPath, ".blocks.json")', helper)
        self.assertIn("FileAttributes.Directory", helper)
        self.assertIn("FileAttributes.ReparsePoint", helper)
        self.assertIn("Length <= 0", helper)


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
