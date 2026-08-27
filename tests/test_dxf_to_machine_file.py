from pathlib import Path
from datetime import datetime
from dataclasses import FrozenInstanceError
from decimal import Decimal
import inspect
import json
import os
import shutil
import stat
import subprocess
import sys
import tempfile
import threading
import unittest
from unittest import mock

import numpy as np

import dxf_to_machine_file as machine
from dxf_to_machine_file import (
    DEFAULT_GALVO_OFFSET,
    DEFAULT_LASER_PARAMS,
    PatchPlacement,
    PlannedPatch,
    build_machine_document,
    discover_layer_dxf_files,
    select_layer_dxf_files,
    extract_layer_number,
    generate_machine_file,
    make_patch,
    read_dxf_lines,
    resolve_output_name,
    validate_machine_directory,
)


def write_dxf(path: Path, rows: list[tuple[float, float, float, float, float, float]]) -> None:
    chunks = ["0\nSECTION\n2\nENTITIES\n"]
    for x1, y1, z1, x2, y2, z2 in rows:
        chunks.append(
            "0\nLINE\n"
            f"10\n{x1}\n20\n{y1}\n30\n{z1}\n"
            f"11\n{x2}\n21\n{y2}\n31\n{z2}\n"
        )
    chunks.append("0\nENDSEC\n0\nEOF\n")
    path.write_text("".join(chunks), encoding="ascii")


def write_block_metadata(
    path: Path,
    border: int,
    blocks: list[dict[str, object]],
) -> None:
    path.with_suffix(".blocks.json").write_text(
        json.dumps({"version": 1, "border_line_count": border, "blocks": blocks}),
        encoding="utf-8",
    )


def write_two_layer_block_fixture(dxf_dir: Path) -> list[Path]:
    first = dxf_dir / "layer_1_a.dxf"
    second = dxf_dir / "layer_2_b.dxf"
    write_dxf(first, [
        (-1, -1, 0, 1, -1, 0), (1, -1, 0, 1, 1, 0),
        (1, 1, 0, -1, 1, 0), (-1, 1, 0, -1, -1, 0),
        (11, 7, 0, 14, 3, 0),
        (19, 4, 0, 16, 0, 0),
    ])
    write_block_metadata(first, 4, [
        {"block_index": 4, "center_x": 10.0, "center_y": 5.0, "line_count": 1},
        {"block_index": 7, "center_x": 18.0, "center_y": 2.0, "line_count": 1},
    ])
    write_dxf(second, [
        (-2, -2, 0, 2, -2, 0), (2, -2, 0, 2, 2, 0),
        (2, 2, 0, -2, 2, 0), (-2, 2, 0, -2, -2, 0),
        (-3, 8, 0, -5, 6, 0),
        (0, 12, 0, -2, 10, 0),
    ])
    write_block_metadata(second, 4, [
        {"block_index": 2, "center_x": -4.0, "center_y": 7.0, "line_count": 1},
        {"block_index": 9, "center_x": -1.0, "center_y": 11.0, "line_count": 1},
    ])
    return [first, second]


