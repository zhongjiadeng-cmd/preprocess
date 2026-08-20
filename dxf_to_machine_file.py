"""Parse layered DXFs and generate validated machine-file directories."""

from __future__ import annotations

import argparse
from copy import deepcopy
import ctypes
from dataclasses import dataclass
from datetime import datetime
from decimal import Decimal, InvalidOperation
import errno
import json
import math
import os
from pathlib import Path
import re
import shutil
import stat
import sys
import uuid

import numpy as np


_LINE_COORDINATE_CODES = (10, 20, 30, 11, 21, 31)
_LAYER_FILENAME_RE = re.compile(r"layer_(\d+)_.*\.dxf", re.IGNORECASE)
_OWNER_TOKEN_RE = re.compile(r"[A-Za-z0-9_-]{1,128}")
_VENDOR_DECIMAL_RE = re.compile(r"-?(?:0|[1-9][0-9]*)\.[0-9]{3}")
MAX_LAYER_STEP_UM = 100_000

DEFAULT_LASER_PARAMS: tuple[dict[str, object], ...] = (
    {
        "power": 38, "frequency": 350, "pulseWidthIdx": 3,
        "scanSpeed": 2100, "jump_vel": 6000, "jump_delay": 50,
        "scan_ahead": True, "accScale": 50, "cornerScale": 100,
        "endScale": 100, "sky_writing": True, "timeLag": 100,
        "laserOnShift": 18, "delaseroff": 32, "delaseron": 0,
    },
    {
        "frequency": 100, "power": 10, "pulseWidthIdx": 3,
        "scanSpeed": 2100, "jump_vel": 6000, "jump_delay": 50,
        "scan_ahead": True, "accScale": 50, "cornerScale": 100,
        "endScale": 100, "sky_writing": False, "timeLag": 100,
        "laserOnShift": 18, "delaseroff": 32, "delaseron": 0,
    },
    {
        "power": 20, "frequency": 350, "pulseWidthIdx": 4,
        "scanSpeed": 2100, "jump_vel": 6000, "jump_delay": 50,
        "scan_ahead": True, "accScale": 50, "cornerScale": 100,
        "endScale": 100, "sky_writing": True, "timeLag": 100,
        "laserOnShift": 18, "delaseroff": 32, "delaseron": 0,
    },
)
DEFAULT_GALVO_OFFSET: dict[str, list[int]] = {"galvo_0": [0, 0, 0, 0]}
_BOOLEAN_LASER_KEYS = {"scan_ahead", "sky_writing"}


@dataclass(frozen=True)
class PatchPlacement:
    """The layer and global center for a single planned patch."""

    layer_index: int
    center_x: float
    center_y: float


@dataclass(frozen=True)
class PlannedPatch:
    """A patch's placement and its source-coordinate DXF line segments."""

    placement: PatchPlacement
    lines: np.ndarray


@dataclass(frozen=True)
class BlockDefinition:
    """The center and LINE count recorded for one source hatch block."""

    block_index: int
    center_x: float
    center_y: float
    line_count: int


@dataclass(frozen=True)
class BlockMetadata:
    """Validated v1 block metadata for one layered DXF."""

    border_line_count: int
    blocks: tuple[BlockDefinition, ...]


def _validate_layer_step(layer_step_um: float) -> int:
    if isinstance(layer_step_um, bool):
        raise ValueError("layer_step_um must be a whole number of micrometres")
    if isinstance(layer_step_um, (int, np.integer)):
        step_um = int(layer_step_um)
    elif isinstance(layer_step_um, (float, np.floating)):
        numeric_step = float(layer_step_um)
        if not math.isfinite(numeric_step) or not numeric_step.is_integer():
            raise ValueError("layer_step_um must be a whole number of micrometres")
        step_um = int(numeric_step)
    else:
        raise ValueError("layer_step_um must be a whole number of micrometres")
    if not 1 <= step_um <= MAX_LAYER_STEP_UM:
        raise ValueError(
            f"layer_step_um must be between 1 and {MAX_LAYER_STEP_UM} micrometres"
        )
    return step_um


def _parse_layer_step_argument(value: str) -> int:
    """Parse the CLI step losslessly before applying the shared range contract."""
    try:
        parsed = Decimal(value)
    except InvalidOperation as exc:
        raise argparse.ArgumentTypeError(
            "layer step must be a whole number from 1 to 100000 micrometres"
        ) from exc
    if (
        not parsed.is_finite()
        or parsed != parsed.to_integral_value()
        or parsed < Decimal(1)
        or parsed > Decimal(MAX_LAYER_STEP_UM)
    ):
        raise argparse.ArgumentTypeError(
            "layer step must be a whole number from 1 to 100000 micrometres"
        )
    try:
        return _validate_layer_step(int(parsed))
    except ValueError as exc:
        raise argparse.ArgumentTypeError(str(exc)) from exc


