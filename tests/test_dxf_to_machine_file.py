from pathlib import Path
from datetime import datetime
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
    build_machine_document,
    discover_layer_dxf_files,
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


class MakePatchTests(unittest.TestCase):
    def test_writes_patch_z_in_millimeters_and_preserves_xy(self) -> None:
        lines = np.array([[1.25, 2.5, 99, 3.75, 4.5, 88]], dtype=np.float64)

        patch = make_patch(lines, patch_index=2, layer_step_um=3)

        self.assertEqual(patch.dtype, np.dtype("<f4"))
        self.assertEqual(patch.shape, (1, 6))
        np.testing.assert_array_equal(patch[:, [0, 1, 3, 4]], lines[:, [0, 1, 3, 4]].astype("<f4"))
        np.testing.assert_array_equal(patch[:, [2, 5]], np.array([[-0.006, -0.006]], dtype="<f4"))
        self.assertFalse(np.shares_memory(lines, patch))

    def test_rejects_invalid_line_shape(self) -> None:
        with self.assertRaises(ValueError):
            make_patch(np.zeros((1, 5)), patch_index=0, layer_step_um=1)

    def test_rejects_negative_patch_index(self) -> None:
        with self.assertRaises(ValueError):
            make_patch(np.zeros((1, 6)), patch_index=-1, layer_step_um=1)

    def test_handles_unsigned_numpy_patch_index_without_overflow(self) -> None:
        patch = make_patch(np.zeros((1, 6)), patch_index=np.uint64(2), layer_step_um=3)

        np.testing.assert_array_equal(patch[:, [2, 5]], np.array([[-0.006, -0.006]], dtype="<f4"))

    def test_rejects_invalid_layer_steps(self) -> None:
        lines = np.zeros((1, 6))
        for invalid_step in (0, -1, float("nan"), float("inf")):
            with self.subTest(layer_step_um=invalid_step):
                with self.assertRaises(ValueError):
                    make_patch(lines, patch_index=0, layer_step_um=invalid_step)


class MachineDocumentTests(unittest.TestCase):
    def test_builds_exact_defaults_custom_first_group_and_cycles(self) -> None:
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

        document = build_machine_document(3, 3, custom)

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
            {"galvo_0": [0, "G00X0.000Y0.000Z0.000F40", [0, 0]]},
            {"galvo_0": [0, "G00X0.000Y0.000Z-0.003F40", [1, 0]]},
            {"galvo_0": [0, "G00X0.000Y0.000Z-0.006F40", [2, 0]]},
        ])

    def test_deep_copies_defaults_and_caller_data(self) -> None:
        caller = dict(DEFAULT_LASER_PARAMS[0])
        document = build_machine_document(1, 3, caller)
        document["laser_params"][0]["power"] = 999
        document["laser_params"][1]["power"] = 999
        document["galvo_offset"]["galvo_0"][0] = 999

        self.assertEqual(caller["power"], 38)
        self.assertEqual(DEFAULT_LASER_PARAMS[0]["power"], 38)
        self.assertEqual(DEFAULT_LASER_PARAMS[1]["power"], 10)
        self.assertEqual(DEFAULT_GALVO_OFFSET, {"galvo_0": [0, 0, 0, 0]})

    def test_rejects_invalid_counts_steps_and_first_group_types(self) -> None:
        valid = dict(DEFAULT_LASER_PARAMS[0])
        for count in (0, -1):
            with self.subTest(count=count), self.assertRaises(ValueError):
                build_machine_document(count, 3, valid)
        for step in (0, -1, float("nan"), float("inf"), float("-inf")):
            with self.subTest(step=step), self.assertRaises(ValueError):
                build_machine_document(1, step, valid)
        invalid_groups = []
        missing = dict(valid); missing.pop("power"); invalid_groups.append(missing)
        extra = dict(valid, surprise=1); invalid_groups.append(extra)
        bool_integer = dict(valid, power=True); invalid_groups.append(bool_integer)
        float_integer = dict(valid, power=38.0); invalid_groups.append(float_integer)
        integer_boolean = dict(valid, scan_ahead=1); invalid_groups.append(integer_boolean)
        for group in invalid_groups:
            with self.subTest(group=group), self.assertRaises(ValueError):
                build_machine_document(1, 3, group)


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
        for step in (0, -1, float("nan"), float("inf"), float("-inf")):
            with self.subTest(step=step), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                dxf_dir = root / "dxfs"; dxf_dir.mkdir()
                write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 3, 4, 5, 6)])
                with self.assertRaises(ValueError):
                    generate_machine_file(dxf_dir, "job", step, dict(DEFAULT_LASER_PARAMS[0]))
                self.assertFalse((root / "job").exists())
                self.assertFalse((root / ".job.building").exists())

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

            validate_machine_directory(result, 40, 3, dict(DEFAULT_LASER_PARAMS[0]))
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
                    "G00X0.000Y0.000Z0.000F40",
                    "G00X0.000Y0.000Z-0.005F40",
                    "G00X0.000Y0.000Z-0.010F40",
                ],
            )


