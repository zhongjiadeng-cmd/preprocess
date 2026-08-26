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
import base64
import ctypes
import errno
import hashlib
import io
import json
import math
import os
import stat
import sys
import tempfile
from dataclasses import dataclass, field
from pathlib import Path
from typing import TextIO

import numpy as np
from PIL import Image


MM_PER_INCH = 25.4
MAX_PREVIEW_PNG_BYTES = 64 * 1024 * 1024


class RepeatPeriodNotFoundError(ValueError):
    """图片在指定方向上没有可可靠识别的重复周期。"""


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


@dataclass(frozen=True)
class _OwnedStagingDirectory:
    """A private staging directory retained by an open descriptor."""

    path: Path
    descriptor: int
    identity: tuple[int, int]
    publication_directory_descriptor: int
    publication_directory_identity: tuple[int, int]
    resources: _OwnedHatchResources


@dataclass(frozen=True)
class _OwnedStagedFile:
    """A staged regular file retained by its originally-created descriptor."""

    path: Path
    name: str
    descriptor: int
    identity: tuple[int, int]
    staging_directory: _OwnedStagingDirectory
    digest: bytes | None = None
    expected_size: int | None = None


@dataclass(frozen=True)
class _OwnedPublishedFile:
    """A no-replace destination retained through pair publication/rollback."""

    path: Path
    name: str
    descriptor: int
    identity: tuple[int, int]
    expected_size: int
    digest: bytes
    directory_descriptor: int
    staging_directory: _OwnedStagingDirectory


@dataclass
class _OwnedHatchResources:
    """Caller-owned registry that closes assignment-window acquisition gaps."""

    staging_directory: _OwnedStagingDirectory | None = None
    staged_files: list[_OwnedStagedFile] = field(default_factory=list)
    published_files: list[_OwnedPublishedFile] = field(default_factory=list)


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


def _write_block_metadata_stream(
    stream: TextIO,
    document: dict[str, object],
) -> None:
    json.dump(
        document,
        stream,
        ensure_ascii=False,
        allow_nan=False,
        indent=4,
    )
    stream.flush()
    os.fsync(stream.fileno())


def write_block_metadata(
    path: Path,
    document: dict[str, object],
    *,
    owned_file: _OwnedStagedFile | None = None,
) -> None:
    if owned_file is not None:
        with _open_owned_text_writer(owned_file, encoding="utf-8") as stream:
            _write_block_metadata_stream(stream, document)
        return

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
            _write_block_metadata_stream(stream, document)
        os.replace(temporary_path, path)
        temporary_path = None
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)


def _create_owned_staging_directory(
    final_path: Path,
    resources: _OwnedHatchResources,
) -> _OwnedStagingDirectory:
    publication_directory_descriptor = _open_directory_no_follow(final_path.parent)
    staging_path: Path | None = None
    descriptor: int | None = None
    staging_directory: _OwnedStagingDirectory | None = None
    try:
        publication_directory_stat = os.fstat(publication_directory_descriptor)
        publication_directory_identity = (
            publication_directory_stat.st_dev,
            publication_directory_stat.st_ino,
        )
        staging_path = Path(tempfile.mkdtemp(
            prefix=f".{final_path.name}.",
            suffix=".staging",
            dir=final_path.parent,
        ))
        path_stat = os.lstat(staging_path)
        path_identity = (path_stat.st_dev, path_stat.st_ino)
        if (
            not stat.S_ISDIR(path_stat.st_mode)
            or stat.S_IMODE(path_stat.st_mode) != 0o700
        ):
            raise ValueError("staging directory must be a private mode-0700 directory")
        flags = os.O_RDONLY | getattr(os, "O_DIRECTORY", 0)
        no_follow = getattr(os, "O_NOFOLLOW", 0)
        if not no_follow:
            raise OSError(errno.ENOTSUP, "O_NOFOLLOW is required for safe staging")
        flags |= no_follow
        descriptor = os.open(staging_path, flags)
        directory_stat = os.fstat(descriptor)
        if (
            not stat.S_ISDIR(directory_stat.st_mode)
            or stat.S_IMODE(directory_stat.st_mode) != 0o700
            or (directory_stat.st_dev, directory_stat.st_ino) != path_identity
        ):
            raise ValueError("staging directory must be a private mode-0700 directory")
        current_path_stat = os.lstat(staging_path)
        if (current_path_stat.st_dev, current_path_stat.st_ino) != path_identity:
            raise ValueError("staging directory identity changed during creation")
        staging_directory = _OwnedStagingDirectory(
            staging_path,
            descriptor,
            path_identity,
            publication_directory_descriptor,
            publication_directory_identity,
            resources,
        )
        _verify_owned_publication_directory(staging_directory)
        resources.staging_directory = staging_directory
        return staging_directory
    except BaseException:
        caller_owns_descriptors = resources.staging_directory is staging_directory
        if descriptor is not None and not caller_owns_descriptors:
            try:
                os.close(descriptor)
            except BaseException:
                pass
        if not caller_owns_descriptors:
            try:
                os.close(publication_directory_descriptor)
            except BaseException:
                pass
        # Do not mutate a pathname whose identity could have changed. At worst a
        # single private empty staging directory is left for manual inspection.
        raise