def _validate_first_laser_params(params: dict[str, object]) -> None:
    if not isinstance(params, dict) or set(params) != set(DEFAULT_LASER_PARAMS[0]):
        raise ValueError("first_laser_params must contain exactly the required keys")
    for key, value in params.items():
        if key in _BOOLEAN_LASER_KEYS:
            if type(value) is not bool:
                raise ValueError(f"{key} must be a bool")
        elif type(value) is not int:
            raise ValueError(f"{key} must be an int")


def _validate_placements(placements: list[PatchPlacement]) -> None:
    if not isinstance(placements, list) or not placements:
        raise ValueError("placements must be a non-empty list")

    previous_layer: int | None = None
    for placement in placements:
        if not isinstance(placement, PatchPlacement):
            raise ValueError("placements must contain PatchPlacement values")
        if (
            isinstance(placement.layer_index, bool)
            or not isinstance(placement.layer_index, (int, np.integer))
            or placement.layer_index < 0
        ):
            raise ValueError("placement layer_index must be a non-negative integer")
        for center in (placement.center_x, placement.center_y):
            if (
                isinstance(center, bool)
                or not isinstance(center, (int, float, np.integer, np.floating))
                or not np.isfinite(center)
            ):
                raise ValueError("placement centers must be finite numbers")
        layer_index = int(placement.layer_index)
        if previous_layer is None:
            if layer_index != 0:
                raise ValueError("placements must start at layer zero")
        elif layer_index not in {previous_layer, previous_layer + 1}:
            raise ValueError("placement layers must repeat or advance by one")
        previous_layer = layer_index


def _rounded_machine_coordinate(value: float) -> float:
    if not np.isfinite(value):
        raise ValueError("machine coordinates and deltas must be finite")
    rounded = float(f"{value:.3f}")
    if not np.isfinite(rounded):
        raise ValueError("rounded machine coordinates and deltas must be finite")
    return 0.0 if rounded == 0.0 else rounded


def _validate_owner_token(owner_token: str) -> None:
    if type(owner_token) is not str or _OWNER_TOKEN_RE.fullmatch(owner_token) is None:
        raise ValueError(
            "owner_token must be 1-128 ASCII letters, digits, hyphens, or underscores"
        )


def resolve_output_name(output_name: str | None, now: datetime | None = None) -> str:
    """Resolve a safe output directory name, generating one for blank input."""
    if output_name is None or output_name.strip() == "":
        return f"machine_file_{(now or datetime.now()).strftime('%Y%m%d_%H%M%S')}"
    output_name = output_name.strip()
    if (
        output_name in {".", ".."}
        or "/" in output_name
        or "\\" in output_name
        or Path(output_name).name != output_name
    ):
        raise ValueError("output_name must be a plain directory name")
    return output_name


def build_machine_document(
    placements: list[PatchPlacement],
    layer_step_um: float,
    first_laser_params: dict[str, object],
) -> dict[str, object]:
    """Build the complete machine.json object in its required insertion order."""
    _validate_placements(placements)
    step_um = _validate_layer_step(layer_step_um)
    _validate_first_laser_params(first_laser_params)
    step_mm = step_um / 1000
    previous_commanded_x = previous_commanded_y = 0.0
    previous_layer = 0
    cycles = []
    for patch_index, placement in enumerate(placements):
        target_x = _rounded_machine_coordinate(float(placement.center_x))
        target_y = _rounded_machine_coordinate(float(placement.center_y))
        delta_x = _rounded_machine_coordinate(target_x - previous_commanded_x)
        delta_y = _rounded_machine_coordinate(target_y - previous_commanded_y)
        delta_z = (
            0.0
            if placement.layer_index == previous_layer
            else _rounded_machine_coordinate(-step_mm)
        )
        command = f"G00X{delta_x:.3f}Y{delta_y:.3f}Z{delta_z:.3f}F40"
        if patch_index == 0:
            command = "G91" + command
        if patch_index == len(placements) - 1:
            # Target-controller contract: F40 completes this relative motion
            # before the lexically trailing G90 restores absolute mode.
            command += "G90"
        cycles.append({"galvo_0": [0, command, [patch_index, 0]]})
        previous_commanded_x = target_x
        previous_commanded_y = target_y
        previous_layer = int(placement.layer_index)
    return {
        "laser_params": [deepcopy(first_laser_params), *deepcopy(DEFAULT_LASER_PARAMS[1:])],
        "galvo_offset": deepcopy(DEFAULT_GALVO_OFFSET),
        "machine_cycle": cycles,
    }


def _consume_vendor_literal(command: str, cursor: int, literal: str) -> int:
    if not command.startswith(literal, cursor):
        raise ValueError(f"vendor command expected {literal!r} at offset {cursor}")
    return cursor + len(literal)