class ReadDxfLinesTests(unittest.TestCase):
    def test_reads_line_entities_in_source_order_and_preserves_direction(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "source.dxf"
            write_dxf(path, [(1, 2, 3, 4, 5, 6), (9, 8, 7, 6, 5, 4)])

            lines = read_dxf_lines(path)

        np.testing.assert_array_equal(
            lines,
            np.array([[1, 2, 3, 4, 5, 6], [9, 8, 7, 6, 5, 4]], dtype=np.float64),
        )
        self.assertEqual(lines.dtype, np.dtype(np.float64))

    def test_rejects_truncated_group_code_value_pair(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "truncated.dxf"
            path.write_text("0\nLINE\n10\n", encoding="ascii")
            with self.assertRaises(ValueError):
                read_dxf_lines(path)

    def test_rejects_line_missing_required_coordinate(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "missing-coordinate.dxf"
            path.write_text("0\nLINE\n10\n1\n20\n2\n30\n3\n11\n4\n21\n5\n", encoding="ascii")
            with self.assertRaises(ValueError):
                read_dxf_lines(path)

    def test_rejects_dxf_with_no_line_entities(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "empty.dxf"
            path.write_text("0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n", encoding="ascii")
            with self.assertRaises(ValueError):
                read_dxf_lines(path)

    def test_rejects_non_finite_line_coordinate(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "not-finite.dxf"
            write_dxf(path, [(1, 2, float("nan"), 4, 5, 6)])
            with self.assertRaises(ValueError):
                read_dxf_lines(path)


class LayerDiscoveryTests(unittest.TestCase):
    def test_extracts_layer_number_from_full_valid_filename(self) -> None:
        self.assertEqual(extract_layer_number(Path("LAYER_002_gray.dXf")), 2)

    def test_sorts_layers_by_numeric_layer_number(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dxf_dir = Path(directory)
            for name in ("layer_10_last.dxf", "layer_2_middle.dxf", "layer_1_first.dxf"):
                (dxf_dir / name).write_text("", encoding="ascii")

            files = discover_layer_dxf_files(dxf_dir, require_contiguous=False)

        self.assertEqual([path.name for path in files], ["layer_1_first.dxf", "layer_2_middle.dxf", "layer_10_last.dxf"])

    def test_rejects_duplicate_numeric_layer_numbers(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dxf_dir = Path(directory)
            (dxf_dir / "layer_1_a.dxf").write_text("", encoding="ascii")
            (dxf_dir / "layer_01_b.dxf").write_text("", encoding="ascii")
            with self.assertRaises(ValueError):
                discover_layer_dxf_files(dxf_dir)

    def test_rejects_gapped_layer_numbers(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dxf_dir = Path(directory)
            (dxf_dir / "layer_1_a.dxf").write_text("", encoding="ascii")
            (dxf_dir / "layer_3_c.dxf").write_text("", encoding="ascii")
            with self.assertRaises(ValueError):
                discover_layer_dxf_files(dxf_dir)

    def test_rejects_missing_directory_and_directory_without_matches(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dxf_dir = Path(directory)
            with self.assertRaises(ValueError):
                discover_layer_dxf_files(dxf_dir / "missing")
            (dxf_dir / "unrelated.dxf").write_text("", encoding="ascii")
            with self.assertRaises(ValueError):
                discover_layer_dxf_files(dxf_dir)

    def test_explicit_manifest_ignores_historical_duplicate_in_directory(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dxf_dir = Path(directory).resolve()
            historical = dxf_dir / "layer_1_old.dxf"
            current = dxf_dir / "layer_01_current.dxf"
            historical.write_text("old", encoding="ascii")
            current.write_text("current", encoding="ascii")

            files = select_layer_dxf_files(dxf_dir, [current])

        self.assertEqual(files, [current])

    def test_explicit_manifest_sorts_numeric_layers(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dxf_dir = Path(directory).resolve()
            first = dxf_dir / "layer_01_first.dxf"
            second = dxf_dir / "layer_02_second.dxf"
            first.write_text("first", encoding="ascii")
            second.write_text("second", encoding="ascii")

            files = select_layer_dxf_files(dxf_dir, [second, first])

        self.assertEqual(files, [first, second])

    def test_explicit_manifest_rejects_invalid_paths_and_numbers(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            dxf_dir = root / "dxfs"
            dxf_dir.mkdir()
            outside = root / "layer_01_outside.dxf"
            outside.write_text("outside", encoding="ascii")
            nested_dir = dxf_dir / "nested"
            nested_dir.mkdir()
            nested = nested_dir / "layer_01_nested.dxf"
            nested.write_text("nested", encoding="ascii")
            first = dxf_dir / "layer_01_first.dxf"
            duplicate = dxf_dir / "layer_1_duplicate.dxf"
            third = dxf_dir / "layer_03_third.dxf"
            invalid = dxf_dir / "not-a-layer.dxf"
            zero = dxf_dir / "layer_0_zero.dxf"
            for path in (first, duplicate, third, invalid, zero):
                path.write_text("x", encoding="ascii")

            invalid_manifests = (
                [],
                [Path("layer_01_first.dxf")],
                [outside],
                [nested],
                [dxf_dir / "layer_02_missing.dxf"],
                [invalid],
                [zero],
                [first, duplicate],
                [first, third],
            )
            for manifest in invalid_manifests:
                with self.subTest(manifest=manifest), self.assertRaises(ValueError):
                    select_layer_dxf_files(dxf_dir, manifest)

    def test_explicit_manifest_rejects_symlink_while_ambient_discovery_accepts_it(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            dxf_dir = root / "dxfs"
            dxf_dir.mkdir()
            target = root / "target.dxf"
            target.write_text("target", encoding="ascii")
            link = dxf_dir / "layer_1_link.dxf"
            link.symlink_to(target)

            self.assertEqual(discover_layer_dxf_files(dxf_dir), [link])
            with self.assertRaises(ValueError):
                select_layer_dxf_files(dxf_dir, [link])


class MakePatchTests(unittest.TestCase):
    def test_writes_patch_z_in_millimeters_and_preserves_block_local_xy(self) -> None:
        lines = np.array([[1.25, 2.5, 99, 3.75, 4.5, 88]], dtype=np.float64)

        patch = make_patch(lines, layer_index=2, layer_step_um=3, center_x=1, center_y=2)

        self.assertEqual(patch.dtype, np.dtype("<f4"))
        self.assertEqual(patch.shape, (1, 6))
        np.testing.assert_array_equal(
            patch[:, [0, 1, 3, 4]],
            np.array([[0.25, 0.5, 2.75, 2.5]], dtype="<f4"),
        )
        np.testing.assert_array_equal(patch[:, [2, 5]], np.array([[-0.006, -0.006]], dtype="<f4"))
        self.assertFalse(np.shares_memory(lines, patch))

    def test_rejects_invalid_line_shape(self) -> None:
        with self.assertRaises(ValueError):
            make_patch(np.zeros((1, 5)), layer_index=0, layer_step_um=1)

    def test_rejects_negative_layer_index(self) -> None:
        with self.assertRaises(ValueError):
            make_patch(np.zeros((1, 6)), layer_index=-1, layer_step_um=1)

    def test_handles_unsigned_numpy_layer_index_without_overflow(self) -> None:
        patch = make_patch(np.zeros((1, 6)), layer_index=np.uint64(2), layer_step_um=3)

        np.testing.assert_array_equal(patch[:, [2, 5]], np.array([[-0.006, -0.006]], dtype="<f4"))

    def test_rejects_invalid_layer_steps(self) -> None:
        lines = np.zeros((1, 6))
        for invalid_step in (
            True,
            0,
            -1,
            0.1,
            0.5,
            1.5,
            100001,
            9007199254740993,
            float("nan"),
            float("inf"),
        ):
            with self.subTest(layer_step_um=invalid_step):
                with self.assertRaises(ValueError):
                    make_patch(lines, layer_index=0, layer_step_um=invalid_step)

    def test_accepts_positive_integer_valued_layer_steps(self) -> None:
        lines = np.zeros((1, 6))
        for layer_step_um in (1, 3, 6, 3.0, np.float64(3.0), 100000):
            with self.subTest(layer_step_um=layer_step_um):
                patch = make_patch(
                    lines,
                    layer_index=2,
                    layer_step_um=layer_step_um,
                )
                expected_z = np.float32(-2 * float(layer_step_um) / 1000)
                self.assertEqual(patch[0, 2], expected_z)

    def test_make_patch_converts_xy_to_block_local_but_keeps_layer_z(self) -> None:
        lines = np.array([[11.0, 7.0, 0.0, 14.0, 3.0, 0.0]])

        patch = make_patch(lines, layer_index=2, layer_step_um=6, center_x=10, center_y=5)

        np.testing.assert_array_equal(
            patch,
            np.array([[1.0, 2.0, -0.012, 4.0, -2.0, -0.012]], dtype="<f4"),
        )

    def test_rejects_nonfinite_transformed_patch(self) -> None:
        with self.assertRaises(ValueError):
            make_patch(
                np.array([[float("inf"), 1, 0, 2, 3, 0]]),
                layer_index=0,
                layer_step_um=3,
            )


class MachineDocumentTests(unittest.TestCase):
    def test_builds_exact_defaults_and_custom_first_group(self) -> None:
        expected_first = {
            "power": 38, "frequency": 350, "pulseWidthIdx": 3,
            "scanSpeed": 2100, "jump_vel": 6000, "jump_delay": 50,
            "scan_ahead": True, "accScale": 50, "cornerScale": 100,
            "endScale": 100, "sky_writing": True, "timeLag": 100,
            "laserOnShift": 18, "delaseroff": 32, "delaseron": 0,
        }
        self.assertEqual(DEFAULT_LASER_PARAMS[0], expected_first)
        self.assertEqual(DEFAULT_GALVO_OFFSET, {"galvo_0": [0, 0, 0, 0]})
        custom = dict(expected_first, power=41)

        document = build_machine_document(
            [PatchPlacement(index, 0.0, 0.0) for index in range(3)],
            3,
            custom,
        )

        self.assertEqual(list(document), ["laser_params", "galvo_offset", "machine_cycle"])
        self.assertEqual(len(document["laser_params"]), 3)
        self.assertEqual(document["laser_params"][0]["power"], 41)
        self.assertEqual(document["laser_params"][1], {
            "frequency": 100, "power": 10, "pulseWidthIdx": 3,
            "scanSpeed": 2100, "jump_vel": 6000, "jump_delay": 50,
            "scan_ahead": True, "accScale": 50, "cornerScale": 100,
            "endScale": 100, "sky_writing": False, "timeLag": 100,
            "laserOnShift": 18, "delaseroff": 32, "delaseron": 0,
        })
        self.assertEqual(document["laser_params"][2], {
            "power": 20, "frequency": 350, "pulseWidthIdx": 4,
            "scanSpeed": 2100, "jump_vel": 6000, "jump_delay": 50,
            "scan_ahead": True, "accScale": 50, "cornerScale": 100,
            "endScale": 100, "sky_writing": True, "timeLag": 100,
            "laserOnShift": 18, "delaseroff": 32, "delaseron": 0,
        })
        self.assertEqual(document["galvo_offset"], {"galvo_0": [0, 0, 0, 0]})
        self.assertEqual(document["machine_cycle"], [
            {"galvo_0": [0, "G91G00X0.000Y0.000Z0.000F40", [0, 0]]},
            {"galvo_0": [0, "G00X0.000Y0.000Z-0.003F40", [1, 0]]},
            {"galvo_0": [0, "G91G00X0.000Y0.000Z-0.003F40G90", [2, 0]]},
        ])

    def test_deep_copies_defaults_and_caller_data(self) -> None:
        caller = dict(DEFAULT_LASER_PARAMS[0])
        document = build_machine_document([PatchPlacement(0, 0.0, 0.0)], 3, caller)
        document["laser_params"][0]["power"] = 999
        document["laser_params"][1]["power"] = 999
        document["galvo_offset"]["galvo_0"][0] = 999

        self.assertEqual(caller["power"], 38)
        self.assertEqual(DEFAULT_LASER_PARAMS[0]["power"], 38)
        self.assertEqual(DEFAULT_LASER_PARAMS[1]["power"], 10)
        self.assertEqual(DEFAULT_GALVO_OFFSET, {"galvo_0": [0, 0, 0, 0]})

    def test_rejects_invalid_counts_steps_and_first_group_types(self) -> None:
        valid = dict(DEFAULT_LASER_PARAMS[0])
        with self.assertRaises(ValueError):
            build_machine_document([], 3, valid)
        for step in (
            0,
            -1,
            0.1,
            0.5,
            1.5,
            float("nan"),
            float("inf"),
            float("-inf"),
        ):
            with self.subTest(step=step), self.assertRaises(ValueError):
                build_machine_document([PatchPlacement(0, 0.0, 0.0)], step, valid)
        invalid_groups = []
        missing = dict(valid); missing.pop("power"); invalid_groups.append(missing)
        extra = dict(valid, surprise=1); invalid_groups.append(extra)
        bool_integer = dict(valid, power=True); invalid_groups.append(bool_integer)
        float_integer = dict(valid, power=38.0); invalid_groups.append(float_integer)
        integer_boolean = dict(valid, scan_ahead=1); invalid_groups.append(integer_boolean)
        for group in invalid_groups:
            with self.subTest(group=group), self.assertRaises(ValueError):
                build_machine_document([PatchPlacement(0, 0.0, 0.0)], 3, group)

    def test_builds_relative_layer_cycles_with_mode_guards(self) -> None:
        placements = [PatchPlacement(index, 0.0, 0.0) for index in range(3)]
        document = build_machine_document(placements, 6, dict(DEFAULT_LASER_PARAMS[0]))
        self.assertEqual(
            [cycle["galvo_0"][1] for cycle in document["machine_cycle"]],
            [
                "G91G00X0.000Y0.000Z0.000F40",
                "G00X0.000Y0.000Z-0.006F40",
                "G91G00X0.000Y0.000Z-0.006F40G90",
            ],
        )

    def test_reasserts_relative_mode_on_final_multi_patch_cycle(self) -> None:
        document = build_machine_document(
            [PatchPlacement(0, 1.0, 2.0), PatchPlacement(0, 4.0, 6.0)],
            3,
            dict(DEFAULT_LASER_PARAMS[0]),
        )

        self.assertEqual(
            [cycle["galvo_0"][1] for cycle in document["machine_cycle"]],
            [
                "G91G00X1.000Y2.000Z0.000F40",
                "G91G00X3.000Y4.000Z0.000F40G90",
            ],
        )

    def test_moves_by_center_deltas_and_descends_only_on_layer_change(self) -> None:
        placements = [
            PatchPlacement(0, 10.0, 5.0),
            PatchPlacement(0, 18.0, 2.0),
            PatchPlacement(1, -4.0, 7.0),
        ]
        document = build_machine_document(placements, 6, dict(DEFAULT_LASER_PARAMS[0]))
        self.assertEqual(
            [cycle["galvo_0"][1] for cycle in document["machine_cycle"]],
            [
                "G91G00X10.000Y5.000Z0.000F40",
                "G00X8.000Y-3.000Z0.000F40",
                "G91G00X-22.000Y5.000Z-0.006F40G90",
            ],
        )

    def test_single_patch_enters_and_leaves_relative_mode(self) -> None:
        document = build_machine_document(
            [PatchPlacement(0, 2.0, -3.0)], 6, dict(DEFAULT_LASER_PARAMS[0])
        )
        self.assertEqual(
            document["machine_cycle"][0]["galvo_0"][1],
            "G91G00X2.000Y-3.000Z0.000F40G90",
        )

    def test_relative_deltas_accumulate_to_each_three_decimal_target(self) -> None:
        placements = [
            PatchPlacement(0, 0.0004, -0.0),
            PatchPlacement(0, 0.0008, -0.0001),
        ]
        document = build_machine_document(placements, 6, dict(DEFAULT_LASER_PARAMS[0]))
        self.assertEqual(
            [cycle["galvo_0"][1] for cycle in document["machine_cycle"]],
            [
                "G91G00X0.000Y0.000Z0.000F40",
                "G91G00X0.001Y0.000Z0.000F40G90",
            ],
        )

    def test_rejects_nonfinite_delta_between_individually_finite_centers(self) -> None:
        placements = [
            PatchPlacement(0, 1e308, 0.0),
            PatchPlacement(0, -1e308, 0.0),
        ]

        with self.assertRaises(ValueError):
            build_machine_document(placements, 6, dict(DEFAULT_LASER_PARAMS[0]))

    def test_rejects_invalid_placement_sequences_and_values(self) -> None:
        valid = dict(DEFAULT_LASER_PARAMS[0])
        invalid_placements = [
            [PatchPlacement(1, 0.0, 0.0)],
            [PatchPlacement(0, 0.0, 0.0), PatchPlacement(2, 0.0, 0.0)],
            [PatchPlacement(0, 0.0, 0.0), PatchPlacement(1, 0.0, 0.0), PatchPlacement(0, 0.0, 0.0)],
            [PatchPlacement(True, 0.0, 0.0)],
            [PatchPlacement(0, True, 0.0)],
            [PatchPlacement(0, 0.0, False)],
            [PatchPlacement(0, float("nan"), 0.0)],
            [PatchPlacement(0, 0.0, float("inf"))],
        ]
        for placements in invalid_placements:
            with self.subTest(placements=placements), self.assertRaises(ValueError):
                build_machine_document(placements, 3, valid)


class VendorCommandSimulatorTests(unittest.TestCase):
    def test_executes_final_motion_before_trailing_g90_in_vendor_lexical_order(self) -> None:
        cycles = [
            {"galvo_0": [0, "G91G00X10.000Y5.000Z0.000F40", [0, 0]]},
            {"galvo_0": [0, "G91G00X-2.000Y3.000Z-0.006F40G90", [1, 0]]},
        ]

        states = machine._simulate_vendor_machine_cycles(cycles)

        self.assertEqual(
            states,
            [
                (Decimal("10.000"), Decimal("5.000"), Decimal("0.000")),
                (Decimal("8.000"), Decimal("8.000"), Decimal("-0.006")),
            ],
        )

    def test_single_command_moves_relatively_then_restores_absolute_mode(self) -> None:
        states = machine._simulate_vendor_machine_cycles([
            {"galvo_0": [0, "G91G00X2.000Y-3.000Z-0.001F40G90", [0, 0]]}
        ])

        self.assertEqual(
            states,
            [(Decimal("2.000"), Decimal("-3.000"), Decimal("-0.001"))],
        )

    def test_accumulates_large_thousandth_coordinates_without_decimal_rounding(self) -> None:
        large = "1000000000000000019884624838656.000"
        states = machine._simulate_vendor_machine_cycles([
            {"galvo_0": [0, f"G91G00X{large}Y0.000Z0.000F40", [0, 0]]},
            {"galvo_0": [0, f"G91G00X-{large}Y0.000Z-0.001F40G90", [1, 0]]},
        ])

        self.assertEqual(
            states,
            [
                (Decimal(large), Decimal("0.000"), Decimal("0.000")),
                (Decimal("0.000"), Decimal("0.000"), Decimal("-0.001")),
            ],
        )

    def test_rejects_commands_outside_the_restricted_vendor_grammar(self) -> None:
        invalid_cycle_lists = [
            [{"galvo_0": [0, "G00X1.000Y2.000Z0.000F40G90", [0, 0]]}],
            [{"galvo_0": [0, "G91G00X1.000Y2.000Z0.000G90F40", [0, 0]]}],
            [{"galvo_0": [0, "G91G00X1.000Y2.000Z0.000F40", [0, 0]]}],
            [{"galvo_0": [0, "G91G00XnanY2.000Z0.000F40G90", [0, 0]]}],
            [{"galvo_0": [0, "G91G00X1.00١Y2.000Z0.000F40G90", [0, 0]]}],
            [{"galvo_0": [0, "G91G00X1.000Y2.000Z0.000F40G90X0.000", [0, 0]]}],
            [{"galvo_0": [0, "G91G00X1.000Y2.000Z0.000F40G90", [1, 0]]}],
            [
                {"galvo_0": [0, "G91G00X1.000Y2.000Z0.000F40G90", [0, 0]]},
                {"galvo_0": [0, "G00X1.000Y2.000Z0.000F40", [1, 0]]},
            ],
        ]
        for cycles in invalid_cycle_lists:
            with self.subTest(cycles=cycles), self.assertRaises(ValueError):
                machine._simulate_vendor_machine_cycles(cycles)

    def test_simulator_is_independent_of_generation_helpers(self) -> None:
        cycles = [
            {"galvo_0": [0, "G91G00X1.000Y2.000Z-0.003F40G90", [0, 0]]}
        ]
        with (
            mock.patch.object(
                machine,
                "build_machine_document",
                side_effect=AssertionError("builder must not be reused"),
            ),
            mock.patch.object(
                machine,
                "make_patch",
                side_effect=AssertionError("patch helper must not be reused"),
            ),
        ):
            states = machine._simulate_vendor_machine_cycles(cycles)

        self.assertEqual(states[0][2], Decimal("-0.003"))


class BlockMetadataTests(unittest.TestCase):
    def test_reads_exact_v1_metadata_into_immutable_values(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dxf = Path(directory) / "layer_1_a.dxf"
            write_block_metadata(dxf, 4, [
                {
                    "block_index": 7,
                    "center_x": 10.5,
                    "center_y": -2,
                    "line_count": 3,
                }
            ])

            metadata = machine.read_block_metadata(dxf)

            self.assertEqual(metadata, machine.BlockMetadata(
                border_line_count=4,
                blocks=(machine.BlockDefinition(7, 10.5, -2.0, 3),),
            ))
            with self.assertRaises(FrozenInstanceError):
                metadata.border_line_count = 5
            with self.assertRaises(FrozenInstanceError):
                metadata.blocks[0].center_x = 11.0

    def test_rejects_missing_malformed_nonregular_and_symlink_sidecars(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf = root / "layer_1_a.dxf"
            sidecar = dxf.with_suffix(".blocks.json")

            with self.subTest(case="missing"), self.assertRaises(ValueError):
                machine.read_block_metadata(dxf)

            sidecar.write_text("{broken", encoding="utf-8")
            with self.subTest(case="malformed"), self.assertRaises(ValueError):
                machine.read_block_metadata(dxf)

            sidecar.unlink()
            sidecar.mkdir()
            with self.subTest(case="directory"), self.assertRaises(ValueError):
                machine.read_block_metadata(dxf)

            sidecar.rmdir()
            target = root / "metadata-target.json"
            target.write_text(
                json.dumps({"version": 1, "border_line_count": 0, "blocks": []}),
                encoding="utf-8",
            )
            try:
                sidecar.symlink_to(target)
            except OSError as exc:
                self.skipTest(f"symlinks unsupported: {exc}")
            with self.subTest(case="symlink"), self.assertRaises(ValueError):
                machine.read_block_metadata(dxf)

    def test_rejects_invalid_top_level_schema_and_types(self) -> None:
        valid = {"version": 1, "border_line_count": 0, "blocks": []}
        invalid_documents = {
            "top-level list": [],
            "version value": {**valid, "version": 2},
            "version bool": {**valid, "version": True},
            "version float": {**valid, "version": 1.0},
            "missing field": {"version": 1, "blocks": []},
            "extra field": {**valid, "extra": None},
            "blocks type": {**valid, "blocks": {}},
            "border bool": {**valid, "border_line_count": False},
            "border negative": {**valid, "border_line_count": -1},
        }
        with tempfile.TemporaryDirectory() as directory:
            dxf = Path(directory) / "layer_1_a.dxf"
            sidecar = dxf.with_suffix(".blocks.json")
            for case, document in invalid_documents.items():
                with self.subTest(case=case):
                    sidecar.write_text(json.dumps(document), encoding="utf-8")
                    with self.assertRaises(ValueError):
                        machine.read_block_metadata(dxf)

    def test_rejects_unsupported_border_counts_and_empty_block_list(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dxf = Path(directory) / "layer_1_a.dxf"
            sidecar = dxf.with_suffix(".blocks.json")
            invalid_documents = {
                "border one": {"version": 1, "border_line_count": 1, "blocks": [{
                    "block_index": 0,
                    "center_x": 0.0,
                    "center_y": 0.0,
                    "line_count": 1,
                }]},
                "border two": {"version": 1, "border_line_count": 2, "blocks": [{
                    "block_index": 0,
                    "center_x": 0.0,
                    "center_y": 0.0,
                    "line_count": 1,
                }]},
                "border three": {"version": 1, "border_line_count": 3, "blocks": [{
                    "block_index": 0,
                    "center_x": 0.0,
                    "center_y": 0.0,
                    "line_count": 1,
                }]},
                "border five": {"version": 1, "border_line_count": 5, "blocks": [{
                    "block_index": 0,
                    "center_x": 0.0,
                    "center_y": 0.0,
                    "line_count": 1,
                }]},
                "empty blocks": {"version": 1, "border_line_count": 0, "blocks": []},
            }
            for case, document in invalid_documents.items():
                with self.subTest(case=case):
                    sidecar.write_text(json.dumps(document), encoding="utf-8")
                    with self.assertRaises(ValueError):
                        machine.read_block_metadata(dxf)

    def test_fails_closed_when_no_follow_open_flag_is_unavailable(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dxf = Path(directory) / "layer_1_a.dxf"
            write_block_metadata(dxf, 0, [{
                "block_index": 0,
                "center_x": 0.0,
                "center_y": 0.0,
                "line_count": 1,
            }])

            with mock.patch.object(machine.os, "O_NOFOLLOW", None):
                with mock.patch.object(machine.os, "open") as open_file:
                    with self.assertRaisesRegex(ValueError, "O_NOFOLLOW"):
                        machine.read_block_metadata(dxf)

            open_file.assert_not_called()

    def test_rejects_invalid_block_schema_types_values_and_duplicates(self) -> None:
        valid_block = {
            "block_index": 0,
            "center_x": 1.0,
            "center_y": 2.0,
            "line_count": 1,
        }
        invalid_blocks = {
            "block not object": [None],
            "missing field": [
                {key: value for key, value in valid_block.items() if key != "center_x"}
            ],
            "extra field": [{**valid_block, "extra": None}],
            "index bool": [{**valid_block, "block_index": True}],
            "index negative": [{**valid_block, "block_index": -1}],
            "line count bool": [{**valid_block, "line_count": False}],
            "line count negative": [{**valid_block, "line_count": -1}],
            "center bool": [{**valid_block, "center_x": True}],
            "center string": [{**valid_block, "center_y": "2"}],
            "center nan": [{**valid_block, "center_x": float("nan")}],
            "center infinity": [{**valid_block, "center_y": float("inf")}],
            "duplicate index": [valid_block, {**valid_block, "center_x": 9.0}],
        }
        with tempfile.TemporaryDirectory() as directory:
            dxf = Path(directory) / "layer_1_a.dxf"
            for case, blocks in invalid_blocks.items():
                with self.subTest(case=case):
                    write_block_metadata(dxf, 0, blocks)
                    with self.assertRaises(ValueError):
                        machine.read_block_metadata(dxf)


class PatchPlanTests(unittest.TestCase):
    def test_block_plan_excludes_border_skips_empty_and_localizes_xy(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dxf = Path(directory) / "layer_1_a.dxf"
            write_dxf(dxf, [
                (-5, -5, 0, 5, -5, 0), (5, -5, 0, 5, 5, 0),
                (5, 5, 0, -5, 5, 0), (-5, 5, 0, -5, -5, 0),
                (11, 7, 0, 14, 3, 0), (20, 8, 0, 23, 9, 0),
            ])
            write_block_metadata(dxf, 4, [
                {"block_index": 4, "center_x": 10.0, "center_y": 5.0, "line_count": 1},
                {"block_index": 7, "center_x": 15.0, "center_y": 6.0, "line_count": 0},
                {"block_index": 2, "center_x": 20.0, "center_y": 8.0, "line_count": 1},
            ])

            plan = machine.build_patch_plan([dxf], block_center_positioning=True)

            self.assertEqual([item.placement for item in plan], [
                PatchPlacement(0, 10.0, 5.0), PatchPlacement(0, 20.0, 8.0)
            ])
            np.testing.assert_array_equal(plan[0].lines, np.array([
                [11, 7, 0, 14, 3, 0]
            ], dtype=np.float64))
            np.testing.assert_array_equal(plan[1].lines, np.array([
                [20, 8, 0, 23, 9, 0]
            ], dtype=np.float64))
            patch = make_patch(
                plan[0].lines,
                layer_index=plan[0].placement.layer_index,
                layer_step_um=6,
                center_x=plan[0].placement.center_x,
                center_y=plan[0].placement.center_y,
            )
            np.testing.assert_array_equal(patch[0, [0, 1, 3, 4]], [1, 2, 4, -2])

    def test_block_plan_quantizes_centers_to_machine_command_precision(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dxf = Path(directory) / "layer_1_a.dxf"
            write_dxf(dxf, [(0.1, 0.545484, 0, 0.2, 0.552109, 0)])
            write_block_metadata(dxf, 0, [{
                "block_index": 0,
                "center_x": -2.27263749,
                "center_y": 16.62577553,
                "line_count": 1,
            }])

            plan = machine.build_patch_plan([dxf], block_center_positioning=True)

            self.assertEqual(plan[0].placement, PatchPlacement(0, -2.273, 16.626))

    def test_unblocked_plan_retains_all_lines_without_opening_sidecar(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dxf = Path(directory) / "layer_1_a.dxf"
            rows = [(1, 2, 3, 4, 5, 6), (7, 8, 9, 10, 11, 12)]
            write_dxf(dxf, rows)
            dxf.with_suffix(".blocks.json").mkdir()

            plan = machine.build_patch_plan([dxf], block_center_positioning=False)

            self.assertEqual(
                [item.placement for item in plan],
                [PatchPlacement(0, 0.0, 0.0)],
            )
            np.testing.assert_array_equal(plan[0].lines, np.array(rows, dtype=np.float64))

    def test_rejects_metadata_counts_that_do_not_consume_every_line(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dxf = Path(directory) / "layer_1_a.dxf"
            write_dxf(dxf, [(1, 2, 0, 3, 4, 0), (5, 6, 0, 7, 8, 0)])
            for case, border, line_count in (
                ("under-count", 0, 1),
                ("over-count", 4, 1),
            ):
                with self.subTest(case=case):
                    write_block_metadata(dxf, border, [{
                        "block_index": 0,
                        "center_x": 0.0,
                        "center_y": 0.0,
                        "line_count": line_count,
                    }])
                    with self.assertRaises(ValueError):
                        machine.build_patch_plan([dxf], block_center_positioning=True)

    def test_rejects_all_empty_block_plan_and_empty_layer_list(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            dxf = Path(directory) / "layer_1_a.dxf"
            write_dxf(dxf, [
                (-1, -1, 0, 1, -1, 0), (1, -1, 0, 1, 1, 0),
                (1, 1, 0, -1, 1, 0), (-1, 1, 0, -1, -1, 0),
            ])
            write_block_metadata(dxf, 4, [{
                "block_index": 0,
                "center_x": 0.0,
                "center_y": 0.0,
                "line_count": 0,
            }])

            with self.assertRaises(ValueError):
                machine.build_patch_plan([dxf], block_center_positioning=True)
            for block_center_positioning in (False, True):
                with self.subTest(block_center_positioning=block_center_positioning):
                    with self.assertRaises(ValueError):
                        machine.build_patch_plan([], block_center_positioning)

    def test_rejects_an_empty_block_layer_even_when_another_layer_has_a_patch(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            first = root / "layer_1_a.dxf"
            second = root / "layer_2_b.dxf"
            write_dxf(first, [(1, 2, 0, 3, 4, 0)])
            write_block_metadata(first, 0, [{
                "block_index": 0,
                "center_x": 1.0,
                "center_y": 2.0,
                "line_count": 1,
            }])
            write_dxf(second, [
                (-1, -1, 0, 1, -1, 0), (1, -1, 0, 1, 1, 0),
                (1, 1, 0, -1, 1, 0), (-1, 1, 0, -1, -1, 0),
            ])
            write_block_metadata(second, 4, [{
                "block_index": 1,
                "center_x": 5.0,
                "center_y": 6.0,
                "line_count": 0,
            }])

            with self.assertRaises(ValueError):
                machine.build_patch_plan([first, second], block_center_positioning=True)


class OutputNameTests(unittest.TestCase):
    def test_resolves_valid_and_deterministic_blank_names(self) -> None:
        instant = datetime(2026, 8, 17, 12, 34, 56)
        self.assertEqual(resolve_output_name("job-01", instant), "job-01")
        self.assertEqual(resolve_output_name(None, instant), "machine_file_20260817_123456")
        self.assertEqual(resolve_output_name("  ", instant), "machine_file_20260817_123456")

    def test_strips_nonblank_output_name(self) -> None:
        self.assertEqual(resolve_output_name(" job "), "job")

    def test_rejects_unsafe_names(self) -> None:
        for name in (".", "..", "a/b", "a\\b", "../job"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                resolve_output_name(name)


class AtomicPublicationTests(unittest.TestCase):
    def test_rename_no_replace_moves_source_when_destination_is_absent(self) -> None:
        self.assertTrue(
            hasattr(machine, "_rename_no_replace"),
            "_rename_no_replace must implement atomic publication",
        )
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "source"
            destination = root / "destination"
            source.mkdir()
            (source / "sentinel").write_text("source-owned", encoding="utf-8")

            machine._rename_no_replace(source, destination)

            self.assertFalse(os.path.lexists(source))
            self.assertEqual(
                (destination / "sentinel").read_text(encoding="utf-8"),
                "source-owned",
            )

    def test_rename_no_replace_does_not_replace_existing_empty_directory(self) -> None:
        self.assertTrue(
            hasattr(machine, "_rename_no_replace"),
            "_rename_no_replace must implement atomic publication",
        )
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "source"
            destination = root / "destination"
            source.mkdir()
            destination.mkdir()
            (source / "sentinel").write_text("source-owned", encoding="utf-8")

            with self.assertRaises(FileExistsError):
                machine._rename_no_replace(source, destination)

            self.assertEqual(
                (source / "sentinel").read_text(encoding="utf-8"),
                "source-owned",
            )
            self.assertEqual(list(destination.iterdir()), [])


class GenerateMachineFileTests(unittest.TestCase):
    @unittest.skipUnless(sys.platform == "darwin", "macOS Finder flags are Darwin-specific")
    def test_published_package_is_visible_in_finder(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"
            dxf_dir.mkdir()
            write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 3, 4, 5, 6)])

            original_validate = machine.validate_machine_directory

            def validate_then_mark_hidden(*args: object, **kwargs: object) -> None:
                original_validate(*args, **kwargs)
                package_path = Path(args[0])
                for path in [package_path, *package_path.rglob("*")]:
                    os.chflags(path, os.lstat(path).st_flags | stat.UF_HIDDEN)

            with mock.patch.object(
                machine,
                "validate_machine_directory",
                side_effect=validate_then_mark_hidden,
            ):
                result = generate_machine_file(
                    dxf_dir,
                    "job",
                    3,
                    dict(DEFAULT_LASER_PARAMS[0]),
                )

            package_paths = [result, *result.rglob("*")]
            hidden_paths = [
                path
                for path in package_paths
                if os.lstat(path).st_flags & stat.UF_HIDDEN
            ]
            self.assertEqual(hidden_paths, [])

    def test_generates_two_layer_atomic_sibling_package(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"
            dxf_dir.mkdir()
            write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 9, 3, 4, 8)])
            write_dxf(dxf_dir / "layer_2_b.dxf", [(5, 6, 7, 8, 9, 6), (2, 3, 4, 5, 6, 7)])
            params = dict(DEFAULT_LASER_PARAMS[0], power=41)

            result = generate_machine_file(dxf_dir, "job", 3, params)

            self.assertEqual(result, (root / "job").absolute())
            self.assertTrue(result.is_dir())
            self.assertFalse((root / ".job.building").exists())
            self.assertEqual(sorted(path.name for path in (result / "patches").iterdir()), ["0_0.npy", "1_0.npy"])
            first = np.load(result / "patches" / "0_0.npy", allow_pickle=False)
            second = np.load(result / "patches" / "1_0.npy", allow_pickle=False)
            self.assertEqual(first.dtype, np.dtype("<f4"))
            self.assertEqual(first.shape, (1, 6))
            self.assertEqual(second.shape, (2, 6))
            np.testing.assert_array_equal(first[:, [2, 5]], np.array([[0, 0]], dtype="<f4"))
            np.testing.assert_array_equal(second[:, [2, 5]], np.array([[-0.003, -0.003], [-0.003, -0.003]], dtype="<f4"))
            document = json.loads((result / "machine.json").read_text(encoding="utf-8"))
            self.assertEqual(document["laser_params"][0]["power"], 41)

    def test_explicit_manifest_uses_current_file_despite_historical_duplicate(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            dxf_dir = root / "dxfs"
            dxf_dir.mkdir()
            historical = dxf_dir / "layer_1_old.dxf"
            current = dxf_dir / "layer_01_current.dxf"
            write_dxf(historical, [(90, 90, 0, 91, 91, 0)])
            write_dxf(current, [(1, 2, 0, 3, 4, 0)])

            result = generate_machine_file(
                dxf_dir,
                "job",
                3,
                dict(DEFAULT_LASER_PARAMS[0]),
                layer_files=[current],
            )
            patch = np.load(result / "patches" / "0_0.npy", allow_pickle=False)

        np.testing.assert_array_equal(
            patch[:, [0, 1, 3, 4]],
            np.array([[1, 2, 3, 4]], dtype="<f4"),
        )

    def test_invalid_explicit_manifest_leaves_no_machine_artifacts(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            dxf_dir = root / "dxfs"
            dxf_dir.mkdir()
            current = dxf_dir / "layer_01_current.dxf"
            current.write_text("invalid", encoding="ascii")

            with self.assertRaises(ValueError):
                generate_machine_file(
                    dxf_dir,
                    "job",
                    3,
                    dict(DEFAULT_LASER_PARAMS[0]),
                    layer_files=[dxf_dir / "layer_02_missing.dxf"],
                )

            self.assertFalse(os.path.lexists(root / "job"))
            self.assertFalse(os.path.lexists(root / ".job.building"))
            self.assertFalse(os.path.lexists(root / ".job.lock"))

    def test_generates_two_layer_block_local_package_and_relative_commands(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"
            dxf_dir.mkdir()
            write_two_layer_block_fixture(dxf_dir)

            result = generate_machine_file(
                dxf_dir,
                "job",
                6,
                dict(DEFAULT_LASER_PARAMS[0]),
                block_center_positioning=True,
            )

            patches = result / "patches"
            self.assertEqual(sorted(path.name for path in patches.iterdir()), [
                "0_0.npy", "1_0.npy", "2_0.npy", "3_0.npy"
            ])
            expected_xy = (
                [1, 2, 4, -2],
                [1, 2, -2, -2],
                [1, 1, -1, -1],
                [1, 1, -1, -1],
            )
            for index, xy in enumerate(expected_xy):
                patch = np.load(patches / f"{index}_0.npy", allow_pickle=False)
                np.testing.assert_array_equal(patch[0, [0, 1, 3, 4]], xy)
                np.testing.assert_array_equal(
                    patch[0, [2, 5]],
                    np.array(
                        [0.0, 0.0] if index < 2 else [-0.006, -0.006],
                        dtype="<f4",
                    ),
                )
            document = json.loads((result / "machine.json").read_text(encoding="utf-8"))
            commands = [
                cycle["galvo_0"][1]
                for cycle in document["machine_cycle"]
            ]
            self.assertEqual(commands, [
                "G91G00X10.000Y5.000Z0.000F40",
                "G00X8.000Y-3.000Z0.000F40",
                "G00X-22.000Y5.000Z-0.006F40",
                "G91G00X3.000Y4.000Z0.000F40G90",
            ])
            simulated_states = machine._simulate_vendor_machine_cycles(
                document["machine_cycle"]
            )
            self.assertEqual(
                [state[2] for state in simulated_states],
                [
                    Decimal("0.000"),
                    Decimal("0.000"),
                    Decimal("-0.006"),
                    Decimal("-0.006"),
                ],
            )
            for index, state in enumerate(simulated_states):
                patch = np.load(patches / f"{index}_0.npy", allow_pickle=False)
                self.assertEqual(patch[0, 2], np.float32(float(state[2])))

    def test_multilayer_machine_z_equals_every_patch_z_without_drift(self) -> None:
        for layer_step_um in (1, 3, 6, 3.0):
            with self.subTest(layer_step_um=layer_step_um), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                dxf_dir = root / "dxfs"
                dxf_dir.mkdir()
                for layer_number in range(1, 9):
                    write_dxf(
                        dxf_dir / f"layer_{layer_number}_a.dxf",
                        [(1, 2, 0, 3, 4, 0)],
                    )

                result = generate_machine_file(
                    dxf_dir,
                    "job",
                    layer_step_um,
                    dict(DEFAULT_LASER_PARAMS[0]),
                )
                document = json.loads(
                    (result / "machine.json").read_text(encoding="utf-8")
                )
                states = machine._simulate_vendor_machine_cycles(
                    document["machine_cycle"]
                )

                for patch_index, state in enumerate(states):
                    expected_z = Decimal(-patch_index * int(layer_step_um)) / Decimal(1000)
                    patch = np.load(
                        result / "patches" / f"{patch_index}_0.npy",
                        allow_pickle=False,
                    )
                    self.assertEqual(state[2], expected_z)
                    np.testing.assert_array_equal(
                        patch[:, [2, 5]],
                        np.full(
                            (patch.shape[0], 2),
                            np.float32(float(expected_z)),
                            dtype="<f4",
                        ),
                    )

    def test_independent_validation_rejects_systematically_wrong_builder_motion(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"
            dxf_dir.mkdir()
            write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 0, 3, 4, 0)])
            write_dxf(dxf_dir / "layer_2_a.dxf", [(1, 2, 0, 3, 4, 0)])
            real_builder = machine.build_machine_document

            def systematically_wrong_builder(*args: object, **kwargs: object) -> dict[str, object]:
                document = real_builder(*args, **kwargs)
                command = document["machine_cycle"][1]["galvo_0"][1]
                document["machine_cycle"][1]["galvo_0"][1] = command.replace(
                    "X0.000",
                    "X1.000",
                    1,
                )
                return document

            with mock.patch.object(
                machine,
                "build_machine_document",
                side_effect=systematically_wrong_builder,
            ):
                with self.assertRaises(ValueError):
                    generate_machine_file(
                        dxf_dir,
                        "job",
                        3,
                        dict(DEFAULT_LASER_PARAMS[0]),
                    )

            self.assertFalse(os.path.lexists(root / "job"))
            self.assertFalse(os.path.lexists(root / ".job.building"))
            self.assertFalse(os.path.lexists(root / ".job.lock"))

    def test_independent_validation_rejects_systematically_wrong_patch_helper(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"
            dxf_dir.mkdir()
            write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 0, 3, 4, 0)])
            real_make_patch = machine.make_patch

            def systematically_wrong_patch(*args: object, **kwargs: object) -> np.ndarray:
                patch = real_make_patch(*args, **kwargs)
                patch[:, [0, 3]] += np.float32(1.0)
                return patch

            with mock.patch.object(
                machine,
                "make_patch",
                side_effect=systematically_wrong_patch,
            ):
                with self.assertRaises(ValueError):
                    generate_machine_file(
                        dxf_dir,
                        "job",
                        3,
                        dict(DEFAULT_LASER_PARAMS[0]),
                    )

            self.assertFalse(os.path.lexists(root / "job"))
            self.assertFalse(os.path.lexists(root / ".job.building"))
            self.assertFalse(os.path.lexists(root / ".job.lock"))

    def test_block_mode_rejects_all_empty_layer_without_publishing(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"
            dxf_dir.mkdir()
            dxf = dxf_dir / "layer_1_a.dxf"
            write_dxf(dxf, [
                (-1, -1, 0, 1, -1, 0), (1, -1, 0, 1, 1, 0),
                (1, 1, 0, -1, 1, 0), (-1, 1, 0, -1, -1, 0),
            ])
            write_block_metadata(dxf, 4, [{
                "block_index": 0,
                "center_x": 1.0,
                "center_y": 2.0,
                "line_count": 0,
            }])

            with self.assertRaises(ValueError):
                generate_machine_file(
                    dxf_dir,
                    "job",
                    6,
                    dict(DEFAULT_LASER_PARAMS[0]),
                    block_center_positioning=True,
                )

            self.assertFalse(os.path.lexists(root / "job"))
            self.assertFalse(os.path.lexists(root / ".job.building"))
            self.assertFalse(os.path.lexists(root / ".job.lock"))

    def test_block_mode_rejects_semantically_invalid_metadata_without_publishing(self) -> None:
        invalid_metadata = (
            (1, [{
                "block_index": 0,
                "center_x": 1.0,
                "center_y": 2.0,
                "line_count": 1,
            }]),
            (0, []),
        )
        for border, blocks in invalid_metadata:
            with self.subTest(border=border, blocks=blocks), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                dxf_dir = root / "dxfs"
                dxf_dir.mkdir()
                dxf = dxf_dir / "layer_1_a.dxf"
                rows = (
                    [(0, 0, 0, 0, 0, 0), (1, 2, 0, 3, 4, 0)]
                    if border == 1
                    else [(1, 2, 0, 3, 4, 0)]
                )
                write_dxf(dxf, rows)
                write_block_metadata(dxf, border, blocks)

                with self.assertRaises(ValueError):
                    generate_machine_file(
                        dxf_dir,
                        "job",
                        6,
                        dict(DEFAULT_LASER_PARAMS[0]),
                        block_center_positioning=True,
                    )

                self.assertFalse(os.path.lexists(root / "job"))
                self.assertFalse(os.path.lexists(root / ".job.building"))
                self.assertFalse(os.path.lexists(root / ".job.lock"))

    @unittest.skipUnless(hasattr(os, "mkfifo"), "FIFO creation is unavailable")
    def test_fifo_sidecar_fails_within_timeout_without_publishing(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"
            dxf_dir.mkdir()
            dxf = dxf_dir / "layer_1_a.dxf"
            write_dxf(dxf, [(1, 2, 0, 3, 4, 0)])
            os.mkfifo(dxf.with_suffix(".blocks.json"))

            try:
                completed = subprocess.run(
                    [
                        sys.executable,
                        str(Path(__file__).parents[1] / "dxf_to_machine_file.py"),
                        str(dxf_dir),
                        "fifo-job",
                        "--block-center-positioning",
                    ],
                    text=True,
                    capture_output=True,
                    check=False,
                    timeout=2,
                )
            except subprocess.TimeoutExpired:
                self.fail("FIFO sidecar open blocked past the external two-second timeout")

            self.assertNotEqual(completed.returncode, 0)
            self.assertFalse(os.path.lexists(root / "fifo-job"))
            self.assertFalse(os.path.lexists(root / ".fifo-job.building"))
            self.assertFalse(os.path.lexists(root / ".fifo-job.lock"))

    def test_extreme_block_centers_cannot_publish_nonfinite_motion_delta(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"
            dxf_dir.mkdir()
            dxf = dxf_dir / "layer_1_a.dxf"
            write_dxf(dxf, [
                (1e308, 0, 0, 1e308, 0, 0),
                (-1e308, 0, 0, -1e308, 0, 0),
            ])
            write_block_metadata(dxf, 0, [
                {
                    "block_index": 0,
                    "center_x": 1e308,
                    "center_y": 0.0,
                    "line_count": 1,
                },
                {
                    "block_index": 1,
                    "center_x": -1e308,
                    "center_y": 0.0,
                    "line_count": 1,
                },
            ])

            with self.assertRaises(ValueError):
                generate_machine_file(
                    dxf_dir,
                    "job",
                    6,
                    dict(DEFAULT_LASER_PARAMS[0]),
                    block_center_positioning=True,
                )

            self.assertFalse(os.path.lexists(root / "job"))
            self.assertFalse(os.path.lexists(root / ".job.building"))
            self.assertFalse(os.path.lexists(root / ".job.lock"))

    def test_strips_nonblank_output_name_before_creating_package(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"; dxf_dir.mkdir()
            write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 3, 4, 5, 6)])

            result = generate_machine_file(
                dxf_dir,
                " job ",
                3,
                dict(DEFAULT_LASER_PARAMS[0]),
            )

            self.assertEqual(result, root / "job")
            self.assertTrue((root / "job").is_dir())
            self.assertFalse(os.path.lexists(root / " job "))

    def test_accepts_safe_owner_tokens_including_maximum_length(self) -> None:
        self.assertIn("owner_token", inspect.signature(generate_machine_file).parameters)
        for token in ("owner-1", "A_b-9", "x" * 128):
            with self.subTest(token=token), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                dxf_dir = root / "dxfs"; dxf_dir.mkdir()
                write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 3, 4, 5, 6)])

                result = generate_machine_file(
                    dxf_dir,
                    "job",
                    3,
                    dict(DEFAULT_LASER_PARAMS[0]),
                    owner_token=token,
                )

                self.assertTrue(result.is_dir())
                self.assertFalse(os.path.lexists(root / ".job.lock"))

    def test_rejects_unsafe_owner_tokens_before_creating_paths(self) -> None:
        self.assertIn("owner_token", inspect.signature(generate_machine_file).parameters)
        invalid_tokens = ("", " ", "owner/token", "owner.token", "拥有者", "x" * 129, 123)
        for token in invalid_tokens:
            with self.subTest(token=token), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                dxf_dir = root / "dxfs"; dxf_dir.mkdir()
                write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 3, 4, 5, 6)])

                with self.assertRaises(ValueError):
                    generate_machine_file(
                        dxf_dir,
                        "job",
                        3,
                        dict(DEFAULT_LASER_PARAMS[0]),
                        owner_token=token,  # type: ignore[arg-type]
                    )

                self.assertFalse(os.path.lexists(root / "job"))
                self.assertFalse(os.path.lexists(root / ".job.building"))
                self.assertFalse(os.path.lexists(root / ".job.lock"))

    def test_writes_and_flushes_owner_token_before_generation_continues(self) -> None:
        self.assertIn("owner_token", inspect.signature(generate_machine_file).parameters)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"; dxf_dir.mkdir()
            write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 3, 4, 5, 6)])
            lock_path = root / ".job.lock"
            entered_reader = threading.Event()
            release_reader = threading.Event()
            observed_tokens: list[str] = []
            failures: list[BaseException] = []
            real_reader = machine.read_dxf_lines

            def controlled_reader(path: Path) -> np.ndarray:
                observed_tokens.append(lock_path.read_text(encoding="ascii"))
                entered_reader.set()
                if not release_reader.wait(timeout=5):
                    raise RuntimeError("test did not release controlled DXF read")
                return real_reader(path)

            def generate() -> None:
                try:
                    generate_machine_file(
                        dxf_dir,
                        "job",
                        3,
                        dict(DEFAULT_LASER_PARAMS[0]),
                        owner_token="ui-run_01",
                    )
                except BaseException as exc:  # Preserve worker failure for the test thread.
                    failures.append(exc)

            with mock.patch.object(machine, "read_dxf_lines", side_effect=controlled_reader):
                worker = threading.Thread(target=generate)
                worker.start()
                try:
                    self.assertTrue(entered_reader.wait(timeout=5), failures)
                    self.assertEqual(observed_tokens, ["ui-run_01"])
                finally:
                    release_reader.set()
                    worker.join(timeout=5)

            self.assertFalse(worker.is_alive())
            if failures:
                raise failures[0]
            self.assertTrue((root / "job").is_dir())
            self.assertFalse(os.path.lexists(lock_path))

    def test_omitted_owner_token_writes_internal_uuid_hex(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"; dxf_dir.mkdir()
            write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 3, 4, 5, 6)])
            lock_path = root / ".job.lock"
            observed_tokens: list[str] = []
            real_reader = machine.read_dxf_lines

            def observe_lock_then_read(path: Path) -> np.ndarray:
                observed_tokens.append(lock_path.read_text(encoding="ascii"))
                return real_reader(path)

            with mock.patch.object(
                machine,
                "read_dxf_lines",
                side_effect=observe_lock_then_read,
            ):
                generate_machine_file(
                    dxf_dir,
                    "job",
                    3,
                    dict(DEFAULT_LASER_PARAMS[0]),
                )

            self.assertEqual(len(observed_tokens), 1)
            self.assertRegex(observed_tokens[0], r"\A[0-9a-f]{32}\Z")
            self.assertFalse(os.path.lexists(lock_path))

    def test_preserves_foreign_token_lock_and_replacement_temp_after_failure(self) -> None:
        self.assertIn("owner_token", inspect.signature(generate_machine_file).parameters)
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"; dxf_dir.mkdir()
            write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 3, 4, 5, 6)])
            lock_path = root / ".job.lock"
            temp_path = root / ".job.building"

            def replace_owned_paths(_path: Path) -> np.ndarray:
                shutil.rmtree(temp_path)
                temp_path.mkdir()
                (temp_path / "foreign-sentinel").write_text("keep", encoding="utf-8")
                lock_path.unlink()
                lock_path.write_text("foreign-owner", encoding="ascii")
                raise RuntimeError("forced failure after foreign paths replaced owned paths")

            with mock.patch.object(machine, "read_dxf_lines", side_effect=replace_owned_paths):
                with self.assertRaisesRegex(RuntimeError, "foreign paths replaced"):
                    generate_machine_file(
                        dxf_dir,
                        "job",
                        3,
                        dict(DEFAULT_LASER_PARAMS[0]),
                        owner_token="original-owner",
                    )

            self.assertEqual(lock_path.read_text(encoding="ascii"), "foreign-owner")
            self.assertEqual(
                (temp_path / "foreign-sentinel").read_text(encoding="utf-8"),
                "keep",
            )
            self.assertFalse(os.path.lexists(root / "job"))

    def test_rejects_dangling_final_or_temp_symlink_without_replacing_it(self) -> None:
        for existing_name in ("job", ".job.building"):
            with self.subTest(existing_name=existing_name), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                dxf_dir = root / "dxfs"; dxf_dir.mkdir()
                write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 3, 4, 5, 6)])
                existing = root / existing_name
                try:
                    existing.symlink_to("missing-target", target_is_directory=True)
                except OSError as exc:
                    self.skipTest(f"symlinks unsupported: {exc}")

                try:
                    generate_machine_file(dxf_dir, "job", 3, dict(DEFAULT_LASER_PARAMS[0]))
                except OSError as exc:
                    self.assertIsInstance(exc, FileExistsError)
                else:
                    self.fail("dangling output symlink was not rejected")

                self.assertTrue(os.path.lexists(existing))
                self.assertTrue(existing.is_symlink())
                self.assertEqual(os.readlink(existing), "missing-target")
                self.assertFalse(os.path.lexists(root / ".job.lock"))
                if existing_name != "job":
                    self.assertFalse(os.path.lexists(root / "job"))

    def test_rejects_colliding_output_lock_without_removing_other_owner_sentinel(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"; dxf_dir.mkdir()
            write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 3, 4, 5, 6)])
            lock_path = root / ".job.lock"
            lock_path.write_text("other-owner", encoding="utf-8")

            with self.assertRaises(FileExistsError):
                generate_machine_file(dxf_dir, "job", 3, dict(DEFAULT_LASER_PARAMS[0]))

            self.assertEqual(lock_path.read_text(encoding="utf-8"), "other-owner")
            self.assertFalse(os.path.lexists(root / "job"))
            self.assertFalse(os.path.lexists(root / ".job.building"))

    def test_owned_lock_is_removed_after_ordinary_generation_failure(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"; dxf_dir.mkdir()
            (dxf_dir / "layer_1_a.dxf").write_text(
                "0\nSECTION\n2\nENTITIES\n0\nEOF\n", encoding="ascii"
            )

            with self.assertRaises(ValueError):
                generate_machine_file(dxf_dir, "job", 3, dict(DEFAULT_LASER_PARAMS[0]))

            self.assertFalse(os.path.lexists(root / "job"))
            self.assertFalse(os.path.lexists(root / ".job.building"))
            self.assertFalse(os.path.lexists(root / ".job.lock"))

    def test_rejects_existing_final_or_temp_without_modifying_sentinels(self) -> None:
        for existing_name in ("job", ".job.building"):
            with self.subTest(existing_name=existing_name), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                dxf_dir = root / "dxfs"; dxf_dir.mkdir()
                write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 3, 4, 5, 6)])
                existing = root / existing_name; existing.mkdir()
                sentinel = existing / "sentinel"; sentinel.write_text("keep", encoding="utf-8")
                with self.assertRaises(FileExistsError):
                    generate_machine_file(dxf_dir, "job", 3, dict(DEFAULT_LASER_PARAMS[0]))
                self.assertEqual(sentinel.read_text(encoding="utf-8"), "keep")
                self.assertFalse((root / "job").exists() and existing_name != "job")

    def test_removes_exact_temp_directory_on_ordinary_failure(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"; dxf_dir.mkdir()
            write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 3, 4, 5, 6)])
            (dxf_dir / "layer_2_b.dxf").write_text("0\nSECTION\n2\nENTITIES\n0\nEOF\n", encoding="ascii")
            with self.assertRaises(ValueError):
                generate_machine_file(dxf_dir, "job", 3, dict(DEFAULT_LASER_PARAMS[0]))
            self.assertFalse((root / "job").exists())
            self.assertFalse((root / ".job.building").exists())

    def test_rejects_invalid_steps_before_creating_output(self) -> None:
        for step in (
            True,
            0,
            -1,
            0.1,
            0.5,
            1.5,
            100001,
            9007199254740993,
            float("nan"),
            float("inf"),
            float("-inf"),
        ):
            with self.subTest(step=step), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                dxf_dir = root / "dxfs"; dxf_dir.mkdir()
                write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 3, 4, 5, 6)])
                with self.assertRaises(ValueError):
                    generate_machine_file(dxf_dir, "job", step, dict(DEFAULT_LASER_PARAMS[0]))
                self.assertFalse((root / "job").exists())
                self.assertFalse((root / ".job.building").exists())
                self.assertFalse((root / ".job.lock").exists())

    def test_validates_float32_adjacent_steps_across_four_layers(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"; dxf_dir.mkdir()
            for layer in range(1, 5):
                write_dxf(dxf_dir / f"layer_{layer}_a.dxf", [(1, 2, 3, 4, 5, 6)])

            result = generate_machine_file(dxf_dir, "job", 3, dict(DEFAULT_LASER_PARAMS[0]))

            self.assertTrue(result.is_dir())

    def test_validates_float32_adjacent_steps_across_forty_layers(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"; dxf_dir.mkdir()
            for layer in range(1, 41):
                write_dxf(dxf_dir / f"layer_{layer}_a.dxf", [(1, 2, 3, 4, 5, 6)])

            result = generate_machine_file(dxf_dir, "job", 3, dict(DEFAULT_LASER_PARAMS[0]))

            plan = [
                PlannedPatch(
                    PatchPlacement(index, 0.0, 0.0),
                    read_dxf_lines(dxf_dir / f"layer_{index + 1}_a.dxf"),
                )
                for index in range(40)
            ]
            validate_machine_directory(result, plan, 3, dict(DEFAULT_LASER_PARAMS[0]))
            self.assertTrue(result.is_dir())

    def test_generates_custom_five_micrometer_patch_and_cycle_depths(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"; dxf_dir.mkdir()
            for layer in range(1, 4):
                write_dxf(dxf_dir / f"layer_{layer}_a.dxf", [(1, 2, 3, 4, 5, 6)])

            result = generate_machine_file(
                dxf_dir,
                "job",
                5,
                dict(DEFAULT_LASER_PARAMS[0]),
            )

            actual_z = [
                float(np.load(result / "patches" / f"{index}_0.npy", allow_pickle=False)[0, 2])
                for index in range(3)
            ]
            np.testing.assert_allclose(actual_z, [0.0, -0.005, -0.010], rtol=0, atol=1e-9)
            document = json.loads((result / "machine.json").read_text(encoding="utf-8"))
            self.assertEqual(
                [cycle["galvo_0"][1] for cycle in document["machine_cycle"]],
                [
                    "G91G00X0.000Y0.000Z0.000F40",
                    "G00X0.000Y0.000Z-0.005F40",
                    "G91G00X0.000Y0.000Z-0.005F40G90",
                ],
            )


class ValidateMachineDirectoryTests(unittest.TestCase):
    def _make_package(self, root: Path) -> tuple[Path, list[PlannedPatch]]:
        dxf_dir = root / "dxfs"; dxf_dir.mkdir()
        first_lines = np.array([[1, 2, 3, 4, 5, 6]], dtype=np.float64)
        second_lines = np.array([[2, 3, 4, 5, 6, 7]], dtype=np.float64)
        write_dxf(dxf_dir / "layer_1_a.dxf", [tuple(first_lines[0])])
        write_dxf(dxf_dir / "layer_2_b.dxf", [tuple(second_lines[0])])
        package = generate_machine_file(dxf_dir, "job", 3, dict(DEFAULT_LASER_PARAMS[0]))
        return package, [
            PlannedPatch(PatchPlacement(0, 0.0, 0.0), first_lines),
            PlannedPatch(PatchPlacement(1, 0.0, 0.0), second_lines),
        ]

    def _make_block_package(self, root: Path) -> tuple[Path, list[PlannedPatch]]:
        dxf_dir = root / "dxfs"
        dxf_dir.mkdir()
        layer_files = write_two_layer_block_fixture(dxf_dir)
        plan = machine.build_patch_plan(layer_files, block_center_positioning=True)
        package = generate_machine_file(
            dxf_dir,
            "job",
            6,
            dict(DEFAULT_LASER_PARAMS[0]),
            block_center_positioning=True,
        )
        return package, plan

    def test_rejects_subresolution_step_before_reading_machine_directory(self) -> None:
        plan = [
            PlannedPatch(
                PatchPlacement(0, 0.0, 0.0),
                np.array([[1, 2, 3, 4, 5, 6]], dtype=np.float64),
            )
        ]

        with self.assertRaises(ValueError):
            validate_machine_directory(
                Path("not-needed-for-layer-step-validation"),
                plan,
                0.1,
                dict(DEFAULT_LASER_PARAMS[0]),
            )

    def test_accepts_float32_local_xy_reconstruction_with_large_cancellation(self) -> None:
        source_lines = np.array(
            [[0.1, 0.545484, 0, 0.2, 0.552109, 0]],
            dtype=np.float64,
        )
        planned_patch = PlannedPatch(
            PatchPlacement(0, -2.273, 16.626),
            source_lines,
        )
        patch = make_patch(source_lines, 0, 3, -2.273, 16.626)

        machine._independently_validate_patch_geometry(
            0,
            patch,
            planned_patch,
            3,
        )

    def test_rejects_bad_patch_dtype(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            package, plan = self._make_package(Path(directory))
            patch_path = package / "patches" / "0_0.npy"
            np.save(patch_path, np.zeros((1, 6), dtype=np.float64))
            with self.assertRaises(ValueError):
                validate_machine_directory(package, plan, 3, dict(DEFAULT_LASER_PARAMS[0]))


    def test_rejects_bad_patch_shape(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            package, plan = self._make_package(Path(directory))
            np.save(package / "patches" / "0_0.npy", np.zeros((0, 6), dtype="<f4"))
            with self.assertRaises(ValueError):
                validate_machine_directory(package, plan, 3, dict(DEFAULT_LASER_PARAMS[0]))


    def test_rejects_bad_patch_z(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            package, plan = self._make_package(Path(directory))
            patch_path = package / "patches" / "1_0.npy"
            patch = np.load(patch_path, allow_pickle=False)
            patch[:, 2] = -0.004
            patch[:, 5] = -0.004
            np.save(patch_path, patch)
            with self.assertRaises(ValueError):
                validate_machine_directory(package, plan, 3, dict(DEFAULT_LASER_PARAMS[0]))

    def test_rejects_tampered_same_layer_block_local_patch(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            package, plan = self._make_block_package(Path(directory))
            patch_path = package / "patches" / "1_0.npy"
            patch = np.load(patch_path, allow_pickle=False)
            patch[0, 0] += 1
            np.save(patch_path, patch)

            with self.assertRaises(ValueError):
                validate_machine_directory(
                    package,
                    plan,
                    6,
                    dict(DEFAULT_LASER_PARAMS[0]),
                )

    def test_rejects_bad_cycle_reference(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            package, plan = self._make_package(Path(directory))
            json_path = package / "machine.json"
            document = json.loads(json_path.read_text(encoding="utf-8"))
            document["machine_cycle"][1]["galvo_0"][2] = [0, 0]
            json_path.write_text(json.dumps(document), encoding="utf-8")
            with self.assertRaises(ValueError):
                validate_machine_directory(package, plan, 3, dict(DEFAULT_LASER_PARAMS[0]))

    def test_rejects_bad_cycle_count(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            package, plan = self._make_package(Path(directory))
            json_path = package / "machine.json"
            document = json.loads(json_path.read_text(encoding="utf-8"))
            document["machine_cycle"].pop()
            json_path.write_text(json.dumps(document), encoding="utf-8")
            with self.assertRaises(ValueError):
                validate_machine_directory(package, plan, 3, dict(DEFAULT_LASER_PARAMS[0]))

    def test_rejects_any_extra_file_or_directory_in_patches(self) -> None:
        self.assertIn(
            "expected_first_laser_params",
            inspect.signature(validate_machine_directory).parameters,
        )
        params = dict(DEFAULT_LASER_PARAMS[0])
        for extra_name, is_directory in (("extra.npy", False), ("foreign", True)):
            with self.subTest(extra_name=extra_name), tempfile.TemporaryDirectory() as directory:
                package, plan = self._make_package(Path(directory))
                extra = package / "patches" / extra_name
                if is_directory:
                    extra.mkdir()
                else:
                    extra.write_text("sentinel", encoding="utf-8")

                with self.assertRaises(ValueError):
                    validate_machine_directory(package, plan, 3, params)

                self.assertTrue(os.path.lexists(extra))

    def test_rejects_validly_typed_first_laser_group_that_differs_from_expected(self) -> None:
        self.assertIn(
            "expected_first_laser_params",
            inspect.signature(validate_machine_directory).parameters,
        )
        with tempfile.TemporaryDirectory() as directory:
            params = dict(DEFAULT_LASER_PARAMS[0])
            package, plan = self._make_package(Path(directory))
            json_path = package / "machine.json"
            document = json.loads(json_path.read_text(encoding="utf-8"))
            document["laser_params"][0]["power"] = 999
            json_path.write_text(json.dumps(document), encoding="utf-8")

            with self.assertRaises(ValueError):
                validate_machine_directory(package, plan, 3, params)


class AvaloniaLayerStepSourceContractTests(unittest.TestCase):
    def test_layer_step_control_and_preflight_require_whole_micrometres(self) -> None:
        source = (
            Path(__file__).resolve().parents[1]
            / "GrayscaleLayersMac"
            / "MainWindow.cs"
        ).read_text(encoding="utf-8")
        field_start = source.index("private readonly NumericUpDown _pipelineLayerStepBox")
        field_end = source.index(
            "private readonly CheckBox _pipelineBlockCenterMotionBox",
            field_start,
        )
        field = source[field_start:field_end]
        self.assertIn("MakeNumberBox(3, 1, 100000, 0, minimum: 1)", field)

        preflight_start = source.index("var layerStep = _pipelineLayerStepBox.Value;")
        preflight_end = source.index("var layerScript =", preflight_start)
        preflight = source[preflight_start:preflight_end]
        self.assertIn("layerStep.Value < 1m", preflight)
        self.assertIn("layerStep.Value > 100000m", preflight)
        self.assertIn(
            "layerStep.Value != decimal.Truncate(layerStep.Value)",
            preflight,
        )

    def test_cancellation_leaves_machine_artifacts_for_owner_safe_recovery(self) -> None:
        source = (
            Path(__file__).resolve().parents[1]
            / "GrayscaleLayersMac"
            / "MainWindow.cs"
        ).read_text(encoding="utf-8")
        catch_start = source.index("catch (OperationCanceledException)")
        catch_end = source.index("catch (Exception ex)", catch_start)
        cancellation_handler = source[catch_start:catch_end]

        self.assertNotIn("CleanupMachineArtifactsAfterCancellation", cancellation_handler)
        self.assertIn("不会自动删除", cancellation_handler)
        self.assertNotIn("Directory.Delete(tempPath, recursive: true)", source)
        self.assertNotIn("File.Delete(lockPath)", source)


class CliTests(unittest.TestCase):
    def test_cli_default_discovery_accepts_symlinked_layer_file(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            dxf_dir = root / "dxfs"
            dxf_dir.mkdir()
            target = root / "target.dxf"
            write_dxf(target, [(1, 2, 0, 3, 4, 0)])
            link = dxf_dir / "layer_1_link.dxf"
            link.symlink_to(target)

            completed = subprocess.run(
                [
                    sys.executable,
                    str(Path(__file__).parents[1] / "dxf_to_machine_file.py"),
                    str(dxf_dir), "cli-symlink",
                ],
                text=True,
                capture_output=True,
                check=False,
            )

            self.assertEqual(completed.returncode, 0, completed.stderr)
            self.assertIn("层数: 1", completed.stdout)

    def test_cli_repeatable_layer_dxf_uses_only_explicit_manifest(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            dxf_dir = root / "dxfs"
            dxf_dir.mkdir()
            historical = dxf_dir / "layer_1_old.dxf"
            first = dxf_dir / "layer_01_current.dxf"
            second = dxf_dir / "layer_02_current.dxf"
            write_dxf(historical, [(90, 90, 0, 91, 91, 0)])
            write_dxf(first, [(1, 2, 0, 3, 4, 0)])
            write_dxf(second, [(5, 6, 0, 7, 8, 0)])

            completed = subprocess.run(
                [
                    sys.executable,
                    str(Path(__file__).parents[1] / "dxf_to_machine_file.py"),
                    str(dxf_dir), "cli-manifest",
                    "--layer-dxf", str(second),
                    "--layer-dxf", str(first),
                ],
                text=True,
                capture_output=True,
                check=False,
            )

            self.assertEqual(completed.returncode, 0, completed.stderr)
            self.assertIn("层数: 2", completed.stdout)
            self.assertEqual(
                sorted(path.name for path in (root / "cli-manifest" / "patches").iterdir()),
                ["0_0.npy", "1_0.npy"],
            )

    def test_cli_maps_all_first_group_flags_booleans_and_custom_step(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"; dxf_dir.mkdir()
            write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 3, 4, 5, 6)])
            write_dxf(dxf_dir / "layer_2_b.dxf", [(2, 3, 4, 5, 6, 7), (3, 4, 5, 6, 7, 8)])
            completed = subprocess.run(
                [sys.executable, str(Path(__file__).parents[1] / "dxf_to_machine_file.py"),
                 str(dxf_dir), "cli-job",
                 "--owner-token", "cli_owner-01",
                 "--layer-step-um", "5.0",
                 "--power", "41",
                 "--frequency", "351",
                 "--pulse-width-idx", "4",
                 "--scan-speed", "2101",
                 "--jump-vel", "6001",
                 "--jump-delay", "51",
                 "--acc-scale", "52",
                 "--corner-scale", "102",
                 "--end-scale", "103",
                 "--time-lag", "104",
                 "--laser-on-shift", "19",
                 "--delaseroff", "33",
                 "--delaseron", "1",
                 "--no-scan-ahead",
                 "--no-sky-writing"],
                text=True, capture_output=True, check=False,
            )
            self.assertEqual(completed.returncode, 0, completed.stderr)
            self.assertIn("加工文件生成完成", completed.stdout)
            self.assertIn("层数: 2", completed.stdout)
            self.assertIn("补丁数: 2", completed.stdout)
            self.assertIn("线段总数: 3", completed.stdout)
            self.assertIn("Z 范围: -0.005000 ～ 0.000000 mm", completed.stdout)
            self.assertIn(f"输出目录: {(root / 'cli-job').absolute()}", completed.stdout)
            document = json.loads((root / "cli-job" / "machine.json").read_text(encoding="utf-8"))
            self.assertEqual(document["laser_params"][0], {
                "power": 41,
                "frequency": 351,
                "pulseWidthIdx": 4,
                "scanSpeed": 2101,
                "jump_vel": 6001,
                "jump_delay": 51,
                "scan_ahead": False,
                "accScale": 52,
                "cornerScale": 102,
                "endScale": 103,
                "sky_writing": False,
                "timeLag": 104,
                "laserOnShift": 19,
                "delaseroff": 33,
                "delaseron": 1,
            })
            self.assertEqual(
                [cycle["galvo_0"][1] for cycle in document["machine_cycle"]],
                [
                    "G91G00X0.000Y0.000Z0.000F40",
                    "G91G00X0.000Y0.000Z-0.005F40G90",
                ],
            )
            second_patch = np.load(root / "cli-job" / "patches" / "1_0.npy", allow_pickle=False)
            np.testing.assert_array_equal(
                second_patch[:, [2, 5]],
                np.array([[-0.005, -0.005], [-0.005, -0.005]], dtype="<f4"),
            )

    def test_cli_rejects_out_of_range_or_fractional_step_losslessly(self) -> None:
        for requested_step in (
            "100001",
            "9007199254740993",
            "3.0000000000000001",
            "1e1000000",
        ):
            with self.subTest(requested_step=requested_step), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                dxf_dir = root / "dxfs"
                dxf_dir.mkdir()
                write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 3, 4, 5, 6)])

                completed = subprocess.run(
                    [
                        sys.executable,
                        str(Path(__file__).parents[1] / "dxf_to_machine_file.py"),
                        str(dxf_dir),
                        "cli-invalid-step",
                        "--layer-step-um",
                        requested_step,
                    ],
                    text=True,
                    capture_output=True,
                    check=False,
                    timeout=2,
                )

                self.assertNotEqual(completed.returncode, 0)
                self.assertFalse(os.path.lexists(root / "cli-invalid-step"))
                self.assertFalse(os.path.lexists(root / ".cli-invalid-step.building"))
                self.assertFalse(os.path.lexists(root / ".cli-invalid-step.lock"))

    def test_cli_accepts_maximum_whole_micrometre_step(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"
            dxf_dir.mkdir()
            write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 3, 4, 5, 6)])
            write_dxf(dxf_dir / "layer_2_a.dxf", [(1, 2, 3, 4, 5, 6)])

            completed = subprocess.run(
                [
                    sys.executable,
                    str(Path(__file__).parents[1] / "dxf_to_machine_file.py"),
                    str(dxf_dir),
                    "cli-max-step",
                    "--layer-step-um",
                    "100000",
                ],
                text=True,
                capture_output=True,
                check=False,
            )

            self.assertEqual(completed.returncode, 0, completed.stderr)
            patch = np.load(
                root / "cli-max-step" / "patches" / "1_0.npy",
                allow_pickle=False,
            )
            self.assertEqual(patch[0, 2], np.float32(-100.0))

    def test_cli_generates_block_local_package_and_reports_layer_and_patch_counts(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"
            dxf_dir.mkdir()
            write_two_layer_block_fixture(dxf_dir)

            completed = subprocess.run(
                [
                    sys.executable,
                    str(Path(__file__).parents[1] / "dxf_to_machine_file.py"),
                    str(dxf_dir),
                    "cli-block-job",
                    "--layer-step-um", "6",
                    "--block-center-positioning",
                ],
                text=True,
                capture_output=True,
                check=False,
            )

            self.assertEqual(completed.returncode, 0, completed.stderr)
            self.assertIn("层数: 2", completed.stdout)
            self.assertIn("补丁数: 4", completed.stdout)
            self.assertEqual(
                sorted(path.name for path in (root / "cli-block-job" / "patches").iterdir()),
                ["0_0.npy", "1_0.npy", "2_0.npy", "3_0.npy"],
            )

    def test_cli_missing_block_metadata_fails_without_publishing(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            dxf_dir = root / "dxfs"
            dxf_dir.mkdir()
            write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 0, 3, 4, 0)])

            completed = subprocess.run(
                [
                    sys.executable,
                    str(Path(__file__).parents[1] / "dxf_to_machine_file.py"),
                    str(dxf_dir),
                    "cli-block-job",
                    "--block-center-positioning",
                ],
                text=True,
                capture_output=True,
                check=False,
            )

            self.assertNotEqual(completed.returncode, 0)
            self.assertFalse(os.path.lexists(root / "cli-block-job"))
            self.assertFalse(os.path.lexists(root / ".cli-block-job.building"))
            self.assertFalse(os.path.lexists(root / ".cli-block-job.lock"))


if __name__ == "__main__":
    unittest.main()