def _verify_owned_staging_directory(
    staging_directory: _OwnedStagingDirectory,
) -> os.stat_result:
    descriptor_stat = os.fstat(staging_directory.descriptor)
    if (
        (descriptor_stat.st_dev, descriptor_stat.st_ino)
        != staging_directory.identity
        or not stat.S_ISDIR(descriptor_stat.st_mode)
        or stat.S_IMODE(descriptor_stat.st_mode) not in {0o500, 0o700}
    ):
        raise ValueError("invalid owned staging directory descriptor")
    try:
        path_stat = os.lstat(staging_directory.path)
    except OSError as exc:
        raise ValueError("owned staging directory path is missing") from exc
    if (
        (path_stat.st_dev, path_stat.st_ino) != staging_directory.identity
        or not stat.S_ISDIR(path_stat.st_mode)
        or stat.S_IMODE(path_stat.st_mode) != stat.S_IMODE(descriptor_stat.st_mode)
    ):
        raise ValueError("owned staging directory identity changed")
    _verify_owned_publication_directory(staging_directory)
    return descriptor_stat


def _create_owned_temporary_file(
    final_path: Path,
    staging_directory: _OwnedStagingDirectory,
) -> _OwnedStagedFile:
    _verify_owned_staging_directory(staging_directory)
    flags = os.O_RDWR | os.O_CREAT | os.O_EXCL
    no_follow = getattr(os, "O_NOFOLLOW", 0)
    if not no_follow:
        raise OSError(errno.ENOTSUP, "O_NOFOLLOW is required for safe staging")
    flags |= no_follow
    file_descriptor = os.open(
        final_path.name,
        flags,
        0o600,
        dir_fd=staging_directory.descriptor,
    )
    owned_file: _OwnedStagedFile | None = None
    try:
        file_stat = os.fstat(file_descriptor)
        if not stat.S_ISREG(file_stat.st_mode):
            raise ValueError("staged artifact must be a regular file")
        relative_stat = os.stat(
            final_path.name,
            dir_fd=staging_directory.descriptor,
            follow_symlinks=False,
        )
        identity = (file_stat.st_dev, file_stat.st_ino)
        if (
            (relative_stat.st_dev, relative_stat.st_ino) != identity
            or not stat.S_ISREG(relative_stat.st_mode)
        ):
            raise ValueError("staged artifact identity changed during creation")
        owned_file = _OwnedStagedFile(
            staging_directory.path / final_path.name,
            final_path.name,
            file_descriptor,
            identity,
            staging_directory,
        )
        staging_directory.resources.staged_files.append(owned_file)
        return owned_file
    except BaseException:
        caller_owns_descriptor = any(
            staged_file is owned_file
            for staged_file in staging_directory.resources.staged_files
        )
        if not caller_owns_descriptor:
            try:
                os.close(file_descriptor)
            except BaseException:
                pass
        raise


def _verify_owned_staged_file(
    staged_file: _OwnedStagedFile,
    *,
    require_nonempty: bool = False,
) -> os.stat_result:
    _verify_owned_staging_directory(staged_file.staging_directory)
    descriptor_stat = os.fstat(staged_file.descriptor)
    descriptor_identity = (descriptor_stat.st_dev, descriptor_stat.st_ino)
    if (
        descriptor_identity != staged_file.identity
        or not stat.S_ISREG(descriptor_stat.st_mode)
        or stat.S_IMODE(descriptor_stat.st_mode) not in {0o400, 0o600}
        or (require_nonempty and descriptor_stat.st_size <= 0)
    ):
        raise ValueError(f"invalid owned staged artifact: {staged_file.path}")
    try:
        path_stat = os.stat(
            staged_file.name,
            dir_fd=staged_file.staging_directory.descriptor,
            follow_symlinks=False,
        )
    except OSError as exc:
        raise ValueError(f"missing owned staged artifact: {staged_file.path}") from exc
    if (
        (path_stat.st_dev, path_stat.st_ino) != staged_file.identity
        or not stat.S_ISREG(path_stat.st_mode)
        or stat.S_IMODE(path_stat.st_mode) != stat.S_IMODE(descriptor_stat.st_mode)
        or path_stat.st_size != descriptor_stat.st_size
    ):
        raise ValueError(f"staged artifact identity changed: {staged_file.path}")
    return descriptor_stat


def _digest_owned_descriptor(descriptor: int, expected_size: int) -> bytes:
    """Hash exactly one retained descriptor without changing its file offset."""
    digest = hashlib.sha256()
    offset = 0
    while offset < expected_size:
        chunk = os.pread(descriptor, min(1024 * 1024, expected_size - offset), offset)
        if not chunk:
            raise ValueError("owned artifact became shorter while hashing")
        digest.update(chunk)
        offset += len(chunk)
    if os.fstat(descriptor).st_size != expected_size:
        raise ValueError("owned artifact size changed while hashing")
    return digest.digest()