def _consume_vendor_axis(
    command: str,
    cursor: int,
    axis: str,
) -> tuple[int, int]:
    cursor = _consume_vendor_literal(command, cursor, axis)
    match = _VENDOR_DECIMAL_RE.match(command, cursor)
    if match is None:
        raise ValueError(f"vendor command has an invalid {axis} coordinate")
    token = match.group(0)
    negative = token.startswith("-")
    unsigned = token[1:] if negative else token
    whole, fraction = unsigned.split(".")
    thousandths = int(whole) * 1000 + int(fraction)
    return (-thousandths if negative else thousandths), match.end()


def _decimal_from_thousandths(value: int) -> Decimal:
    """Construct an exact Decimal without applying the ambient context."""
    digits = tuple(int(character) for character in str(abs(value)))
    return Decimal((1 if value < 0 else 0, digits, -3))


def _simulate_vendor_machine_cycles(
    machine_cycles: object,
) -> list[tuple[Decimal, Decimal, Decimal]]:
    """Strictly simulate the target controller's supported lexical command grammar.

    This controller executes the pending G00 motion when the lexical ``F40`` token
    completes, then applies a trailing ``G90``. Consequently the approved final
    ``...F40G90`` command finishes its G91-relative move before restoring G90.
    """
    if type(machine_cycles) is not list or not machine_cycles:
        raise ValueError("machine_cycle must be a non-empty list")

    absolute_mode = True
    x = y = z = 0
    states: list[tuple[Decimal, Decimal, Decimal]] = []
    final_index = len(machine_cycles) - 1
    for cycle_index, cycle in enumerate(machine_cycles):
        if type(cycle) is not dict or set(cycle) != {"galvo_0"}:
            raise ValueError("each vendor cycle must contain exactly galvo_0")
        payload = cycle["galvo_0"]
        if type(payload) is not list or len(payload) != 3:
            raise ValueError("galvo_0 must contain mode, command, and patch reference")
        if type(payload[0]) is not int or payload[0] != 0:
            raise ValueError("galvo_0 mode must be integer zero")
        command = payload[1]
        reference = payload[2]
        if type(command) is not str:
            raise ValueError("vendor command must be a string")
        if (
            type(reference) is not list
            or len(reference) != 2
            or type(reference[0]) is not int
            or type(reference[1]) is not int
            or reference != [cycle_index, 0]
        ):
            raise ValueError("vendor cycle patch references must be sequential")

        cursor = 0
        if cycle_index == 0:
            cursor = _consume_vendor_literal(command, cursor, "G91")
            absolute_mode = False
        cursor = _consume_vendor_literal(command, cursor, "G00")
        delta_x, cursor = _consume_vendor_axis(command, cursor, "X")
        delta_y, cursor = _consume_vendor_axis(command, cursor, "Y")
        delta_z, cursor = _consume_vendor_axis(command, cursor, "Z")
        cursor = _consume_vendor_literal(command, cursor, "F40")

        # Vendor contract: reaching F40 completes motion with the mode active now.
        if absolute_mode:
            raise ValueError("vendor motion must execute in G91 relative mode")
        x += delta_x
        y += delta_y
        z += delta_z
        states.append(tuple(
            _decimal_from_thousandths(coordinate)
            for coordinate in (x, y, z)
        ))

        if cycle_index == final_index:
            cursor = _consume_vendor_literal(command, cursor, "G90")
            absolute_mode = True
        if cursor != len(command):
            raise ValueError("vendor command contains unsupported or misplaced tokens")

    if not absolute_mode:
        raise ValueError("vendor command sequence must restore G90 after final motion")
    return states


def extract_layer_number(path: Path) -> int:
    """Return the numeric layer component of a valid layer DXF filename."""
    match = _LAYER_FILENAME_RE.fullmatch(path.name)
    if match is None:
        raise ValueError(f"Invalid layer DXF filename: {path.name}")
    return int(match.group(1))


def discover_layer_dxf_files(
    dxf_dir: Path, *, require_contiguous: bool = True
) -> list[Path]:
    """Find layer DXFs, ordered by their numeric layer number."""
    if not dxf_dir.is_dir():
        raise ValueError(f"DXF directory does not exist or is not a directory: {dxf_dir}")

    numbered_files: list[tuple[int, Path]] = []
    for path in dxf_dir.iterdir():
        if not path.is_file():
            continue
        match = _LAYER_FILENAME_RE.fullmatch(path.name)
        if match is not None:
            numbered_files.append((int(match.group(1)), path))
    if not numbered_files:
        raise ValueError(f"No layer DXF files found in: {dxf_dir}")

    numbered_files.sort(key=lambda item: item[0])
    layer_numbers = [number for number, _ in numbered_files]
    if len(set(layer_numbers)) != len(layer_numbers):
        raise ValueError("Duplicate numeric layer numbers found")
    if require_contiguous and any(
        current != previous + 1
        for previous, current in zip(layer_numbers, layer_numbers[1:])
    ):
        raise ValueError("Layer numbers must be contiguous")
    return [path for _, path in numbered_files]