class ValidateMachineDirectoryTests(unittest.TestCase):
    def _make_package(self, root: Path) -> Path:
        dxf_dir = root / "dxfs"; dxf_dir.mkdir()
        write_dxf(dxf_dir / "layer_1_a.dxf", [(1, 2, 3, 4, 5, 6)])
        write_dxf(dxf_dir / "layer_2_b.dxf", [(2, 3, 4, 5, 6, 7)])
        return generate_machine_file(dxf_dir, "job", 3, dict(DEFAULT_LASER_PARAMS[0]))

    def test_rejects_bad_patch_dtype(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            package = self._make_package(Path(directory))
            patch_path = package / "patches" / "0_0.npy"
            np.save(patch_path, np.zeros((1, 6), dtype=np.float64))
            with self.assertRaises(ValueError):
                validate_machine_directory(package, 2, 3, dict(DEFAULT_LASER_PARAMS[0]))


    def test_rejects_bad_patch_shape(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            package = self._make_package(Path(directory))
            np.save(package / "patches" / "0_0.npy", np.zeros((0, 6), dtype="<f4"))
            with self.assertRaises(ValueError):
                validate_machine_directory(package, 2, 3, dict(DEFAULT_LASER_PARAMS[0]))


    def test_rejects_bad_patch_z(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            package = self._make_package(Path(directory))
            patch_path = package / "patches" / "1_0.npy"
            patch = np.load(patch_path, allow_pickle=False)
            patch[:, 2] = -0.004
            patch[:, 5] = -0.004
            np.save(patch_path, patch)
            with self.assertRaises(ValueError):
                validate_machine_directory(package, 2, 3, dict(DEFAULT_LASER_PARAMS[0]))

    def test_rejects_bad_cycle_reference(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            package = self._make_package(Path(directory))
            json_path = package / "machine.json"
            document = json.loads(json_path.read_text(encoding="utf-8"))
            document["machine_cycle"][1]["galvo_0"][2] = [0, 0]
            json_path.write_text(json.dumps(document), encoding="utf-8")
            with self.assertRaises(ValueError):
                validate_machine_directory(package, 2, 3, dict(DEFAULT_LASER_PARAMS[0]))

    def test_rejects_bad_cycle_count(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            package = self._make_package(Path(directory))
            json_path = package / "machine.json"
            document = json.loads(json_path.read_text(encoding="utf-8"))
            document["machine_cycle"].pop()
            json_path.write_text(json.dumps(document), encoding="utf-8")
            with self.assertRaises(ValueError):
                validate_machine_directory(package, 2, 3, dict(DEFAULT_LASER_PARAMS[0]))

    def test_rejects_any_extra_file_or_directory_in_patches(self) -> None:
        self.assertIn(
            "expected_first_laser_params",
            inspect.signature(validate_machine_directory).parameters,
        )
        params = dict(DEFAULT_LASER_PARAMS[0])
        for extra_name, is_directory in (("extra.npy", False), ("foreign", True)):
            with self.subTest(extra_name=extra_name), tempfile.TemporaryDirectory() as directory:
                package = self._make_package(Path(directory))
                extra = package / "patches" / extra_name
                if is_directory:
                    extra.mkdir()
                else:
                    extra.write_text("sentinel", encoding="utf-8")

                with self.assertRaises(ValueError):
                    validate_machine_directory(package, 2, 3, params)

                self.assertTrue(os.path.lexists(extra))

    def test_rejects_validly_typed_first_laser_group_that_differs_from_expected(self) -> None:
        self.assertIn(
            "expected_first_laser_params",
            inspect.signature(validate_machine_directory).parameters,
        )
        with tempfile.TemporaryDirectory() as directory:
            params = dict(DEFAULT_LASER_PARAMS[0])
            package = self._make_package(Path(directory))
            json_path = package / "machine.json"
            document = json.loads(json_path.read_text(encoding="utf-8"))
            document["laser_params"][0]["power"] = 999
            json_path.write_text(json.dumps(document), encoding="utf-8")

            with self.assertRaises(ValueError):
                validate_machine_directory(package, 2, 3, params)


class CliTests(unittest.TestCase):
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
                 "--layer-step-um", "5",
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
                    "G00X0.000Y0.000Z0.000F40",
                    "G00X0.000Y0.000Z-0.005F40",
                ],
            )
            second_patch = np.load(root / "cli-job" / "patches" / "1_0.npy", allow_pickle=False)
            np.testing.assert_array_equal(
                second_patch[:, [2, 5]],
                np.array([[-0.005, -0.005], [-0.005, -0.005]], dtype="<f4"),
            )


if __name__ == "__main__":
    unittest.main()