def _bind_owned_staged_file_content(
    staged_file: _OwnedStagedFile,
) -> _OwnedStagedFile:
    """Bind the bytes just authored by a completed owned writer."""
    file_stat = _verify_owned_staged_file(staged_file, require_nonempty=True)
    if stat.S_IMODE(file_stat.st_mode) != 0o600:
        raise ValueError("completed staged artifact must still be owner-writable")
    os.fsync(staged_file.descriptor)
    digest = _digest_owned_descriptor(staged_file.descriptor, file_stat.st_size)
    return _OwnedStagedFile(
        staged_file.path,
        staged_file.name,
        staged_file.descriptor,
        staged_file.identity,
        staged_file.staging_directory,
        digest,
        file_stat.st_size,
    )


def _seal_owned_staged_file(staged_file: _OwnedStagedFile) -> _OwnedStagedFile:
    """Make a completed staged inode read-only and bind it to a content digest."""
    file_stat = _verify_owned_staged_file(staged_file, require_nonempty=True)
    if staged_file.digest is None or staged_file.expected_size is None:
        raise ValueError("staged artifact content was not bound after writing")
    if (
        file_stat.st_size != staged_file.expected_size
        or _digest_owned_descriptor(staged_file.descriptor, file_stat.st_size)
        != staged_file.digest
    ):
        raise ValueError("staged artifact content changed after writing")
    os.fsync(staged_file.descriptor)
    os.fchmod(staged_file.descriptor, 0o400)
    sealed_stat = _verify_owned_staged_file(staged_file, require_nonempty=True)
    if (
        (sealed_stat.st_dev, sealed_stat.st_ino) != staged_file.identity
        or sealed_stat.st_size != staged_file.expected_size
        or stat.S_IMODE(sealed_stat.st_mode) != 0o400
        or _digest_owned_descriptor(
            staged_file.descriptor,
            staged_file.expected_size,
        ) != staged_file.digest
    ):
        raise ValueError("staged artifact could not be sealed for publication")
    return _OwnedStagedFile(
        staged_file.path,
        staged_file.name,
        staged_file.descriptor,
        staged_file.identity,
        staged_file.staging_directory,
        staged_file.digest,
        staged_file.expected_size,
    )


def _restore_owned_staged_file_mode(staged_file: _OwnedStagedFile) -> None:
    """Restore the published inode's owner-write bit after final verification."""
    if staged_file.digest is None or staged_file.expected_size is None:
        raise ValueError("staged artifact was not sealed")
    file_stat = os.fstat(staged_file.descriptor)
    if (
        not stat.S_ISREG(file_stat.st_mode)
        or (file_stat.st_dev, file_stat.st_ino) != staged_file.identity
        or file_stat.st_size != staged_file.expected_size
        or _digest_owned_descriptor(
            staged_file.descriptor,
            staged_file.expected_size,
        )
        != staged_file.digest
    ):
        raise ValueError("sealed staged artifact identity or content changed")
    os.fchmod(staged_file.descriptor, 0o600)
    restored_stat = _verify_owned_staged_file(staged_file, require_nonempty=True)
    if stat.S_IMODE(restored_stat.st_mode) != 0o600:
        raise ValueError("published artifact mode could not be restored")
    if (
        restored_stat.st_size != staged_file.expected_size
        or _digest_owned_descriptor(
            staged_file.descriptor,
            staged_file.expected_size,
        )
        != staged_file.digest
    ):
        raise ValueError("published artifact content changed while restoring mode")


def _open_owned_text_writer(
    staged_file: _OwnedStagedFile,
    *,
    encoding: str,
) -> TextIO:
    _verify_owned_staged_file(staged_file)
    os.lseek(staged_file.descriptor, 0, os.SEEK_SET)
    os.ftruncate(staged_file.descriptor, 0)
    descriptor = os.dup(staged_file.descriptor)
    try:
        return os.fdopen(
            descriptor,
            "w",
            encoding=encoding,
            newline="",
        )
    except BaseException:
        try:
            os.close(descriptor)
        except BaseException:
            pass
        raise


def _open_owned_text_reader(
    staged_file: _OwnedStagedFile,
    *,
    label: str,
    encoding: str,
) -> TextIO:
    """Wrap a verified duplicate, closing it even if stream creation is interrupted."""
    if not isinstance(staged_file, _OwnedStagedFile):
        raise ValueError(f"staged {label} must be an originally-owned artifact")
    _verify_owned_staged_file(staged_file, require_nonempty=True)
    descriptor = os.dup(staged_file.descriptor)
    try:
        os.lseek(descriptor, 0, os.SEEK_SET)
        return os.fdopen(descriptor, "r", encoding=encoding)
    except BaseException:
        try:
            os.close(descriptor)
        except BaseException:
            pass
        raise


def _entry_identity(
    directory_descriptor: int,
    name: str,
) -> tuple[int, int] | None:
    try:
        path_stat = os.stat(
            name,
            dir_fd=directory_descriptor,
            follow_symlinks=False,
        )
    except FileNotFoundError:
        return None
    return (path_stat.st_dev, path_stat.st_ino)