def read_dxf_lines(path: Path) -> np.ndarray:
    """Read LINE entities from a minimal ASCII DXF file."""
    raw_lines = path.read_text(encoding="ascii").splitlines()
    if len(raw_lines) % 2:
        raise ValueError("DXF contains a truncated group-code/value pair")

    entities: list[dict[int, str]] = []
    current_entity: dict[int, str] | None = None
    for index in range(0, len(raw_lines), 2):
        try:
            group_code = int(raw_lines[index].strip())
        except ValueError as exc:
            raise ValueError("DXF group code must be an integer") from exc
        value = raw_lines[index + 1]
        if group_code == 0:
            if current_entity is not None:
                entities.append(current_entity)
            current_entity = {0: value.strip()}
        elif current_entity is not None:
            current_entity[group_code] = value.strip()
    if current_entity is not None:
        entities.append(current_entity)

    rows: list[list[float]] = []
    for entity in entities:
        if entity[0] != "LINE":
            continue
        if any(code not in entity for code in _LINE_COORDINATE_CODES):
            raise ValueError("LINE entity is missing a required coordinate")
        try:
            row = [float(entity[code]) for code in _LINE_COORDINATE_CODES]
        except ValueError as exc:
            raise ValueError("LINE coordinate must be numeric") from exc
        if not np.isfinite(row).all():
            raise ValueError("LINE coordinate must be finite")
        rows.append(row)

    if not rows:
        raise ValueError("DXF contains no LINE entities")
    return np.array(rows, dtype=np.float64)


