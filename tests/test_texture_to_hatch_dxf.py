from pathlib import Path
from contextlib import redirect_stdout
import io
import json
import math
import os
import stat
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

    def assert_only_bounded_private_stage_remains(self, root: Path) -> None:
        entries = list(root.iterdir())
        self.assertEqual(len(entries), 1)
        self.assertTrue(entries[0].is_dir())
        self.assertTrue(entries[0].name.endswith(".staging"))
        self.assertEqual(stat.S_IMODE(os.stat(entries[0]).st_mode), 0o700)

    @staticmethod
    def staged_path(created: object) -> Path:
        if isinstance(created, Path):
            return created
        if isinstance(created, tuple):
            return created[0]
        return created.path  # type: ignore[attr-defined,no-any-return]

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

    def test_metadata_write_failure_leaves_only_bounded_private_stage(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with mock.patch.object(
                hatch,
                "write_block_metadata",
                side_effect=OSError("injected metadata write failure"),
            ):
                with self.assertRaisesRegex(OSError, "injected metadata write failure"):
                    self.export_blocked_pair(root)

            self.assert_only_bounded_private_stage_remains(root)

    def test_pair_validation_failure_leaves_only_bounded_private_stage(self) -> None:
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

            self.assert_only_bounded_private_stage_remains(root)

    def test_pair_validator_rejects_unowned_paths(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output, metadata = self.export_blocked_pair(Path(directory))
            document = json.loads(metadata.read_text(encoding="utf-8"))

            with self.assertRaises(ValueError):
                hatch.validate_hatch_output_pair(output, metadata, document)

    def test_base_exception_while_opening_validation_reader_closes_duplicate(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            duplicated_readers: list[int] = []
            real_fdopen = os.fdopen

            def interrupting_fdopen(
                descriptor: int,
                mode: str = "r",
                *args: object,
                **kwargs: object,
            ) -> object:
                if mode == "r":
                    duplicated_readers.append(descriptor)
                    raise KeyboardInterrupt("injected reader wrapper interruption")
                return real_fdopen(descriptor, mode, *args, **kwargs)

            with mock.patch.object(hatch.os, "fdopen", side_effect=interrupting_fdopen):
                with self.assertRaisesRegex(
                    KeyboardInterrupt,
                    "injected reader wrapper interruption",
                ):
                    self.export_blocked_pair(Path(directory))

            self.assertEqual(len(duplicated_readers), 1)
            with self.assertRaises(OSError):
                os.fstat(duplicated_readers[0])

    def test_second_publication_failure_rolls_back_both_owned_outputs(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            publication_count = 0
            real_publish = hatch._publish_file_no_replace

            def publish_then_fail(source: object, destination: Path) -> object:
                nonlocal publication_count
                publication_count += 1
                if publication_count == 2:
                    raise OSError("injected metadata publication failure")
                return real_publish(source, destination)  # type: ignore[arg-type]

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
            self.assert_only_bounded_private_stage_remains(root)

    def test_successful_pair_is_consistent_with_one_bounded_private_stage(self) -> None:
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
                sorted(path.name for path in root.iterdir() if path.is_file()),
                ["layer_01.blocks.json", "layer_01.dxf"],
            )
            self.assertEqual(len([path for path in root.iterdir() if path.is_dir()]), 1)
            staging_directory = next(path for path in root.iterdir() if path.is_dir())
            staged_entries = sorted(staging_directory.iterdir(), key=lambda path: path.name)
            self.assertEqual(
                [path.name for path in staged_entries],
                ["layer_01.blocks.json", "layer_01.dxf"],
            )
            for staged, published in zip(staged_entries, (metadata, output)):
                self.assertEqual(
                    (os.stat(staged).st_dev, os.stat(staged).st_ino),
                    (os.stat(published).st_dev, os.stat(published).st_ino),
                )

    def test_preexisting_pair_is_preserved_without_starting_staging(self) -> None:
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
                sorted(path.name for path in root.iterdir() if path.is_file()),
                ["layer_01.blocks.json", "layer_01.dxf"],
            )
            self.assertEqual(len([path for path in root.iterdir() if path.is_dir()]), 0)

    def test_pair_stages_inside_a_private_mode_0700_directory(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            real_header = hatch.write_dxf_header
            observed_modes: list[int] = []

            def inspect_private_staging_directory(*args: object, **kwargs: object) -> None:
                staging_directories = [path for path in root.iterdir() if path.is_dir()]
                self.assertEqual(len(staging_directories), 1)
                observed_modes.append(
                    os.stat(staging_directories[0], follow_symlinks=False).st_mode & 0o777
                )
                real_header(*args, **kwargs)

            with mock.patch.object(
                hatch,
                "write_dxf_header",
                side_effect=inspect_private_staging_directory,
            ):
                self.export_blocked_pair(root)

            self.assertEqual(observed_modes, [0o700])

    def test_complete_artifact_is_published_in_one_no_replace_hard_link(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            real_link = os.link
            observed_publications: list[str] = []

            def inspect_before_atomic_publication(
                source_name: str,
                destination_name: str,
                *,
                src_dir_fd: object = None,
                dst_dir_fd: object = None,
                follow_symlinks: bool = True,
            ) -> None:
                if destination_name in {output.name, block_metadata_path(output).name}:
                    self.assertIsNotNone(src_dir_fd)
                    self.assertIsNotNone(dst_dir_fd)
                    self.assertIsNone(
                        hatch._entry_identity(
                            dst_dir_fd,  # type: ignore[arg-type]
                            destination_name,
                        )
                    )
                    source_stat = os.stat(
                        source_name,
                        dir_fd=src_dir_fd,  # type: ignore[arg-type]
                        follow_symlinks=False,
                    )
                    self.assertTrue(stat.S_ISREG(source_stat.st_mode))
                    self.assertGreater(source_stat.st_size, 0)
                    self.assertEqual(
                        stat.S_IMODE(os.fstat(src_dir_fd).st_mode),  # type: ignore[arg-type]
                        0o500,
                    )
                    observed_publications.append(destination_name)
                real_link(
                    source_name,
                    destination_name,
                    src_dir_fd=src_dir_fd,  # type: ignore[arg-type]
                    dst_dir_fd=dst_dir_fd,  # type: ignore[arg-type]
                    follow_symlinks=follow_symlinks,
                )

            with mock.patch.object(
                hatch.os,
                "link",
                side_effect=inspect_before_atomic_publication,
            ):
                generated_output, generated_metadata = self.export_blocked_pair(root)

            self.assertEqual(
                observed_publications,
                [output.name, block_metadata_path(output).name],
            )
            self.assertGreater(generated_output.stat().st_size, 0)
            self.assertGreater(generated_metadata.stat().st_size, 0)

    def test_cleanup_never_rmdirs_a_re_resolved_staging_path(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with mock.patch.object(
                hatch.os,
                "rmdir",
                side_effect=AssertionError("unsafe pathname rmdir attempted"),
            ):
                output, metadata = self.export_blocked_pair(root)

            self.assertTrue(output.is_file())
            self.assertTrue(metadata.is_file())
            staging_directories = [path for path in root.iterdir() if path.is_dir()]
            self.assertEqual(len(staging_directories), 1)
            self.assertEqual(
                stat.S_IMODE(os.stat(staging_directories[0]).st_mode),
                0o700,
            )

    def test_staging_directory_swap_cannot_modify_foreign_directory(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            foreign = root / "foreign-directory"
            foreign.mkdir(mode=0o755)
            sentinel = foreign / "sentinel"
            sentinel.write_bytes(b"keep")
            real_mkdtemp = tempfile.mkdtemp

            def create_then_swap(*args: object, **kwargs: object) -> str:
                staging_path = Path(real_mkdtemp(*args, **kwargs))
                staging_path.rmdir()
                staging_path.symlink_to(foreign, target_is_directory=True)
                return str(staging_path)

            with mock.patch.object(
                hatch.tempfile,
                "mkdtemp",
                side_effect=create_then_swap,
            ):
                with self.assertRaises((OSError, ValueError)):
                    self.export_blocked_pair(root)

            self.assertEqual(stat.S_IMODE(os.stat(foreign).st_mode), 0o755)
            self.assertEqual(sentinel.read_bytes(), b"keep")
            self.assertFalse(os.path.lexists(output))
            self.assertFalse(os.path.lexists(metadata))

    def test_path_swap_before_dxf_write_cannot_truncate_foreign_file(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            foreign = root / "foreign-sentinel.dxf"
            sentinel = b"foreign dxf sentinel"
            foreign.write_bytes(sentinel)
            real_create = hatch._create_owned_temporary_file

            def create_then_swap(*args: object, **kwargs: object) -> object:
                created = real_create(*args, **kwargs)
                final_path = next(
                    argument for argument in args if isinstance(argument, Path)
                )
                if final_path == output:
                    staged = self.staged_path(created)
                    staged.unlink()
                    staged.symlink_to(foreign)
                return created

            with mock.patch.object(
                hatch,
                "_create_owned_temporary_file",
                side_effect=create_then_swap,
            ):
                with self.assertRaises((OSError, ValueError)):
                    self.export_blocked_pair(root)

            self.assertEqual(foreign.read_bytes(), sentinel)
            self.assertFalse(os.path.lexists(output))
            self.assertFalse(os.path.lexists(metadata))

    def test_swap_immediately_before_publication_cannot_publish_foreign_file(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            foreign = root / "foreign-sentinel.dxf"
            sentinel = b"foreign dxf sentinel"
            foreign.write_bytes(sentinel)
            real_publish = hatch._publish_file_no_replace

            def swap_then_publish(*args: object, **kwargs: object) -> None:
                source = self.staged_path(args[0])
                destination = args[1]
                if destination == output:
                    source.unlink()
                    os.link(foreign, source, follow_symlinks=False)
                real_publish(*args, **kwargs)

            with mock.patch.object(
                hatch,
                "_publish_file_no_replace",
                side_effect=swap_then_publish,
            ):
                with self.assertRaises((OSError, ValueError)):
                    self.export_blocked_pair(root)

            self.assertEqual(foreign.read_bytes(), sentinel)
            self.assertFalse(os.path.lexists(output))
            self.assertFalse(os.path.lexists(metadata))

    def test_source_swap_inside_publication_call_cannot_leave_foreign_output(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            foreign = root / "foreign-sentinel.dxf"
            sentinel = b"foreign dxf sentinel"
            foreign.write_bytes(sentinel)
            real_link = os.link
            injected = False

            def attempt_swap_inside_locked_link(
                source_name: str,
                destination_name: str,
                *,
                src_dir_fd: object = None,
                dst_dir_fd: object = None,
                follow_symlinks: bool = True,
            ) -> None:
                nonlocal injected
                if not injected and destination_name == output.name:
                    injected = True
                    self.assertEqual(
                        stat.S_IMODE(os.fstat(src_dir_fd).st_mode),  # type: ignore[arg-type]
                        0o500,
                    )
                    with self.assertRaises(PermissionError):
                        os.unlink(source_name, dir_fd=src_dir_fd)  # type: ignore[arg-type]
                real_link(
                    source_name,
                    destination_name,
                    src_dir_fd=src_dir_fd,  # type: ignore[arg-type]
                    dst_dir_fd=dst_dir_fd,  # type: ignore[arg-type]
                    follow_symlinks=follow_symlinks,
                )

            with mock.patch.object(
                hatch.os,
                "link",
                side_effect=attempt_swap_inside_locked_link,
            ):
                self.export_blocked_pair(root)

            self.assertTrue(injected)
            self.assertEqual(foreign.read_bytes(), sentinel)
            self.assertTrue(output.is_file())
            self.assertNotEqual(os.stat(output).st_ino, os.stat(foreign).st_ino)
            self.assertTrue(metadata.is_file())

    def test_rename_preserving_source_swap_cannot_publish_foreign_file(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            foreign = root / "foreign-sentinel.dxf"
            sentinel = b"foreign rename-preserving sentinel"
            foreign.write_bytes(sentinel)
            real_link = os.link
            injected = False

            def attempt_rename_inside_locked_link(
                source_name: str,
                destination_name: str,
                *,
                src_dir_fd: object = None,
                dst_dir_fd: object = None,
                follow_symlinks: bool = True,
            ) -> None:
                nonlocal injected
                if not injected and destination_name == output.name:
                    injected = True
                    self.assertEqual(
                        stat.S_IMODE(os.fstat(src_dir_fd).st_mode),  # type: ignore[arg-type]
                        0o500,
                    )
                    with self.assertRaises(PermissionError):
                        os.rename(
                            source_name,
                            f"{source_name}.displaced-owned",
                            src_dir_fd=src_dir_fd,  # type: ignore[arg-type]
                            dst_dir_fd=src_dir_fd,  # type: ignore[arg-type]
                        )
                real_link(
                    source_name,
                    destination_name,
                    src_dir_fd=src_dir_fd,  # type: ignore[arg-type]
                    dst_dir_fd=dst_dir_fd,  # type: ignore[arg-type]
                    follow_symlinks=follow_symlinks,
                )

            with mock.patch.object(
                hatch.os,
                "link",
                side_effect=attempt_rename_inside_locked_link,
            ):
                self.export_blocked_pair(root)

            self.assertTrue(injected)
            self.assertEqual(foreign.read_bytes(), sentinel)
            self.assertTrue(output.is_file())
            self.assertNotEqual(os.stat(output).st_ino, os.stat(foreign).st_ino)
            self.assertTrue(metadata.is_file())

    def test_immediate_foreign_replacement_remains_at_its_public_path(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            foreign = root / "foreign-sentinel.dxf"
            sentinel = b"stable immediate replacement sentinel"
            foreign.write_bytes(sentinel)
            displaced_owned = root / "displaced-owned-output.dxf"
            real_open = os.open
            injected = False

            def replace_before_verification_open(
                path: object,
                flags: int,
                mode: int = 0o777,
                *,
                dir_fd: object = None,
            ) -> int:
                nonlocal injected
                if (
                    not injected
                    and path == output.name
                    and dir_fd is not None
                    and os.path.lexists(output)
                ):
                    injected = True
                    output.rename(displaced_owned)
                    foreign.rename(output)
                if dir_fd is None:
                    return real_open(path, flags, mode)
                return real_open(path, flags, mode, dir_fd=dir_fd)  # type: ignore[arg-type]

            with mock.patch.object(
                hatch.os,
                "open",
                side_effect=replace_before_verification_open,
            ):
                with self.assertRaises((OSError, ValueError)):
                    self.export_blocked_pair(root)

            self.assertTrue(injected)
            self.assertFalse(os.path.lexists(foreign))
            self.assertEqual(output.read_bytes(), sentinel)
            self.assertFalse(os.path.lexists(metadata))

    def test_foreign_replacement_between_pair_publications_is_not_accepted(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            foreign = root / "foreign-sentinel.dxf"
            sentinel = b"foreign between-publications sentinel"
            foreign.write_bytes(sentinel)
            displaced_owned = root / "displaced-owned-output.dxf"
            real_publish = hatch._publish_file_no_replace
            publication_count = 0

            def replace_after_first_publish(source: object, destination: Path) -> object:
                nonlocal publication_count
                published = real_publish(source, destination)  # type: ignore[arg-type]
                publication_count += 1
                if publication_count == 1:
                    output.rename(displaced_owned)
                    foreign.rename(output)
                return published

            with mock.patch.object(
                hatch,
                "_publish_file_no_replace",
                side_effect=replace_after_first_publish,
            ):
                with self.assertRaises((OSError, ValueError)):
                    self.export_blocked_pair(root)

            self.assertEqual(publication_count, 2)
            self.assertFalse(os.path.lexists(foreign))
            self.assertEqual(output.read_bytes(), sentinel)
            self.assertFalse(os.path.lexists(metadata))
            self.assertTrue(displaced_owned.is_file())

    def test_same_size_mutation_through_retained_descriptor_is_not_published(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            real_create = hatch._create_owned_temporary_file
            real_link = os.link
            retained_dxf: object = None
            mutated = False

            def capture_staged_file(*args: object, **kwargs: object) -> object:
                nonlocal retained_dxf
                created = real_create(*args, **kwargs)
                if created.name == output.name:
                    retained_dxf = created
                return created

            def mutate_then_link(
                source_name: str,
                destination_name: str,
                *,
                src_dir_fd: object = None,
                dst_dir_fd: object = None,
                follow_symlinks: bool = True,
            ) -> None:
                nonlocal mutated
                if not mutated and destination_name == output.name:
                    self.assertIsNotNone(retained_dxf)
                    descriptor = retained_dxf.descriptor  # type: ignore[attr-defined]
                    original_size = os.fstat(descriptor).st_size
                    os.pwrite(descriptor, b"9", 0)
                    self.assertEqual(os.fstat(descriptor).st_size, original_size)
                    mutated = True
                real_link(
                    source_name,
                    destination_name,
                    src_dir_fd=src_dir_fd,  # type: ignore[arg-type]
                    dst_dir_fd=dst_dir_fd,  # type: ignore[arg-type]
                    follow_symlinks=follow_symlinks,
                )

            with (
                mock.patch.object(
                    hatch,
                    "_create_owned_temporary_file",
                    side_effect=capture_staged_file,
                ),
                mock.patch.object(hatch.os, "link", side_effect=mutate_then_link),
            ):
                with self.assertRaises((OSError, ValueError)):
                    self.export_blocked_pair(root)

            self.assertTrue(mutated)
            self.assertFalse(os.path.lexists(output))
            self.assertFalse(os.path.lexists(metadata))

    def test_same_size_mutation_before_seal_is_rejected_by_pair_validation(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            real_seal = hatch._seal_owned_staged_file
            mutated = False

            def mutate_then_seal(staged_file: object) -> object:
                nonlocal mutated
                if not mutated and staged_file.name == output.name:  # type: ignore[attr-defined]
                    descriptor = staged_file.descriptor  # type: ignore[attr-defined]
                    content = os.pread(descriptor, os.fstat(descriptor).st_size, 0)
                    line_marker = content.find(b"0\nLINE\n")
                    self.assertGreaterEqual(line_marker, 0)
                    os.pwrite(descriptor, b"9", line_marker)
                    mutated = True
                return real_seal(staged_file)  # type: ignore[arg-type]

            with mock.patch.object(
                hatch,
                "_seal_owned_staged_file",
                side_effect=mutate_then_seal,
            ):
                with self.assertRaises((OSError, ValueError)):
                    self.export_blocked_pair(root)

            self.assertTrue(mutated)
            self.assertFalse(os.path.lexists(output))
            self.assertFalse(os.path.lexists(metadata))

    def test_structurally_valid_coordinate_mutation_before_seal_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            real_seal = hatch._seal_owned_staged_file
            mutated = False

            def mutate_then_seal(staged_file: object) -> object:
                nonlocal mutated
                if not mutated and staged_file.name == output.name:  # type: ignore[attr-defined]
                    descriptor = staged_file.descriptor  # type: ignore[attr-defined]
                    content = os.pread(descriptor, os.fstat(descriptor).st_size, 0)
                    coordinate_offset = content.find(b"-5.000000")
                    self.assertGreaterEqual(coordinate_offset, 0)
                    os.pwrite(descriptor, b"-4.000000", coordinate_offset)
                    mutated = True
                return real_seal(staged_file)  # type: ignore[arg-type]

            with mock.patch.object(
                hatch,
                "_seal_owned_staged_file",
                side_effect=mutate_then_seal,
            ):
                with self.assertRaises((OSError, ValueError)):
                    self.export_blocked_pair(root)

            self.assertTrue(mutated)
            self.assertFalse(os.path.lexists(output))
            self.assertFalse(os.path.lexists(metadata))

    def test_keyboard_interrupt_after_first_namespace_publication_rolls_back(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            real_link = os.link
            interrupted = False
            duplicated_descriptors: list[int] = []

            def interrupt_after_namespace_change(
                source_name: str,
                destination_name: str,
                *,
                src_dir_fd: object = None,
                dst_dir_fd: object = None,
                follow_symlinks: bool = True,
            ) -> None:
                nonlocal interrupted
                real_link(
                    source_name,
                    destination_name,
                    src_dir_fd=src_dir_fd,  # type: ignore[arg-type]
                    dst_dir_fd=dst_dir_fd,  # type: ignore[arg-type]
                    follow_symlinks=follow_symlinks,
                )
                if not interrupted and destination_name == output.name:
                    interrupted = True
                    raise KeyboardInterrupt("injected post-publication interrupt")

            real_dup = os.dup

            def record_duplicate(descriptor: int) -> int:
                duplicate = real_dup(descriptor)
                duplicated_descriptors.append(duplicate)
                return duplicate

            with (
                mock.patch.object(
                    hatch.os,
                    "link",
                    side_effect=interrupt_after_namespace_change,
                ),
                mock.patch.object(hatch.os, "dup", side_effect=record_duplicate),
            ):
                with self.assertRaisesRegex(
                    KeyboardInterrupt,
                    "injected post-publication interrupt",
                ):
                    self.export_blocked_pair(root)

            self.assertTrue(interrupted)
            self.assertFalse(os.path.lexists(output))
            self.assertFalse(os.path.lexists(metadata))
            self.assertGreaterEqual(len(duplicated_descriptors), 1)
            for descriptor in duplicated_descriptors:
                with self.subTest(descriptor=descriptor):
                    with self.assertRaises(OSError):
                        os.fstat(descriptor)

    def test_keyboard_interrupt_before_second_publication_rolls_back_first(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            real_publish = hatch._publish_file_no_replace
            publication_count = 0

            def publish_then_interrupt(source: object, destination: Path) -> object:
                nonlocal publication_count
                publication_count += 1
                if publication_count == 2:
                    raise KeyboardInterrupt("injected second-publication interrupt")
                return real_publish(source, destination)  # type: ignore[arg-type]

            with mock.patch.object(
                hatch,
                "_publish_file_no_replace",
                side_effect=publish_then_interrupt,
            ):
                with self.assertRaisesRegex(
                    KeyboardInterrupt,
                    "injected second-publication interrupt",
                ):
                    self.export_blocked_pair(root)

            self.assertEqual(publication_count, 2)
            self.assertFalse(os.path.lexists(output))
            self.assertFalse(os.path.lexists(metadata))

    def test_base_exception_after_publication_return_is_registered_for_cleanup(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            real_publish = hatch._publish_file_no_replace
            captured_descriptors: list[int] = []

            def publish_then_interrupt(source: object, destination: Path) -> object:
                published = real_publish(source, destination)  # type: ignore[arg-type]
                captured_descriptors.append(published.directory_descriptor)
                raise KeyboardInterrupt("injected publication return interruption")

            with mock.patch.object(
                hatch,
                "_publish_file_no_replace",
                side_effect=publish_then_interrupt,
            ):
                with self.assertRaisesRegex(
                    KeyboardInterrupt,
                    "injected publication return interruption",
                ):
                    self.export_blocked_pair(root)

            self.assertFalse(os.path.lexists(output))
            self.assertFalse(os.path.lexists(metadata))
            self.assertEqual(len(captured_descriptors), 1)
            with self.assertRaises(OSError):
                os.fstat(captured_descriptors[0])

    def test_parent_replacement_cannot_split_a_successful_pair(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output_parent = root / "out"
            output_parent.mkdir()
            moved_parent = root / "out-moved"
            sentinel_bytes = b"new parent sentinel"
            real_publish = hatch._publish_file_no_replace
            publication_count = 0

            def replace_parent_after_first_publish(
                source: object,
                destination: Path,
            ) -> object:
                nonlocal publication_count
                published = real_publish(source, destination)  # type: ignore[arg-type]
                publication_count += 1
                if publication_count == 1:
                    output_parent.rename(moved_parent)
                    output_parent.mkdir()
                    (output_parent / "sentinel").write_bytes(sentinel_bytes)
                    old_stage = next(moved_parent.glob(".*.staging"))
                    # Simulate a same-owner adversary deliberately defeating the
                    # mode lock before moving the retained staging inode.
                    old_stage.chmod(0o700)
                    old_stage.rename(output_parent / old_stage.name)
                return published

            with mock.patch.object(
                hatch,
                "_publish_file_no_replace",
                side_effect=replace_parent_after_first_publish,
            ):
                with self.assertRaises((OSError, ValueError)):
                    self.export_blocked_pair(output_parent)

            output = output_parent / "layer_01.dxf"
            metadata = block_metadata_path(output)
            moved_output = moved_parent / output.name
            moved_metadata = moved_parent / metadata.name
            self.assertEqual(publication_count, 1)
            self.assertEqual((output_parent / "sentinel").read_bytes(), sentinel_bytes)
            self.assertFalse(os.path.lexists(output))
            self.assertFalse(os.path.lexists(metadata))
            self.assertFalse(os.path.lexists(moved_output))
            self.assertFalse(os.path.lexists(moved_metadata))

    def test_parent_replacement_after_mode_restore_cannot_report_success(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output_parent = root / "out"
            output_parent.mkdir()
            moved_parent = root / "out-moved"
            sentinel_bytes = b"late replacement parent sentinel"
            real_restore = hatch._restore_owned_staged_file_mode
            restore_count = 0

            def replace_parent_after_second_restore(staged_file: object) -> None:
                nonlocal restore_count
                real_restore(staged_file)  # type: ignore[arg-type]
                restore_count += 1
                if restore_count == 2:
                    output_parent.rename(moved_parent)
                    output_parent.mkdir()
                    (output_parent / "sentinel").write_bytes(sentinel_bytes)

            with mock.patch.object(
                hatch,
                "_restore_owned_staged_file_mode",
                side_effect=replace_parent_after_second_restore,
            ):
                with self.assertRaises((OSError, ValueError)):
                    self.export_blocked_pair(output_parent)

            output = output_parent / "layer_01.dxf"
            metadata = block_metadata_path(output)
            self.assertEqual(restore_count, 2)
            self.assertEqual((output_parent / "sentinel").read_bytes(), sentinel_bytes)
            self.assertFalse(os.path.lexists(output))
            self.assertFalse(os.path.lexists(metadata))
            self.assertFalse(os.path.lexists(moved_parent / output.name))
            self.assertFalse(os.path.lexists(moved_parent / metadata.name))

    def test_mode_change_after_restore_cannot_report_success(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            real_restore = hatch._restore_owned_staged_file_mode
            restore_count = 0

            def restore_then_change_mode(staged_file: object) -> None:
                nonlocal restore_count
                real_restore(staged_file)  # type: ignore[arg-type]
                restore_count += 1
                if restore_count == 2:
                    os.fchmod(staged_file.descriptor, 0o666)  # type: ignore[attr-defined]

            with mock.patch.object(
                hatch,
                "_restore_owned_staged_file_mode",
                side_effect=restore_then_change_mode,
            ):
                with self.assertRaises((OSError, ValueError)):
                    self.export_blocked_pair(root)

            self.assertEqual(restore_count, 2)
            self.assertFalse(os.path.lexists(output))
            self.assertFalse(os.path.lexists(metadata))

    def test_base_exception_during_directory_acquisition_closes_descriptor(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            opened_descriptors: list[int] = []
            real_open = os.open

            def record_open(*args: object, **kwargs: object) -> int:
                descriptor = real_open(*args, **kwargs)  # type: ignore[arg-type]
                opened_descriptors.append(descriptor)
                return descriptor

            with (
                mock.patch.object(hatch.os, "open", side_effect=record_open),
                mock.patch.object(
                    hatch.os,
                    "fstat",
                    side_effect=KeyboardInterrupt("injected acquisition interrupt"),
                ),
            ):
                with self.assertRaisesRegex(
                    KeyboardInterrupt,
                    "injected acquisition interrupt",
                ):
                    hatch._open_directory_no_follow(root)

            self.assertEqual(len(opened_descriptors), 1)
            with self.assertRaises(OSError):
                os.fstat(opened_descriptors[0])

    def test_base_exception_after_staging_return_closes_registered_descriptors(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            real_create = hatch._create_owned_staging_directory
            captured_descriptors: list[int] = []

            def create_then_interrupt(*args: object, **kwargs: object) -> object:
                created = real_create(*args, **kwargs)
                captured_descriptors.extend(
                    [created.descriptor, created.publication_directory_descriptor]
                )
                raise KeyboardInterrupt("injected staging return interruption")

            with mock.patch.object(
                hatch,
                "_create_owned_staging_directory",
                side_effect=create_then_interrupt,
            ):
                with self.assertRaisesRegex(
                    KeyboardInterrupt,
                    "injected staging return interruption",
                ):
                    self.export_blocked_pair(root)

            self.assertEqual(len(captured_descriptors), 2)
            for descriptor in captured_descriptors:
                with self.subTest(descriptor=descriptor), self.assertRaises(OSError):
                    os.fstat(descriptor)

    def test_base_exception_after_staged_file_return_closes_registered_descriptor(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            real_create = hatch._create_owned_temporary_file
            captured_descriptors: list[int] = []

            def create_then_interrupt(*args: object, **kwargs: object) -> object:
                created = real_create(*args, **kwargs)
                captured_descriptors.append(created.descriptor)
                raise KeyboardInterrupt("injected staged-file return interruption")

            with mock.patch.object(
                hatch,
                "_create_owned_temporary_file",
                side_effect=create_then_interrupt,
            ):
                with self.assertRaisesRegex(
                    KeyboardInterrupt,
                    "injected staged-file return interruption",
                ):
                    self.export_blocked_pair(root)

            self.assertEqual(len(captured_descriptors), 1)
            with self.assertRaises(OSError):
                os.fstat(captured_descriptors[0])

    def test_failed_prepublication_cleanup_never_unlinks_staged_entries(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            unlink_calls: list[tuple[object, object]] = []

            def reject_unlink(
                path: object,
                *,
                dir_fd: object = None,
            ) -> None:
                unlink_calls.append((path, dir_fd))
                raise OSError("cleanup must not unlink a re-resolved entry")

            with (
                mock.patch.object(
                    hatch,
                    "validate_hatch_output_pair",
                    side_effect=ValueError("injected validation failure"),
                ),
                mock.patch.object(hatch.os, "unlink", side_effect=reject_unlink),
            ):
                with self.assertRaisesRegex(ValueError, "injected validation failure"):
                    self.export_blocked_pair(root)

            self.assertEqual(unlink_calls, [])

    def test_destination_replacement_during_publication_check_is_restored(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            foreign = root / "foreign-sentinel.dxf"
            sentinel = b"foreign replacement sentinel"
            foreign.write_bytes(sentinel)
            displaced_owned = root / "owned-output-displaced-by-injection"
            real_stat = os.stat
            injected = False

            def swap_before_post_publication_stat(
                path: object,
                *args: object,
                **kwargs: object,
            ) -> os.stat_result:
                nonlocal injected
                if (
                    not injected
                    and path == output.name
                    and kwargs.get("dir_fd") is not None
                    and os.path.lexists(output)
                ):
                    injected = True
                    output.rename(displaced_owned)
                    foreign.rename(output)
                return real_stat(path, *args, **kwargs)  # type: ignore[arg-type]

            with mock.patch.object(hatch.os, "stat", side_effect=swap_before_post_publication_stat):
                with self.assertRaises((OSError, ValueError)):
                    self.export_blocked_pair(root)

            self.assertTrue(injected)
            self.assertFalse(os.path.lexists(foreign))
            self.assertEqual(output.read_bytes(), sentinel)
            self.assertFalse(os.path.lexists(metadata))

    def test_rollback_swap_preserves_replacement_instead_of_unlinking_it(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            foreign = root / "foreign-sentinel.dxf"
            foreign.write_bytes(b"foreign rollback sentinel")
            displaced_owned = root / "owned-output-displaced-by-injection"
            publication_count = 0
            injected = False
            real_publish = hatch._publish_file_no_replace

            def publish_then_fail(source: object, destination: Path) -> object:
                nonlocal publication_count
                publication_count += 1
                if publication_count == 2:
                    raise OSError("injected metadata publication failure")
                return real_publish(source, destination)  # type: ignore[arg-type]

            real_atomic_rename = hatch._atomic_rename_no_replace

            def swap_inside_atomic_rename(
                source_directory_descriptor: int,
                source_name: str,
                destination_directory_descriptor: int,
                destination_name: str,
            ) -> None:
                nonlocal injected
                if not injected and source_name == output.name and output.exists():
                    injected = True
                    output.rename(displaced_owned)
                    os.link(foreign, output, follow_symlinks=False)
                real_atomic_rename(
                    source_directory_descriptor,
                    source_name,
                    destination_directory_descriptor,
                    destination_name,
                )

            with (
                mock.patch.object(
                    hatch,
                    "_publish_file_no_replace",
                    side_effect=publish_then_fail,
                ),
                mock.patch.object(
                    hatch,
                    "_atomic_rename_no_replace",
                    side_effect=swap_inside_atomic_rename,
                ),
            ):
                with self.assertRaisesRegex(
                    OSError,
                    "injected metadata publication failure",
                ):
                    self.export_blocked_pair(root)

            self.assertTrue(injected)
            self.assertEqual(output.read_bytes(), foreign.read_bytes())
            self.assertEqual(
                (os.stat(output).st_dev, os.stat(output).st_ino),
                (os.stat(foreign).st_dev, os.stat(foreign).st_ino),
            )

    def test_rollback_interrupt_after_swap_restores_foreign_replacement(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            foreign = root / "foreign-sentinel.dxf"
            sentinel = b"foreign rollback-interrupt sentinel"
            foreign.write_bytes(sentinel)
            displaced_owned = root / "owned-output-displaced-by-injection"
            publication_count = 0
            injected = False
            real_publish = hatch._publish_file_no_replace
            real_atomic_rename = hatch._atomic_rename_no_replace

            def publish_then_fail(source: object, destination: Path) -> object:
                nonlocal publication_count
                publication_count += 1
                if publication_count == 2:
                    raise OSError("injected metadata publication failure")
                return real_publish(source, destination)  # type: ignore[arg-type]

            def swap_move_then_interrupt(
                source_directory_descriptor: int,
                source_name: str,
                destination_directory_descriptor: int,
                destination_name: str,
            ) -> None:
                nonlocal injected
                if not injected and source_name == output.name and output.exists():
                    injected = True
                    output.rename(displaced_owned)
                    foreign.rename(output)
                    real_atomic_rename(
                        source_directory_descriptor,
                        source_name,
                        destination_directory_descriptor,
                        destination_name,
                    )
                    raise KeyboardInterrupt("injected post-rollback-move interrupt")
                real_atomic_rename(
                    source_directory_descriptor,
                    source_name,
                    destination_directory_descriptor,
                    destination_name,
                )

            with (
                mock.patch.object(
                    hatch,
                    "_publish_file_no_replace",
                    side_effect=publish_then_fail,
                ),
                mock.patch.object(
                    hatch,
                    "_atomic_rename_no_replace",
                    side_effect=swap_move_then_interrupt,
                ),
            ):
                with self.assertRaisesRegex(
                    OSError,
                    "injected metadata publication failure",
                ):
                    self.export_blocked_pair(root)

            self.assertTrue(injected)
            self.assertFalse(os.path.lexists(foreign))
            self.assertEqual(output.read_bytes(), sentinel)
            self.assertTrue(displaced_owned.is_file())

    def test_failed_publication_does_not_promote_unproven_foreign_quarantine(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            foreign = root / "foreign-sentinel.dxf"
            sentinel = b"unproven foreign quarantine sentinel"
            foreign.write_bytes(sentinel)
            foreign_identity = (os.stat(foreign).st_dev, os.stat(foreign).st_ino)
            quarantine_name = f".{output.name}.published-rollback"
            real_lock = hatch._lock_owned_staging_directory
            real_link = os.link
            seeded = False

            def seed_quarantine_then_lock(staging_directory: object) -> None:
                nonlocal seeded
                if not seeded:
                    real_link(
                        foreign,
                        quarantine_name,
                        dst_dir_fd=staging_directory.descriptor,  # type: ignore[attr-defined]
                        follow_symlinks=False,
                    )
                    seeded = True
                real_lock(staging_directory)  # type: ignore[arg-type]

            with (
                mock.patch.object(
                    hatch,
                    "_lock_owned_staging_directory",
                    side_effect=seed_quarantine_then_lock,
                ),
                mock.patch.object(
                    hatch.os,
                    "link",
                    side_effect=OSError("injected pre-publication link failure"),
                ),
            ):
                with self.assertRaisesRegex(
                    OSError,
                    "injected pre-publication link failure",
                ):
                    self.export_blocked_pair(root)

            self.assertTrue(seeded)
            self.assertFalse(os.path.lexists(output))
            self.assertFalse(os.path.lexists(metadata))
            self.assertEqual(foreign.read_bytes(), sentinel)
            staging_directory = next(path for path in root.iterdir() if path.is_dir())
            quarantined = staging_directory / quarantine_name
            self.assertEqual(quarantined.read_bytes(), sentinel)
            self.assertEqual(
                (os.stat(quarantined).st_dev, os.stat(quarantined).st_ino),
                foreign_identity,
            )
            self.assertLessEqual(len(list(staging_directory.iterdir())), 4)

    def test_relocks_staging_directory_if_file_provider_restores_write_mode(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            real_verify = hatch._verify_owned_publication_directory
            restored_write_mode = False

            def restore_write_mode_after_lock(staging_directory: object) -> object:
                nonlocal restored_write_mode
                result = real_verify(staging_directory)  # type: ignore[arg-type]
                mode = stat.S_IMODE(
                    os.fstat(staging_directory.descriptor).st_mode  # type: ignore[attr-defined]
                )
                if not restored_write_mode and mode == 0o500:
                    os.fchmod(staging_directory.descriptor, 0o700)  # type: ignore[attr-defined]
                    restored_write_mode = True
                return result

            with mock.patch.object(
                hatch,
                "_verify_owned_publication_directory",
                side_effect=restore_write_mode_after_lock,
            ):
                output, metadata = self.export_blocked_pair(root)

            self.assertTrue(restored_write_mode)
            self.assertTrue(output.is_file())
            self.assertTrue(metadata.is_file())

    def test_interrupt_during_foreign_restore_retries_without_losing_replacement(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            foreign = root / "foreign-sentinel.dxf"
            sentinel = b"foreign restore-interrupt sentinel"
            foreign.write_bytes(sentinel)
            foreign_identity = (os.stat(foreign).st_dev, os.stat(foreign).st_ino)
            displaced_owned = root / "owned-output-displaced-by-injection"
            quarantine_name = f".{output.name}.published-rollback"
            publication_count = 0
            swapped = False
            interrupted = False
            captured_descriptors: list[int] = []
            real_create_directory = hatch._create_owned_staging_directory
            real_create_file = hatch._create_owned_temporary_file
            real_publish = hatch._publish_file_no_replace
            real_atomic_rename = hatch._atomic_rename_no_replace

            def record_directory(*args: object, **kwargs: object) -> object:
                created = real_create_directory(*args, **kwargs)
                captured_descriptors.extend(
                    [created.descriptor, created.publication_directory_descriptor]
                )
                return created

            def record_file(*args: object, **kwargs: object) -> object:
                created = real_create_file(*args, **kwargs)
                captured_descriptors.append(created.descriptor)
                return created

            def publish_then_fail(source: object, destination: Path) -> object:
                nonlocal publication_count
                publication_count += 1
                if publication_count == 2:
                    raise OSError("injected metadata publication failure")
                published = real_publish(source, destination)  # type: ignore[arg-type]
                captured_descriptors.append(published.directory_descriptor)
                return published

            def swap_then_interrupt_restore(
                source_directory_descriptor: int,
                source_name: str,
                destination_directory_descriptor: int,
                destination_name: str,
            ) -> None:
                nonlocal swapped, interrupted
                if (
                    not swapped
                    and source_name == output.name
                    and destination_name == quarantine_name
                ):
                    swapped = True
                    output.rename(displaced_owned)
                    foreign.rename(output)
                    real_atomic_rename(
                        source_directory_descriptor,
                        source_name,
                        destination_directory_descriptor,
                        destination_name,
                    )
                    return
                if (
                    swapped
                    and not interrupted
                    and source_name == quarantine_name
                    and destination_name == output.name
                ):
                    interrupted = True
                    raise KeyboardInterrupt("injected foreign-restore interruption")
                real_atomic_rename(
                    source_directory_descriptor,
                    source_name,
                    destination_directory_descriptor,
                    destination_name,
                )

            with (
                mock.patch.object(
                    hatch,
                    "_create_owned_staging_directory",
                    side_effect=record_directory,
                ),
                mock.patch.object(
                    hatch,
                    "_create_owned_temporary_file",
                    side_effect=record_file,
                ),
                mock.patch.object(
                    hatch,
                    "_publish_file_no_replace",
                    side_effect=publish_then_fail,
                ),
                mock.patch.object(
                    hatch,
                    "_atomic_rename_no_replace",
                    side_effect=swap_then_interrupt_restore,
                ),
            ):
                with self.assertRaisesRegex(
                    OSError,
                    "injected metadata publication failure",
                ):
                    self.export_blocked_pair(root)

            self.assertEqual(publication_count, 2)
            self.assertTrue(swapped)
            self.assertTrue(interrupted)
            self.assertFalse(os.path.lexists(foreign))
            self.assertTrue(output.is_file())
            self.assertEqual(output.read_bytes(), sentinel)
            self.assertEqual(
                (os.stat(output).st_dev, os.stat(output).st_ino),
                foreign_identity,
            )
            self.assertFalse(os.path.lexists(metadata))
            self.assertTrue(displaced_owned.is_file())

            staging_directories = [path for path in root.iterdir() if path.is_dir()]
            self.assertEqual(len(staging_directories), 1)
            staged_entries = list(staging_directories[0].iterdir())
            self.assertEqual(len(staged_entries), 2)
            self.assertNotIn(
                foreign_identity,
                {(os.stat(path).st_dev, os.stat(path).st_ino) for path in staged_entries},
            )
            self.assertEqual(len(captured_descriptors), 5)
            for descriptor in captured_descriptors:
                with self.subTest(descriptor=descriptor), self.assertRaises(OSError):
                    os.fstat(descriptor)

    def test_one_shot_interrupt_before_rollback_move_is_retried(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "layer_01.dxf"
            metadata = block_metadata_path(output)
            real_publish = hatch._publish_file_no_replace
            real_atomic_rename = hatch._atomic_rename_no_replace
            publication_count = 0
            interrupted = False

            def publish_then_fail(source: object, destination: Path) -> object:
                nonlocal publication_count
                publication_count += 1
                if publication_count == 2:
                    raise OSError("injected metadata publication failure")
                return real_publish(source, destination)  # type: ignore[arg-type]

            def interrupt_before_first_rollback_move(
                source_directory_descriptor: int,
                source_name: str,
                destination_directory_descriptor: int,
                destination_name: str,
            ) -> None:
                nonlocal interrupted
                if not interrupted and source_name == output.name:
                    interrupted = True
                    raise KeyboardInterrupt("injected pre-rollback-move interrupt")
                real_atomic_rename(
                    source_directory_descriptor,
                    source_name,
                    destination_directory_descriptor,
                    destination_name,
                )

            with (
                mock.patch.object(
                    hatch,
                    "_publish_file_no_replace",
                    side_effect=publish_then_fail,
                ),
                mock.patch.object(
                    hatch,
                    "_atomic_rename_no_replace",
                    side_effect=interrupt_before_first_rollback_move,
                ),
            ):
                with self.assertRaisesRegex(
                    OSError,
                    "injected metadata publication failure",
                ):
                    self.export_blocked_pair(root)

            self.assertTrue(interrupted)
            self.assertFalse(os.path.lexists(output))
            self.assertFalse(os.path.lexists(metadata))

    def test_cleanup_error_preserves_primary_failure_and_closes_all_descriptors(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            opened_descriptors: list[int] = []
            real_create_directory = hatch._create_owned_staging_directory
            real_create_file = hatch._create_owned_temporary_file

            def record_directory(*args: object, **kwargs: object) -> object:
                created = real_create_directory(*args, **kwargs)
                opened_descriptors.extend([
                    created.descriptor,
                    created.publication_directory_descriptor,
                ])
                return created

            def record_file(*args: object, **kwargs: object) -> object:
                created = real_create_file(*args, **kwargs)
                opened_descriptors.append(created.descriptor)
                return created

            with (
                mock.patch.object(
                    hatch,
                    "_create_owned_staging_directory",
                    side_effect=record_directory,
                ),
                mock.patch.object(
                    hatch,
                    "_create_owned_temporary_file",
                    side_effect=record_file,
                ),
                mock.patch.object(
                    hatch,
                    "validate_hatch_output_pair",
                    side_effect=ValueError("injected primary validation failure"),
                ),
                mock.patch.object(
                    hatch,
                    "_rollback_published_file",
                    side_effect=OSError("injected rollback cleanup failure"),
                ),
            ):
                with self.assertRaisesRegex(
                    ValueError,
                    "injected primary validation failure",
                ):
                    self.export_blocked_pair(root)

            self.assertEqual(len(opened_descriptors), 4)
            for descriptor in opened_descriptors:
                with self.subTest(descriptor=descriptor):
                    with self.assertRaises(OSError):
                        os.fstat(descriptor)

    def test_validation_does_not_read_or_publish_swapped_artifact_paths(self) -> None:
        for artifact_label in ("dxf", "metadata"):
            with self.subTest(artifact_label=artifact_label), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                reference_root = root / "reference"
                reference_root.mkdir()
                reference_dxf, reference_metadata = self.export_blocked_pair(reference_root)
                foreign = root / f"foreign-{artifact_label}-sentinel"
                foreign.write_bytes(
                    (reference_dxf if artifact_label == "dxf" else reference_metadata).read_bytes()
                )
                foreign_bytes = foreign.read_bytes()
                output = root / "layer_01.dxf"
                metadata = block_metadata_path(output)
                final_artifact = output if artifact_label == "dxf" else metadata
                target_encoding = "ascii" if artifact_label == "dxf" else "utf-8"
                swapped = False
                real_read_text = Path.read_text

                def swap_before_path_read(
                    path: Path,
                    *args: object,
                    **kwargs: object,
                ) -> str:
                    nonlocal swapped
                    encoding = kwargs.get("encoding")
                    if (
                        not swapped
                        and encoding == target_encoding
                        and path != foreign
                        and reference_root not in path.parents
                        and path != final_artifact
                        and root in path.parents
                    ):
                        path.unlink()
                        os.link(foreign, path, follow_symlinks=False)
                        swapped = True
                    return real_read_text(path, *args, **kwargs)

                with mock.patch.object(Path, "read_text", new=swap_before_path_read):
                    generated_dxf, generated_metadata = self.export_blocked_pair(root)

                self.assertEqual(foreign.read_bytes(), foreign_bytes)
                generated = (
                    generated_dxf if artifact_label == "dxf" else generated_metadata
                )
                self.assertNotEqual(
                    (os.stat(generated).st_dev, os.stat(generated).st_ino),
                    (os.stat(foreign).st_dev, os.stat(foreign).st_ino),
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

    def test_manifest_missing_check_revalidates_expected_paths_directly(self) -> None:
        source = (
            Path(__file__).resolve().parents[1] / "GrayscaleLayersMac" / "MainWindow.cs"
        ).read_text(encoding="utf-8")
        manifest_start = source.index("var pathComparer = StringComparer.OrdinalIgnoreCase")
        manifest_end = source.index(
            'AppendPipelineLog($"已验证本次 DXF 清单', manifest_start
        )
        manifest = source[manifest_start:manifest_end]

        self.assertRegex(
            manifest,
            r"expectedDxfFiles\s+\.Where\(path => !IsRegularNonEmptyFile\(path\)\)",
        )
        self.assertNotIn(
            "expectedDxfFiles\n                .Except(actualDxfFiles",
            manifest,
        )


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