def _open_directory_no_follow(path: Path) -> int:
    path_stat = os.lstat(path)
    if not stat.S_ISDIR(path_stat.st_mode):
        raise ValueError(f"publication parent must be a directory: {path}")
    identity = (path_stat.st_dev, path_stat.st_ino)
    flags = os.O_RDONLY | getattr(os, "O_DIRECTORY", 0)
    no_follow = getattr(os, "O_NOFOLLOW", 0)
    if not no_follow:
        raise OSError(errno.ENOTSUP, "O_NOFOLLOW is required for safe publication")
    descriptor = os.open(path, flags | no_follow)
    try:
        descriptor_stat = os.fstat(descriptor)
        if (
            not stat.S_ISDIR(descriptor_stat.st_mode)
            or (descriptor_stat.st_dev, descriptor_stat.st_ino) != identity
        ):
            raise ValueError(f"publication parent identity changed: {path}")
        return descriptor
    except BaseException:
        try:
            os.close(descriptor)
        except BaseException:
            pass
        raise


def _verify_owned_publication_directory(
    staging_directory: _OwnedStagingDirectory,
) -> os.stat_result:
    """Verify the pinned parent, staging relationship, and current parent path."""
    descriptor_stat = os.fstat(
        staging_directory.publication_directory_descriptor
    )
    if (
        not stat.S_ISDIR(descriptor_stat.st_mode)
        or (descriptor_stat.st_dev, descriptor_stat.st_ino)
        != staging_directory.publication_directory_identity
    ):
        raise ValueError("invalid pinned publication directory descriptor")
    actual_parent_stat = os.stat(
        "..",
        dir_fd=staging_directory.descriptor,
        follow_symlinks=False,
    )
    if (
        not stat.S_ISDIR(actual_parent_stat.st_mode)
        or (actual_parent_stat.st_dev, actual_parent_stat.st_ino)
        != staging_directory.publication_directory_identity
    ):
        raise ValueError("staging directory left its pinned publication parent")

    current_descriptor = _open_directory_no_follow(staging_directory.path.parent)
    try:
        current_stat = os.fstat(current_descriptor)
        if (
            not stat.S_ISDIR(current_stat.st_mode)
            or (current_stat.st_dev, current_stat.st_ino)
            != staging_directory.publication_directory_identity
        ):
            raise ValueError("publication directory path identity changed")
    finally:
        os.close(current_descriptor)
    return descriptor_stat


def _lock_owned_staging_directory(
    staging_directory: _OwnedStagingDirectory,
) -> None:
    """Freeze staged entry names before descriptor-relative hard-link publication."""
    directory_stat = _verify_owned_staging_directory(staging_directory)
    if stat.S_IMODE(directory_stat.st_mode) == 0o500:
        return
    os.fchmod(staging_directory.descriptor, 0o500)
    locked_stat = _verify_owned_staging_directory(staging_directory)
    if stat.S_IMODE(locked_stat.st_mode) != 0o500:
        raise ValueError("owned staging directory could not be locked for publication")


def _make_owned_staging_directory_writable(
    staging_directory: _OwnedStagingDirectory,
) -> None:
    """Restore owner write permission through the retained directory descriptor."""
    directory_stat = os.fstat(staging_directory.descriptor)
    if (
        not stat.S_ISDIR(directory_stat.st_mode)
        or (directory_stat.st_dev, directory_stat.st_ino)
        != staging_directory.identity
    ):
        raise ValueError("invalid owned staging directory descriptor")
    if stat.S_IMODE(directory_stat.st_mode) != 0o700:
        os.fchmod(staging_directory.descriptor, 0o700)
    writable_stat = os.fstat(staging_directory.descriptor)
    if (
        not stat.S_ISDIR(writable_stat.st_mode)
        or (writable_stat.st_dev, writable_stat.st_ino)
        != staging_directory.identity
        or stat.S_IMODE(writable_stat.st_mode) != 0o700
    ):
        raise ValueError("owned staging directory could not be made writable")


def _atomic_rename_no_replace(
    source_directory_descriptor: int,
    source_name: str,
    destination_directory_descriptor: int,
    destination_name: str,
) -> None:
    """Atomically rename one directory entry without replacing another."""
    libc = ctypes.CDLL(None, use_errno=True)
    source_bytes = os.fsencode(source_name)
    destination_bytes = os.fsencode(destination_name)
    if sys.platform == "darwin":
        try:
            rename_function = libc.renameatx_np
        except AttributeError as exc:
            raise OSError(
                errno.ENOSYS,
                "atomic descriptor-relative no-replace rename is unavailable",
            ) from exc
        rename_function.argtypes = [
            ctypes.c_int,
            ctypes.c_char_p,
            ctypes.c_int,
            ctypes.c_char_p,
            ctypes.c_uint,
        ]
        rename_function.restype = ctypes.c_int
        arguments = (
            source_directory_descriptor,
            source_bytes,
            destination_directory_descriptor,
            destination_bytes,
            0x00000004,
        )
    elif sys.platform.startswith("linux"):
        try:
            rename_function = libc.renameat2
        except AttributeError as exc:
            raise OSError(
                errno.ENOSYS,
                "atomic descriptor-relative no-replace rename is unavailable",
            ) from exc
        rename_function.argtypes = [
            ctypes.c_int,
            ctypes.c_char_p,
            ctypes.c_int,
            ctypes.c_char_p,
            ctypes.c_uint,
        ]
        rename_function.restype = ctypes.c_int
        arguments = (
            source_directory_descriptor,
            source_bytes,
            destination_directory_descriptor,
            destination_bytes,
            1,
        )
    else:
        raise OSError(
            errno.ENOTSUP,
            f"atomic descriptor-relative rename is unsupported on {sys.platform}",
        )

    ctypes.set_errno(0)
    if rename_function(*arguments) == 0:
        return
    error_number = ctypes.get_errno() or errno.EIO
    if error_number in {errno.EEXIST, errno.ENOTEMPTY}:
        raise FileExistsError(
            error_number,
            os.strerror(error_number),
            destination_name,
        )
    raise OSError(error_number, os.strerror(error_number), destination_name)