def _reject_duplicate_json_keys(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate metadata field: {key}")
        result[key] = value
    return result


def read_block_metadata(dxf_path: Path) -> BlockMetadata:
    """Read and strictly validate the v1 sidecar for one DXF."""
    sidecar_path = dxf_path.with_suffix(".blocks.json")
    no_follow_flag = getattr(os, "O_NOFOLLOW", None)
    nonblock_flag = getattr(os, "O_NONBLOCK", None)
    if type(no_follow_flag) is not int or no_follow_flag == 0:
        raise ValueError("secure metadata opening requires O_NOFOLLOW")
    if type(nonblock_flag) is not int or nonblock_flag == 0:
        raise ValueError("secure metadata opening requires O_NONBLOCK")
    flags = os.O_RDONLY | no_follow_flag | nonblock_flag
    fd: int | None = None
    try:
        fd = os.open(sidecar_path, flags)
        opened_stat = os.fstat(fd)
        if not stat.S_ISREG(opened_stat.st_mode):
            raise ValueError("block metadata must be a regular file")
        with os.fdopen(fd, "r", encoding="utf-8") as sidecar_file:
            fd = None
            document = json.load(
                sidecar_file,
                object_pairs_hook=_reject_duplicate_json_keys,
            )
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ValueError(f"invalid block metadata: {sidecar_path}") from exc
    finally:
        if fd is not None:
            os.close(fd)

    if type(document) is not dict or set(document) != {
        "version", "border_line_count", "blocks"
    }:
        raise ValueError("block metadata must contain exactly the v1 top-level fields")
    if type(document["version"]) is not int or document["version"] != 1:
        raise ValueError("block metadata version must be integer 1")
    border_line_count = document["border_line_count"]
    if type(border_line_count) is not int or border_line_count not in {0, 4}:
        raise ValueError("border_line_count must be integer 0 or 4")
    block_values = document["blocks"]
    if type(block_values) is not list or not block_values:
        raise ValueError("blocks must be a non-empty list")

    blocks: list[BlockDefinition] = []
    block_indices: set[int] = set()
    for block_value in block_values:
        if type(block_value) is not dict or set(block_value) != {
            "block_index", "center_x", "center_y", "line_count"
        }:
            raise ValueError("each block must contain exactly the required fields")
        block_index = block_value["block_index"]
        line_count = block_value["line_count"]
        if type(block_index) is not int or block_index < 0:
            raise ValueError("block_index must be a non-negative integer")
        if block_index in block_indices:
            raise ValueError("block indices must be unique")
        if type(line_count) is not int or line_count < 0:
            raise ValueError("line_count must be a non-negative integer")
        centers = (block_value["center_x"], block_value["center_y"])
        if any(type(center) not in (int, float) or not np.isfinite(center) for center in centers):
            raise ValueError("block centers must be finite numbers")
        block_indices.add(block_index)
        blocks.append(BlockDefinition(
            block_index=block_index,
            center_x=float(centers[0]),
            center_y=float(centers[1]),
            line_count=line_count,
        ))
    return BlockMetadata(
        border_line_count=border_line_count,
        blocks=tuple(blocks),
    )


def build_patch_plan(
    layer_files: list[Path],
    block_center_positioning: bool,
) -> list[PlannedPatch]:
    """Build source-coordinate patches for unblocked or block-local generation."""
    if type(layer_files) is not list or not layer_files:
        raise ValueError("layer_files must be a non-empty list")
    if type(block_center_positioning) is not bool:
        raise ValueError("block_center_positioning must be a bool")

    plan: list[PlannedPatch] = []
    for layer_index, layer_file in enumerate(layer_files):
        lines = read_dxf_lines(layer_file)
        if not block_center_positioning:
            plan.append(PlannedPatch(
                PatchPlacement(layer_index, 0.0, 0.0),
                lines,
            ))
            continue

        metadata = read_block_metadata(layer_file)
        cursor = metadata.border_line_count
        layer_plan: list[PlannedPatch] = []
        for block in metadata.blocks:
            next_cursor = cursor + block.line_count
            if block.line_count:
                layer_plan.append(PlannedPatch(
                    PatchPlacement(layer_index, block.center_x, block.center_y),
                    lines[cursor:next_cursor],
                ))
            cursor = next_cursor
        if cursor != len(lines):
            raise ValueError("block metadata counts must consume every DXF LINE")
        if not layer_plan:
            raise ValueError("each block-positioned layer must contain a non-empty block")
        plan.extend(layer_plan)

    if not plan:
        raise ValueError("patch plan must not be empty")
    return plan


def make_patch(
    lines: np.ndarray,
    layer_index: int,
    layer_step_um: float,
    center_x: float = 0.0,
    center_y: float = 0.0,
) -> np.ndarray:
    """Create one little-endian float32 patch at its negative Z depth in mm."""
    if not isinstance(lines, np.ndarray) or lines.ndim != 2 or lines.shape[1] != 6:
        raise ValueError("lines must be a 2-D array with exactly six columns")
    if isinstance(layer_index, bool) or not isinstance(layer_index, (int, np.integer)) or layer_index < 0:
        raise ValueError("layer_index must be a non-negative integer")
    step_um = _validate_layer_step(layer_step_um)
    for center in (center_x, center_y):
        if (
            isinstance(center, bool)
            or not isinstance(center, (int, float, np.integer, np.floating))
            or not np.isfinite(center)
        ):
            raise ValueError("patch centers must be finite numbers")

    try:
        patch = lines.astype(np.float64, copy=True)
    except (TypeError, ValueError) as exc:
        raise ValueError("lines must contain numeric coordinates") from exc
    patch[:, 0] -= center_x
    patch[:, 3] -= center_x
    patch[:, 1] -= center_y
    patch[:, 4] -= center_y
    z_depth_mm = -int(layer_index) * step_um / 1000.0
    patch[:, 2] = z_depth_mm
    patch[:, 5] = z_depth_mm
    if not np.isfinite(patch).all():
        raise ValueError("transformed patch contains non-finite values")
    patch = patch.astype("<f4")
    if not np.isfinite(patch).all():
        raise ValueError("transformed patch contains non-finite values")
    return patch


def _independently_validate_patch_geometry(
    patch_index: int,
    patch: np.ndarray,
    planned_patch: PlannedPatch,
    step_um: int,
) -> None:
    """Cross-check patch coordinates without calling the generation helper."""
    try:
        source_lines = np.asarray(planned_patch.lines, dtype=np.float64)
    except (TypeError, ValueError) as exc:
        raise ValueError(f"invalid source lines for patch {patch_index}") from exc
    if source_lines.shape != patch.shape or not np.isfinite(source_lines).all():
        raise ValueError(f"invalid source geometry for patch {patch_index}")

    xy_columns = [0, 1, 3, 4]
    offsets = np.array(
        [
            planned_patch.placement.center_x,
            planned_patch.placement.center_y,
            planned_patch.placement.center_x,
            planned_patch.placement.center_y,
        ],
        dtype=np.float64,
    )
    source_xy = source_lines[:, xy_columns]
    unquantized_local_xy = source_xy - offsets
    expected_local_xy = unquantized_local_xy.astype("<f4")
    if not np.array_equal(patch[:, xy_columns], expected_local_xy):
        raise ValueError(
            f"patch {patch_index} local XY does not independently match source geometry"
        )

    reconstructed_xy = patch[:, xy_columns].astype(np.float64) + offsets
    quantization_allowance = (
        np.abs(expected_local_xy.astype(np.float64) - unquantized_local_xy)
        + np.finfo(np.float64).eps * np.maximum(1.0, np.abs(source_xy)) * 4
    )
    if (
        not np.isfinite(reconstructed_xy).all()
        or np.any(np.abs(reconstructed_xy - source_xy) > quantization_allowance)
    ):
        raise ValueError(
            f"patch {patch_index} local XY plus center does not recover source XY"
        )

    expected_z = Decimal(-int(planned_patch.placement.layer_index) * step_um) / Decimal(1000)
    expected_patch_z = np.float32(float(expected_z))
    if not np.array_equal(
        patch[:, [2, 5]],
        np.full((patch.shape[0], 2), expected_patch_z, dtype="<f4"),
    ):
        raise ValueError(
            f"patch {patch_index} does not independently match layer-absolute Z"
        )


def _independently_validate_machine_motion(
    machine_cycles: object,
    planned_patches: list[PlannedPatch],
    actual_patches: list[np.ndarray],
    step_um: int,
) -> None:
    """Cross-check vendor-command state without generation helper reuse."""
    simulated_states = _simulate_vendor_machine_cycles(machine_cycles)
    if len(simulated_states) != len(planned_patches):
        raise ValueError("vendor command count does not match the patch plan")

    for patch_index, (state, planned_patch, patch) in enumerate(
        zip(simulated_states, planned_patches, actual_patches)
    ):
        placement = planned_patch.placement
        target_x = Decimal(f"{float(placement.center_x):.3f}")
        target_y = Decimal(f"{float(placement.center_y):.3f}")
        target_z = Decimal(-int(placement.layer_index) * step_um) / Decimal(1000)
        if not all(value.is_finite() for value in (*state, target_x, target_y, target_z)):
            raise ValueError("independent machine validation found non-finite state")
        if state[:2] != (target_x, target_y):
            raise ValueError(
                f"vendor cumulative XY does not reach patch {patch_index} center"
            )
        if state[2] != target_z:
            raise ValueError(
                f"vendor cumulative Z does not reach patch {patch_index} layer Z"
            )
        expected_patch_z = np.float32(float(state[2]))
        if not np.array_equal(
            patch[:, [2, 5]],
            np.full((patch.shape[0], 2), expected_patch_z, dtype="<f4"),
        ):
            raise ValueError(
                f"vendor cumulative Z does not match patch {patch_index} absolute Z"
            )


def validate_machine_directory(
    path: Path,
    planned_patches: list[PlannedPatch],
    layer_step_um: float,
    expected_first_laser_params: dict[str, object],
) -> None:
    """Reload and validate every generated artifact in a machine directory."""
    if not isinstance(planned_patches, list) or not planned_patches:
        raise ValueError("planned_patches must be a non-empty list")
    if any(not isinstance(planned_patch, PlannedPatch) for planned_patch in planned_patches):
        raise ValueError("planned_patches must contain PlannedPatch values")
    _validate_placements([planned_patch.placement for planned_patch in planned_patches])
    step_um = _validate_layer_step(layer_step_um)
    _validate_first_laser_params(expected_first_laser_params)
    if not path.is_dir():
        raise ValueError("machine directory does not exist")

    patches_path = path / "patches"
    try:
        actual_patch_names = {entry.name for entry in patches_path.iterdir()}
    except OSError as exc:
        raise ValueError("invalid patches directory") from exc
    expected_patch_names = {f"{index}_0.npy" for index in range(len(planned_patches))}
    if actual_patch_names != expected_patch_names:
        raise ValueError("patches directory does not contain the exact expected files")

    actual_patches: list[np.ndarray] = []
    for index, planned_patch in enumerate(planned_patches):
        try:
            patch = np.load(path / "patches" / f"{index}_0.npy", allow_pickle=False)
        except (OSError, ValueError) as exc:
            raise ValueError(f"invalid patch {index}") from exc
        if patch.dtype.str != "<f4":
            raise ValueError(f"patch {index} must use little-endian float32")
        if patch.ndim != 2 or patch.shape[0] <= 0 or patch.shape[1] != 6:
            raise ValueError(f"patch {index} must have shape (N, 6), N > 0")
        if not np.isfinite(patch).all():
            raise ValueError(f"patch {index} contains non-finite values")
        expected_patch = make_patch(
            planned_patch.lines,
            planned_patch.placement.layer_index,
            layer_step_um,
            planned_patch.placement.center_x,
            planned_patch.placement.center_y,
        )
        if not np.array_equal(patch, expected_patch):
            raise ValueError(f"patch {index} does not match the expected plan")
        _independently_validate_patch_geometry(index, patch, planned_patch, step_um)
        actual_patches.append(patch)

    try:
        document = json.loads((path / "machine.json").read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ValueError("invalid machine.json") from exc
    if not isinstance(document, dict) or list(document) != ["laser_params", "galvo_offset", "machine_cycle"]:
        raise ValueError("machine.json has invalid top-level structure")
    laser_params = document["laser_params"]
    if not isinstance(laser_params, list) or len(laser_params) != 3:
        raise ValueError("machine.json must contain exactly three laser groups")
    _validate_first_laser_params(laser_params[0])
    if laser_params[0] != expected_first_laser_params:
        raise ValueError("first laser group does not match the expected parameters")
    if laser_params[1:] != list(DEFAULT_LASER_PARAMS[1:]):
        raise ValueError("immutable laser groups do not match defaults")
    if document["galvo_offset"] != DEFAULT_GALVO_OFFSET:
        raise ValueError("invalid galvo offset")
    _independently_validate_machine_motion(
        document["machine_cycle"],
        planned_patches,
        actual_patches,
        step_um,
    )
    expected_cycles = build_machine_document(
        [planned_patch.placement for planned_patch in planned_patches],
        layer_step_um,
        laser_params[0],
    )["machine_cycle"]
    if document["machine_cycle"] != expected_cycles:
        raise ValueError("invalid machine cycles")


def generate_machine_file(
    dxf_dir: Path,
    output_name: str | None,
    layer_step_um: float,
    first_laser_params: dict[str, object],
    owner_token: str | None = None,
    block_center_positioning: bool = False,
) -> Path:
    """Generate, validate, and atomically publish a machine-file directory."""
    step_um = _validate_layer_step(layer_step_um)
    _validate_first_laser_params(first_laser_params)
    resolved_owner_token = uuid.uuid4().hex if owner_token is None else owner_token
    _validate_owner_token(resolved_owner_token)
    dxf_dir = dxf_dir.absolute()
    resolved_name = resolve_output_name(output_name)
    final_path = dxf_dir.parent / resolved_name
    temp_path = dxf_dir.parent / f".{resolved_name}.building"
    lock_path = dxf_dir.parent / f".{resolved_name}.lock"
    if os.path.lexists(final_path):
        raise FileExistsError(f"output path already exists: {final_path}")
    if os.path.lexists(temp_path):
        raise FileExistsError(f"temporary path already exists: {temp_path}")

    lock_fd: int | None = None
    lock_identity: tuple[int, int] | None = None
    temp_identity: tuple[int, int] | None = None
    try:
        lock_fd = os.open(lock_path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        lock_stat = os.fstat(lock_fd)
        lock_identity = (lock_stat.st_dev, lock_stat.st_ino)
        with os.fdopen(lock_fd, "w", encoding="ascii", closefd=False) as lock_file:
            lock_file.write(resolved_owner_token)
            lock_file.flush()
            os.fsync(lock_file.fileno())

        if os.path.lexists(final_path):
            raise FileExistsError(f"output path already exists: {final_path}")
        if os.path.lexists(temp_path):
            raise FileExistsError(f"temporary path already exists: {temp_path}")

        os.mkdir(temp_path)
        temp_stat = os.lstat(temp_path)
        temp_identity = (temp_stat.st_dev, temp_stat.st_ino)
        layer_files = discover_layer_dxf_files(dxf_dir)
        planned_patches = build_patch_plan(layer_files, block_center_positioning)
        (temp_path / "patches").mkdir()
        for index, planned_patch in enumerate(planned_patches):
            patch = make_patch(
                planned_patch.lines,
                planned_patch.placement.layer_index,
                step_um,
                planned_patch.placement.center_x,
                planned_patch.placement.center_y,
            )
            np.save(temp_path / "patches" / f"{index}_0.npy", patch)
        document = build_machine_document(
            [planned_patch.placement for planned_patch in planned_patches],
            step_um,
            first_laser_params,
        )
        (temp_path / "machine.json").write_text(
            json.dumps(document, ensure_ascii=False, allow_nan=False, indent=4),
            encoding="utf-8",
        )
        validate_machine_directory(
            temp_path,
            planned_patches,
            step_um,
            first_laser_params,
        )
        _clear_finder_hidden_flags(temp_path)
        if os.path.lexists(final_path):
            raise FileExistsError(f"output path already exists: {final_path}")
        _rename_no_replace(temp_path, final_path)
        temp_identity = None
    except Exception:
        if temp_identity is not None and _path_has_identity(temp_path, temp_identity):
            shutil.rmtree(temp_path)
        raise
    finally:
        try:
            if lock_identity is not None and _path_has_identity(lock_path, lock_identity):
                os.unlink(lock_path)
        finally:
            if lock_fd is not None:
                os.close(lock_fd)
    return final_path


def _path_has_identity(path: Path, identity: tuple[int, int]) -> bool:
    try:
        path_stat = os.lstat(path)
    except FileNotFoundError:
        return False
    return (path_stat.st_dev, path_stat.st_ino) == identity


def _clear_finder_hidden_flags(root: Path) -> None:
    """Make a generated package visible in Finder before publishing it."""
    if sys.platform != "darwin":
        return
    for path in [*root.rglob("*"), root]:
        path_stat = os.lstat(path)
        if path_stat.st_flags & stat.UF_HIDDEN:
            os.chflags(path, path_stat.st_flags & ~stat.UF_HIDDEN)


def _rename_no_replace(source: Path, destination: Path) -> None:
    """Atomically rename source while failing if destination already exists."""
    libc = ctypes.CDLL(None, use_errno=True)
    source_bytes = os.fsencode(source)
    destination_bytes = os.fsencode(destination)

    if sys.platform == "darwin":
        try:
            rename_function = libc.renamex_np
        except AttributeError as exc:
            raise OSError(
                errno.ENOSYS,
                "atomic no-replace rename is unavailable: libc.renamex_np is missing",
            ) from exc
        rename_function.argtypes = [ctypes.c_char_p, ctypes.c_char_p, ctypes.c_uint]
        rename_function.restype = ctypes.c_int
        arguments = (source_bytes, destination_bytes, 0x00000004)
    elif sys.platform.startswith("linux"):
        try:
            rename_function = libc.renameat2
        except AttributeError as exc:
            raise OSError(
                errno.ENOSYS,
                "atomic no-replace rename is unavailable: libc.renameat2 is missing",
            ) from exc
        rename_function.argtypes = [
            ctypes.c_int,
            ctypes.c_char_p,
            ctypes.c_int,
            ctypes.c_char_p,
            ctypes.c_uint,
        ]
        rename_function.restype = ctypes.c_int
        arguments = (-100, source_bytes, -100, destination_bytes, 1)
    else:
        raise OSError(
            errno.ENOTSUP,
            f"atomic no-replace rename is unsupported on platform {sys.platform}",
        )

    ctypes.set_errno(0)
    if rename_function(*arguments) == 0:
        return

    error_number = ctypes.get_errno() or errno.EIO
    if error_number in {errno.EEXIST, errno.ENOTEMPTY}:
        raise FileExistsError(
            error_number,
            os.strerror(error_number),
            os.fspath(destination),
        )
    raise OSError(
        error_number,
        f"atomic no-replace rename failed: {source} -> {destination}: "
        f"{os.strerror(error_number)}",
    )


def _build_argument_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Generate an atomic machine-file package from layered DXFs")
    parser.add_argument("dxf_dir", type=Path)
    parser.add_argument("output_name", nargs="?")
    parser.add_argument("--owner-token")
    parser.add_argument("--layer-step-um", type=_parse_layer_step_argument, default=3)
    parser.add_argument(
        "--block-center-positioning",
        action=argparse.BooleanOptionalAction,
        default=False,
    )
    integer_options = (
        ("--power", "power"),
        ("--frequency", "frequency"),
        ("--pulse-width-idx", "pulseWidthIdx"),
        ("--scan-speed", "scanSpeed"),
        ("--jump-vel", "jump_vel"),
        ("--jump-delay", "jump_delay"),
        ("--acc-scale", "accScale"),
        ("--corner-scale", "cornerScale"),
        ("--end-scale", "endScale"),
        ("--time-lag", "timeLag"),
        ("--laser-on-shift", "laserOnShift"),
        ("--delaseroff", "delaseroff"),
        ("--delaseron", "delaseron"),
    )
    for option, key in integer_options:
        parser.add_argument(option, dest=key, type=int, default=DEFAULT_LASER_PARAMS[0][key])
    parser.add_argument(
        "--scan-ahead",
        action=argparse.BooleanOptionalAction,
        default=DEFAULT_LASER_PARAMS[0]["scan_ahead"],
    )
    parser.add_argument(
        "--sky-writing",
        action=argparse.BooleanOptionalAction,
        default=DEFAULT_LASER_PARAMS[0]["sky_writing"],
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    args = _build_argument_parser().parse_args(argv)
    layer_count = len(discover_layer_dxf_files(args.dxf_dir))
    params = {
        key: getattr(args, key)
        for key in DEFAULT_LASER_PARAMS[0]
    }
    output_path = generate_machine_file(
        args.dxf_dir,
        args.output_name,
        args.layer_step_um,
        params,
        owner_token=args.owner_token,
        block_center_positioning=args.block_center_positioning,
    )
    patches = [
        np.load(path, allow_pickle=False)
        for path in sorted((output_path / "patches").glob("*_0.npy"), key=lambda path: int(path.stem.split("_")[0]))
    ]
    line_count = sum(patch.shape[0] for patch in patches)
    z_values = np.concatenate([patch[:, 2] for patch in patches])
    print("加工文件生成完成")
    print(f"层数: {layer_count}")
    print(f"补丁数: {len(patches)}")
    print(f"线段总数: {line_count}")
    print(f"Z 范围: {float(z_values.min()):.6f} ～ {float(z_values.max()):.6f} mm")
    print(f"输出目录: {output_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