def _restore_foreign_rollback_entry(
    published_file: _OwnedPublishedFile,
    quarantine_name: str,
    expected_identity: tuple[int, int] | None = None,
) -> None:
    """Boundedly restore only the foreign identity captured by this rollback."""
    if expected_identity is None:
        expected_identity = _entry_identity(
            published_file.staging_directory.descriptor,
            quarantine_name,
        )
    if expected_identity is None or expected_identity == published_file.identity:
        return

    for attempt in range(2):
        try:
            quarantine_identity = _entry_identity(
                published_file.staging_directory.descriptor,
                quarantine_name,
            )
            public_identity = _entry_identity(
                published_file.directory_descriptor,
                published_file.name,
            )
            if public_identity == expected_identity:
                return
            if public_identity is not None or quarantine_identity != expected_identity:
                return
            _atomic_rename_no_replace(
                published_file.staging_directory.descriptor,
                quarantine_name,
                published_file.directory_descriptor,
                published_file.name,
            )
            if _entry_identity(
                published_file.directory_descriptor,
                published_file.name,
            ) != expected_identity:
                raise ValueError(
                    "foreign rollback replacement identity changed during restore"
                )
            return
        except BaseException:
            if attempt == 1:
                raise


def _rollback_published_file(
    published_file: _OwnedPublishedFile | None,
) -> None:
    """Quarantine only an identity-proven owned public entry."""
    if published_file is None:
        return
    _make_owned_staging_directory_writable(published_file.staging_directory)
    descriptor_stat = os.fstat(published_file.descriptor)
    if (
        (descriptor_stat.st_dev, descriptor_stat.st_ino)
        != published_file.identity
        or not stat.S_ISREG(descriptor_stat.st_mode)
    ):
        return
    if _entry_identity(
        published_file.directory_descriptor,
        published_file.name,
    ) != published_file.identity:
        return
    quarantine_name = f".{published_file.name}.published-rollback"
    if _entry_identity(
        published_file.staging_directory.descriptor,
        quarantine_name,
    ) is not None:
        return
    try:
        _atomic_rename_no_replace(
            published_file.directory_descriptor,
            published_file.name,
            published_file.staging_directory.descriptor,
            quarantine_name,
        )
        moved_identity = _entry_identity(
            published_file.staging_directory.descriptor,
            quarantine_name,
        )
    except BaseException:
        _restore_foreign_rollback_entry(published_file, quarantine_name)
        raise
    if moved_identity != published_file.identity:
        # The public entry changed in the rename window. Restore that
        # replacement instead of deleting or consuming it.
        _restore_foreign_rollback_entry(
            published_file,
            quarantine_name,
            moved_identity,
        )
        return
    # POSIX has no conditional unlink-by-inode primitive. Leave this proven
    # owned quarantine in the private directory instead of reopening a final
    # check-to-unlink race that could delete a swapped-in foreign file.


def _close_published_file(published_file: _OwnedPublishedFile | None) -> None:
    if published_file is None:
        return
    os.close(published_file.directory_descriptor)


def _verify_owned_published_file(
    published_file: _OwnedPublishedFile,
    *,
    expected_mode: int,
) -> os.stat_result:
    """Revalidate one public entry against immutable identity/size/content state."""
    source_stat = os.fstat(published_file.descriptor)
    if (
        not stat.S_ISREG(source_stat.st_mode)
        or (source_stat.st_dev, source_stat.st_ino) != published_file.identity
        or stat.S_IMODE(source_stat.st_mode) != expected_mode
        or source_stat.st_size != published_file.expected_size
        or _digest_owned_descriptor(
            published_file.descriptor,
            published_file.expected_size,
        ) != published_file.digest
    ):
        raise ValueError(f"published source content changed: {published_file.path}")

    no_follow = getattr(os, "O_NOFOLLOW", 0)
    if not no_follow:
        raise OSError(errno.ENOTSUP, "O_NOFOLLOW is required for safe publication")
    verification_descriptor = os.open(
        published_file.name,
        os.O_RDONLY | getattr(os, "O_NONBLOCK", 0) | no_follow,
        dir_fd=published_file.directory_descriptor,
    )
    try:
        public_stat = os.fstat(verification_descriptor)
        if (
            not stat.S_ISREG(public_stat.st_mode)
            or (public_stat.st_dev, public_stat.st_ino) != published_file.identity
            or stat.S_IMODE(public_stat.st_mode) != expected_mode
            or public_stat.st_size != published_file.expected_size
            or _digest_owned_descriptor(
                verification_descriptor,
                published_file.expected_size,
            ) != published_file.digest
        ):
            raise ValueError(f"published artifact changed: {published_file.path}")
    finally:
        os.close(verification_descriptor)
    if _entry_identity(
        published_file.directory_descriptor,
        published_file.name,
    ) != published_file.identity:
        raise ValueError(f"published artifact path changed: {published_file.path}")
    return public_stat


def _publish_file_no_replace(
    source: _OwnedStagedFile,
    destination: Path,
) -> _OwnedPublishedFile:
    """Atomically hard-link a locked, completed private artifact into place."""
    source_stat = _verify_owned_staged_file(source, require_nonempty=True)
    if source.digest is None:
        raise ValueError("staged artifact must be content-sealed before publication")
    if (
        stat.S_IMODE(source_stat.st_mode) != 0o400
        or _digest_owned_descriptor(source.descriptor, source_stat.st_size)
        != source.digest
    ):
        raise ValueError("staged artifact content changed before publication")
    destination_directory_descriptor = os.dup(
        source.staging_directory.publication_directory_descriptor
    )
    published_file: _OwnedPublishedFile | None = None
    try:
        published_file = _OwnedPublishedFile(
            destination,
            destination.name,
            source.descriptor,
            source.identity,
            source_stat.st_size,
            source.digest,
            destination_directory_descriptor,
            source.staging_directory,
        )
        source.staging_directory.resources.published_files.append(published_file)
        no_follow = getattr(os, "O_NOFOLLOW", 0)
        if not no_follow:
            raise OSError(errno.ENOTSUP, "O_NOFOLLOW is required for safe publication")
        _verify_owned_publication_directory(source.staging_directory)
        _verify_owned_staged_file(source, require_nonempty=True)
        _lock_owned_staging_directory(source.staging_directory)
        if stat.S_IMODE(os.fstat(source.staging_directory.descriptor).st_mode) != 0o500:
            raise ValueError("staging directory must be locked before publication")
        os.link(
            source.name,
            destination.name,
            src_dir_fd=source.staging_directory.descriptor,
            dst_dir_fd=destination_directory_descriptor,
            follow_symlinks=False,
        )
        _verify_owned_published_file(published_file, expected_mode=0o400)
        return published_file
    except BaseException:
        caller_owns_descriptor = any(
            registered is published_file
            for registered in source.staging_directory.resources.published_files
        )
        if not caller_owns_descriptor:
            try:
                os.close(destination_directory_descriptor)
            except BaseException:
                pass
        raise


def validate_hatch_output_pair(
    dxf_path: _OwnedStagedFile,
    metadata_path: _OwnedStagedFile,
    expected_metadata: dict[str, object],
) -> None:
    """Descriptor-read and content-validate a staged DXF/metadata pair."""

    try:
        with _open_owned_text_reader(
            dxf_path,
            label="DXF",
            encoding="ascii",
        ) as stream:
            dxf_pairs = stream.read().splitlines()
    except (OSError, UnicodeError) as exc:
        raise ValueError(f"invalid staged DXF: {dxf_path}") from exc
    if len(dxf_pairs) % 2 != 0 or dxf_pairs[-2:] != ["0", "EOF"]:
        raise ValueError(f"invalid staged DXF structure: {dxf_path}")
    dxf_line_count = sum(
        dxf_pairs[index] == "0" and dxf_pairs[index + 1] == "LINE"
        for index in range(0, len(dxf_pairs), 2)
    )

    try:
        with _open_owned_text_reader(
            metadata_path,
            label="block metadata",
            encoding="utf-8",
        ) as stream:
            actual_metadata = json.load(stream)
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ValueError(f"invalid staged block metadata: {metadata_path}") from exc
    if actual_metadata != expected_metadata:
        raise ValueError("staged block metadata does not match the generated document")
    blocks = actual_metadata["blocks"]
    expected_line_count = actual_metadata["border_line_count"] + sum(
        block["line_count"] for block in blocks
    )
    if dxf_line_count != expected_line_count:
        raise ValueError(
            "staged DXF LINE count does not match the staged block metadata"
        )


def _valid_image_dpi(value: object) -> tuple[float, float] | None:
    if not isinstance(value, (tuple, list)) or len(value) < 2:
        return None
    try:
        dpi_x, dpi_y = float(value[0]), float(value[1])
    except (TypeError, ValueError):
        return None
    if not math.isfinite(dpi_x) or not math.isfinite(dpi_y):
        return None
    return (dpi_x, dpi_y) if dpi_x > 0 and dpi_y > 0 else None


def _encode_preview_png(image: Image.Image) -> str:
    preview = image.copy()
    if preview.mode not in ("L", "LA", "RGB", "RGBA"):
        preview = preview.convert("RGBA")
    output = io.BytesIO()
    preview.save(output, format="PNG", optimize=True)
    raw = output.getvalue()
    if len(raw) > MAX_PREVIEW_PNG_BYTES:
        raise ValueError("图片预览 PNG 超过 64 MiB 限制。")
    return base64.b64encode(raw).decode("ascii")


def _validate_fallback_dpi(value: object | None) -> float | None:
    if value is None:
        return None
    try:
        dpi = float(value)
    except (TypeError, ValueError) as exc:
        raise ValueError("备用 DPI 必须是有限的正数。") from exc
    if not math.isfinite(dpi) or dpi <= 0:
        raise ValueError("备用 DPI 必须是有限的正数。")
    return dpi


def _fallback_dpi_argument(value: str) -> float:
    try:
        dpi = _validate_fallback_dpi(value)
    except ValueError as exc:
        raise argparse.ArgumentTypeError(str(exc)) from exc
    assert dpi is not None
    return dpi


def inspect_texture_image(
    image_path: Path, include_preview: bool = False
) -> dict[str, object]:
    with Image.open(image_path) as image:
        pixel_width, pixel_height = image.size
        dpi = _valid_image_dpi(image.info.get("dpi"))
        preview_png_base64 = (
            _encode_preview_png(image)
            if include_preview
            else None
        )
    payload: dict[str, object] = {
        "pixel_width": int(pixel_width),
        "pixel_height": int(pixel_height),
        "dpi_x": dpi[0] if dpi else None,
        "dpi_y": dpi[1] if dpi else None,
    }
    if preview_png_base64 is not None:
        payload["preview_png_base64"] = preview_png_base64
    return payload


def read_binary_texture(
    image_path: Path,
    black_threshold: int = 128,
    fallback_dpi: float | None = None,
) -> tuple[np.ndarray, float, float]:
    """读取纹理，返回黑区掩膜和 X/Y 方向的 mm/像素。"""
    validated_fallback_dpi = _validate_fallback_dpi(fallback_dpi)
    with Image.open(image_path) as image:
        gray = np.asarray(image.convert("L"), dtype=np.uint8)
        dpi = _valid_image_dpi(image.info.get("dpi"))

    if dpi:
        dpi_x, dpi_y = dpi
    elif validated_fallback_dpi is not None:
        dpi_x = dpi_y = validated_fallback_dpi
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
    raise RepeatPeriodNotFoundError(
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
    publish_pair = block_metadata_output is not None and bool(voronoi_blocks)
    if publish_pair:
        assert block_metadata_output is not None
        output_parent = os.path.abspath(output_path.parent)
        metadata_parent = os.path.abspath(block_metadata_output.parent)
        if output_parent != metadata_parent:
            raise ValueError("DXF and block metadata must use the same output directory")
        for final_path in (output_path, block_metadata_output):
            if os.path.lexists(final_path):
                raise FileExistsError(f"output path already exists: {final_path}")
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
    resources = _OwnedHatchResources()
    staging_directory: _OwnedStagingDirectory | None = None
    temporary_dxf: _OwnedStagedFile | None = None
    temporary_metadata: _OwnedStagedFile | None = None
    published_dxf: _OwnedPublishedFile | None = None
    published_metadata: _OwnedPublishedFile | None = None
    try:
        if publish_pair:
            assert block_metadata_output is not None
            staging_directory = _create_owned_staging_directory(
                output_path,
                resources,
            )
            temporary_dxf = _create_owned_temporary_file(
                output_path,
                staging_directory,
            )
            temporary_metadata = _create_owned_temporary_file(
                block_metadata_output,
                staging_directory,
            )
            dxf_stream = _open_owned_text_writer(temporary_dxf, encoding="ascii")
        else:
            dxf_stream = output_path.open("w", encoding="ascii", newline="")

        with dxf_stream as stream:
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

        if publish_pair:
            assert block_metadata_output is not None
            assert temporary_dxf is not None
            assert temporary_metadata is not None
            temporary_dxf = _bind_owned_staged_file_content(temporary_dxf)
            metadata_document = build_block_metadata(
                voronoi_blocks,
                block_order,
                ordered_block_counts,
                4 if include_border else 0,
            )
            write_block_metadata(
                temporary_metadata.path,
                metadata_document,
                owned_file=temporary_metadata,
            )
            temporary_metadata = _bind_owned_staged_file_content(temporary_metadata)
            _lock_owned_staging_directory(staging_directory)
            _verify_owned_staged_file(temporary_dxf, require_nonempty=True)
            _verify_owned_staged_file(temporary_metadata, require_nonempty=True)
            temporary_dxf = _seal_owned_staged_file(temporary_dxf)
            temporary_metadata = _seal_owned_staged_file(temporary_metadata)
            validate_hatch_output_pair(
                temporary_dxf,
                temporary_metadata,
                metadata_document,
            )
            published_dxf = _publish_file_no_replace(temporary_dxf, output_path)
            published_metadata = _publish_file_no_replace(
                temporary_metadata,
                block_metadata_output,
            )
            _verify_owned_published_file(published_dxf, expected_mode=0o400)
            _verify_owned_published_file(published_metadata, expected_mode=0o400)
            _verify_owned_publication_directory(staging_directory)
            _make_owned_staging_directory_writable(staging_directory)
            _restore_owned_staged_file_mode(temporary_dxf)
            _restore_owned_staged_file_mode(temporary_metadata)
            _verify_owned_published_file(published_dxf, expected_mode=0o600)
            _verify_owned_published_file(published_metadata, expected_mode=0o600)
            _verify_owned_publication_directory(staging_directory)
    except BaseException:
        if publish_pair:
            assert block_metadata_output is not None
            owned_staging_directory = resources.staging_directory
            if owned_staging_directory is not None:
                try:
                    _make_owned_staging_directory_writable(owned_staging_directory)
                except BaseException:
                    pass
            # Publication is two explicit no-replace hard links, not an atomic
            # pair. Roll back only entries still proven to be this invocation's
            # inode; quarantine them under the retained private directory rather
            # than unlinking a public pathname.
            for published_file in reversed(resources.published_files):
                for _attempt in range(2):
                    try:
                        _rollback_published_file(published_file)
                        break
                    except BaseException:
                        # One retry closes the async-exception window before a
                        # namespace move. Persistent cleanup failure remains
                        # bounded and cannot mask the primary generation error.
                        pass
        raise
    finally:
        if publish_pair:
            for published_file in reversed(resources.published_files):
                try:
                    _close_published_file(published_file)
                except BaseException:
                    pass
            for staged_file in reversed(resources.staged_files):
                # POSIX has no conditional unlink-by-inode primitive. Close the
                # retained descriptor and leave any failed staged entry in the
                # bounded private directory rather than risk deleting a
                # replacement introduced after an identity check.
                try:
                    os.close(staged_file.descriptor)
                except BaseException:
                    pass
            owned_staging_directory = resources.staging_directory
            if owned_staging_directory is not None:
                # There is no POSIX rmdir-by-open-descriptor primitive. Keep one
                # bounded private directory instead of risking a lstat-to-rmdir
                # swap that removes a foreign empty directory.
                try:
                    os.close(owned_staging_directory.descriptor)
                except BaseException:
                    pass
                try:
                    os.close(
                        owned_staging_directory.publication_directory_descriptor
                    )
                except BaseException:
                    pass

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
    fallback_dpi = _validate_fallback_dpi(fallback_dpi)

    source, pixel_width_mm, pixel_height_mm = read_binary_texture(
        input_path,
        black_threshold=black_threshold,
        fallback_dpi=fallback_dpi,
    )

    target_width_px = max(1, round(target_width_mm / pixel_width_mm))
    target_height_px = max(1, round(target_height_mm / pixel_height_mm))
    repeat_info: tuple[int, int, int, int, float, float, int, int] | None = None
    used_full_source_fallback = False
    if tile_mode == "unit":
        try:
            (
                unit,
                period_width,
                period_height,
                unit_x,
                unit_y,
                repeat_similarity,
                seam_score,
            ) = detect_repeating_unit(source)
        except RepeatPeriodNotFoundError:
            fitted = fit_texture_to_size(
                source,
                target_width_px,
                target_height_px,
                crop_anchor=crop_anchor,
                tile_mode="repeat",
            )
            used_full_source_fallback = True
        else:
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

    if used_full_source_fallback:
        print("处理方式: 未识别到重复周期，使用完整输入图")
    elif tile_mode == "unit":
        print("处理方式: 自动识别最小重复单元")
    else:
        print("处理方式: 传统纹理拼接")
    print(f"像素尺寸: {pixel_width_mm:.8f} × {pixel_height_mm:.8f} mm")
    print(f"目标栅格: {target_width_px} × {target_height_px} px")
    print(f"目标尺寸: {target_width_mm:g} × {target_height_mm:g} mm")
    mode_names = {
        "unit": "完整重复单元",
        "repeat": "周期重复",
        "mirror": "镜像拼接",
    }
    mode_name = (
        "完整输入图周期填充"
        if used_full_source_fallback
        else mode_names[tile_mode]
    )
    print(f"拼接模式: {mode_name}")
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
        effective_block_count = sum(count > 0 for count in block_line_counts)
        print(f"有效加工块: {effective_block_count}")
        print(f"空加工块: {len(block_line_counts) - effective_block_count}")
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
    parser.add_argument("output", type=Path, nargs="?", help="输出 DXF")
    parser.add_argument(
        "--inspect-image",
        action="store_true",
        help="以 JSON 输出图片像素和 DPI 信息后退出",
    )
    parser.add_argument(
        "--include-preview",
        action="store_true",
        help="检查图片时嵌入完整像素尺寸的 PNG 预览",
    )
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
        type=_fallback_dpi_argument,
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

    if args.include_preview and not args.inspect_image:
        parser.error("--include-preview 只能与 --inspect-image 一起使用")
    if args.inspect_image:
        return args
    if args.output is None:
        parser.error("转换模式需要输出 DXF 路径")
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
    if args.inspect_image:
        print(json.dumps(
            inspect_texture_image(args.input, args.include_preview),
            ensure_ascii=False,
        ))
        return
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
