"""Generate validated LaserPMT parameter-matrix machine packages."""

from __future__ import annotations

import argparse
import csv
from copy import deepcopy
from dataclasses import dataclass
from datetime import datetime
from decimal import Decimal
import itertools
import json
import math
import os
from pathlib import Path
import re
import shutil
import uuid

import numpy as np

import dxf_to_machine_file as machine


MAX_JOBS = 1_000
FORMAT_VERSION = 1
LAYER_FEED_KEY = "layerFeedUm"
BOOLEAN_KEYS = frozenset(("scan_ahead", "sky_writing"))
INTEGER_KEYS = frozenset(machine.DEFAULT_LASER_PARAMS[0]) - BOOLEAN_KEYS
SUPPORTED_KEYS = frozenset((*INTEGER_KEYS, *BOOLEAN_KEYS, LAYER_FEED_KEY))
_SAFE_PREFIX_RE = re.compile(r"[A-Za-z0-9_-]{0,64}")
_COMMAND_RE = re.compile(
    r"(?P<prefix>G91)?G00X(?P<x>-?(?:0|[1-9][0-9]*)\.[0-9]{3})"
    r"Y(?P<y>-?(?:0|[1-9][0-9]*)\.[0-9]{3})"
    r"Z(?P<z>-?(?:0|[1-9][0-9]*)\.[0-9]{3})F40(?P<suffix>G90)?"
)


@dataclass(frozen=True)
class ParameterValues:
    name: str
    values: tuple[object, ...]


@dataclass(frozen=True)
class Numbering:
    prefix: str
    start: int
    increment: int
    padding: int


@dataclass(frozen=True)
class BasePatch:
    index: int
    layer_index: int
    center_x: float
    center_y: float
    array: np.ndarray


@dataclass(frozen=True)
class BaseMachine:
    directory: Path
    first_laser_params: dict[str, object]
    patches: tuple[BasePatch, ...]
    layer_feed_um: int
    min_x: float
    min_y: float
    max_x: float
    max_y: float

    @property
    def width(self) -> float:
        return self.max_x - self.min_x

    @property
    def height(self) -> float:
        return self.max_y - self.min_y


@dataclass(frozen=True)
class LayoutCell:
    job_index: int
    identifier: str
    row: int
    column: int
    left: float
    top: float
    width: float
    height: float
    translate_x: float
    translate_y: float


@dataclass(frozen=True)
class MatrixLayout:
    workpiece_width: float
    workpiece_height: float
    rows: int
    columns: int
    horizontal_gap: float
    vertical_gap: float
    cells: tuple[LayoutCell, ...]


@dataclass(frozen=True)
class LaserPmtRequest:
    base_machine_dir: Path
    output_dir: Path
    output_name: str
    workpiece_width: float
    workpiece_height: float
    columns: int
    numbering: Numbering
    parameters: tuple[ParameterValues, ...]
    owner_token: str


def _reject_duplicate_json_keys(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def _load_json(path: Path) -> object:
    try:
        return json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=_reject_duplicate_json_keys,
        )
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ValueError(f"invalid JSON file: {path}") from exc


def _require_plain_int(value: object, label: str) -> int:
    if type(value) is not int:
        raise ValueError(f"{label} must be an integer")
    return value


def _require_finite_positive(value: object, label: str) -> float:
    if type(value) not in (int, float) or not math.isfinite(float(value)) or float(value) <= 0:
        raise ValueError(f"{label} must be a finite positive number")
    return float(value)


def parse_explicit_values(name: str, raw_values: object) -> ParameterValues:
    if name not in SUPPORTED_KEYS:
        raise ValueError(f"unsupported LaserPMT parameter: {name}")
    if type(raw_values) is not list or not raw_values:
        raise ValueError(f"{name} must contain at least one explicit value")
    parsed: list[object] = []
    for raw in raw_values:
        if name in BOOLEAN_KEYS:
            if type(raw) is not bool:
                raise ValueError(f"{name} values must be booleans")
            value: object = raw
        else:
            value = _require_plain_int(raw, f"{name} value")
            if name != LAYER_FEED_KEY and not 0 <= value <= 2_147_483_647:
                raise ValueError(f"{name} values must be between 0 and 2147483647")
            if name == LAYER_FEED_KEY and not 1 <= value <= machine.MAX_LAYER_STEP_UM:
                raise ValueError(
                    f"{name} values must be between 1 and {machine.MAX_LAYER_STEP_UM}"
                )
        if value in parsed:
            raise ValueError(f"{name} contains a repeated value")
        parsed.append(value)
    return ParameterValues(name, tuple(parsed))


def expand_combinations(parameters: tuple[ParameterValues, ...]) -> tuple[dict[str, object], ...]:
    names = [parameter.name for parameter in parameters]
    if len(names) != len(set(names)):
        raise ValueError("LaserPMT parameters must be unique")
    count = math.prod(len(parameter.values) for parameter in parameters)
    if count > MAX_JOBS:
        raise ValueError(f"LaserPMT supports at most {MAX_JOBS} jobs")
    products = itertools.product(*(parameter.values for parameter in parameters))
    return tuple(dict(zip(names, values)) for values in products)


def format_identifiers(numbering: Numbering, count: int) -> tuple[str, ...]:
    if type(numbering.prefix) is not str or _SAFE_PREFIX_RE.fullmatch(numbering.prefix) is None:
        raise ValueError("numbering prefix contains unsupported characters")
    start = _require_plain_int(numbering.start, "numbering start")
    increment = _require_plain_int(numbering.increment, "numbering increment")
    padding = _require_plain_int(numbering.padding, "numbering padding")
    if start < 0 or increment <= 0 or not 1 <= padding <= 18:
        raise ValueError("numbering values are outside their supported ranges")
    if type(count) is not int or not 1 <= count <= MAX_JOBS:
        raise ValueError("job count is outside its supported range")
    identifiers = tuple(
        f"{numbering.prefix}{start + index * increment:0{padding}d}"
        for index in range(count)
    )
    if len(set(identifiers)) != count:
        raise ValueError("numbering produces duplicate identifiers")
    return identifiers


def _simulate_cycles(cycles: object) -> tuple[tuple[Decimal, Decimal, Decimal], ...]:
    if type(cycles) is not list or not cycles:
        raise ValueError("machine_cycle must be a non-empty list")
    states: list[tuple[Decimal, Decimal, Decimal]] = []
    x = y = z = Decimal(0)
    final = len(cycles) - 1
    for index, cycle in enumerate(cycles):
        if type(cycle) is not dict or set(cycle) != {"galvo_0"}:
            raise ValueError("each cycle must contain exactly galvo_0")
        payload = cycle["galvo_0"]
        if type(payload) is not list or len(payload) != 3:
            raise ValueError("invalid galvo_0 payload")
        if type(payload[0]) is not int or payload[0] < 0:
            raise ValueError("laser parameter index must be non-negative")
        reference = payload[2]
        if (
            type(reference) is not list
            or len(reference) != 2
            or type(reference[0]) is not int
            or type(reference[1]) is not int
            or reference[0] < 0
            or reference[1] != 0
        ):
            raise ValueError("invalid patch reference")
        match = _COMMAND_RE.fullmatch(payload[1]) if type(payload[1]) is str else None
        if match is None:
            raise ValueError("unsupported vendor command")
        if (match.group("prefix") is not None) != (index in {0, final}):
            raise ValueError("G91 must prefix the first and final cycles")
        if (match.group("suffix") is not None) != (index == final):
            raise ValueError("G90 must suffix only the final cycle")
        x += Decimal(match.group("x"))
        y += Decimal(match.group("y"))
        z += Decimal(match.group("z"))
        states.append((x, y, z))
    return tuple(states)


def load_base_machine(directory: Path) -> BaseMachine:
    directory = Path(directory).absolute()
    if not directory.is_dir():
        raise ValueError("base machine directory does not exist")
    document = _load_json(directory / "machine.json")
    if type(document) is not dict or list(document) != ["laser_params", "galvo_offset", "machine_cycle"]:
        raise ValueError("base machine.json has an invalid top-level structure")
    laser_params = document["laser_params"]
    if type(laser_params) is not list or len(laser_params) != 3:
        raise ValueError("base machine.json must contain exactly three laser groups")
    machine._validate_first_laser_params(laser_params[0])
    if laser_params[1:] != list(machine.DEFAULT_LASER_PARAMS[1:]):
        raise ValueError("base immutable laser groups do not match defaults")
    if document["galvo_offset"] != machine.DEFAULT_GALVO_OFFSET:
        raise ValueError("base galvo_offset does not match the supported value")
    states = _simulate_cycles(document["machine_cycle"])
    references = [cycle["galvo_0"][2][0] for cycle in document["machine_cycle"]]
    if references != list(range(len(references))):
        raise ValueError("base patch references must be sequential")
    if any(cycle["galvo_0"][0] != 0 for cycle in document["machine_cycle"]):
        raise ValueError("base machine cycles must select the editable first laser group")
    patch_dir = directory / "patches"
    try:
        actual_names = {entry.name for entry in patch_dir.iterdir()}
    except OSError as exc:
        raise ValueError("base patches directory is invalid") from exc
    expected_names = {f"{index}_0.npy" for index in range(len(states))}
    if actual_names != expected_names:
        raise ValueError("base patches directory does not contain the exact referenced files")

    unique_z: list[Decimal] = []
    patches: list[BasePatch] = []
    min_x = min_y = math.inf
    max_x = max_y = -math.inf
    for index, state in enumerate(states):
        if unique_z and state[2] in unique_z and state[2] != unique_z[-1]:
            raise ValueError("base machine patch order cannot return to an earlier layer")
        if state[2] not in unique_z:
            if unique_z and state[2] >= unique_z[-1]:
                raise ValueError("base machine layers must descend in patch order")
            unique_z.append(state[2])
        layer_index = unique_z.index(state[2])
        try:
            patch = np.load(patch_dir / f"{index}_0.npy", allow_pickle=False)
        except (OSError, ValueError) as exc:
            raise ValueError(f"invalid base patch {index}") from exc
        if patch.dtype.str != "<f4" or patch.ndim != 2 or patch.shape[0] <= 0 or patch.shape[1] != 6:
            raise ValueError(f"invalid base patch shape or dtype: {index}")
        if not np.isfinite(patch).all():
            raise ValueError(f"base patch contains non-finite values: {index}")
        expected_z = np.float32(float(state[2]))
        if not np.array_equal(
            patch[:, [2, 5]],
            np.full((patch.shape[0], 2), expected_z, dtype="<f4"),
        ):
            raise ValueError(f"base patch Z does not match machine motion: {index}")
        center_x, center_y = float(state[0]), float(state[1])
        global_x = np.concatenate((patch[:, 0], patch[:, 3])).astype(np.float64) + center_x
        global_y = np.concatenate((patch[:, 1], patch[:, 4])).astype(np.float64) + center_y
        min_x = min(min_x, float(global_x.min()))
        max_x = max(max_x, float(global_x.max()))
        min_y = min(min_y, float(global_y.min()))
        max_y = max(max_y, float(global_y.max()))
        patches.append(BasePatch(index, layer_index, center_x, center_y, patch))

    if not unique_z or unique_z[0] != 0:
        raise ValueError("base machine must start at layer Z zero")
    if len(unique_z) == 1:
        layer_feed_um = 1
    else:
        differences = [unique_z[index - 1] - unique_z[index] for index in range(1, len(unique_z))]
        if any(difference <= 0 or difference != differences[0] for difference in differences):
            raise ValueError("base machine layers must use one uniform descent")
        micrometres = differences[0] * 1000
        if micrometres != micrometres.to_integral_value():
            raise ValueError("base layer feed must be a whole number of micrometres")
        layer_feed_um = machine._validate_layer_step(int(micrometres))
    if not all(math.isfinite(value) for value in (min_x, min_y, max_x, max_y)):
        raise ValueError("base machine footprint is not finite")
    if max_x <= min_x or max_y <= min_y:
        raise ValueError("base machine footprint must have positive width and height")
    return BaseMachine(
        directory,
        deepcopy(laser_params[0]),
        tuple(patches),
        layer_feed_um,
        min_x,
        min_y,
        max_x,
        max_y,
    )


def calculate_layout(
    base: BaseMachine,
    identifiers: tuple[str, ...],
    workpiece_width: float,
    workpiece_height: float,
    configured_columns: int,
) -> MatrixLayout:
    workpiece_width = _require_finite_positive(workpiece_width, "workpiece width")
    workpiece_height = _require_finite_positive(workpiece_height, "workpiece height")
    configured_columns = _require_plain_int(configured_columns, "columns")
    if configured_columns <= 0:
        raise ValueError("columns must be positive")
    if not identifiers:
        raise ValueError("layout must contain at least one job")
    columns = min(configured_columns, len(identifiers))
    rows = (len(identifiers) + columns - 1) // columns
    horizontal_gap = (workpiece_width - columns * base.width) / (columns + 1)
    vertical_gap = (workpiece_height - rows * base.height) / (rows + 1)
    if horizontal_gap < 0 or vertical_gap < 0:
        raise ValueError("workpiece is too small for the LaserPMT matrix")
    cells: list[LayoutCell] = []
    for index, identifier in enumerate(identifiers):
        row, column = divmod(index, columns)
        left = horizontal_gap + column * (base.width + horizontal_gap)
        top = vertical_gap + row * (base.height + vertical_gap)
        # Preview Y grows down, while machine Y grows up. Source max_y is its top edge.
        translate_x = left - base.min_x
        translate_y = -top - base.max_y
        cells.append(LayoutCell(
            index,
            identifier,
            row,
            column,
            left,
            top,
            base.width,
            base.height,
            translate_x,
            translate_y,
        ))
    return MatrixLayout(
        workpiece_width,
        workpiece_height,
        rows,
        columns,
        horizontal_gap,
        vertical_gap,
        tuple(cells),
    )


def _format_coordinate(value: float) -> float:
    return machine._rounded_machine_coordinate(float(value))


def _build_cycles(
    targets: list[tuple[float, float, float]],
    patch_indices: list[int],
    laser_indices: list[int],
) -> list[dict[str, object]]:
    if not targets or not (len(targets) == len(patch_indices) == len(laser_indices)):
        raise ValueError("cycle inputs must be non-empty and have matching lengths")
    previous_x = previous_y = previous_z = 0.0
    cycles: list[dict[str, object]] = []
    final = len(targets) - 1
    for index, ((target_x, target_y, target_z), patch_index, laser_index) in enumerate(
        zip(targets, patch_indices, laser_indices)
    ):
        target_x = _format_coordinate(target_x)
        target_y = _format_coordinate(target_y)
        target_z = _format_coordinate(target_z)
        delta_x = _format_coordinate(target_x - previous_x)
        delta_y = _format_coordinate(target_y - previous_y)
        delta_z = _format_coordinate(target_z - previous_z)
        command = f"G00X{delta_x:.3f}Y{delta_y:.3f}Z{delta_z:.3f}F40"
        if index in {0, final}:
            command = "G91" + command
        if index == final:
            command += "G90"
        cycles.append({"galvo_0": [laser_index, command, [patch_index, 0]]})
        previous_x, previous_y, previous_z = target_x, target_y, target_z
    return cycles


def _write_json(path: Path, document: object) -> None:
    path.write_text(
        json.dumps(document, ensure_ascii=False, allow_nan=False, indent=4),
        encoding="utf-8",
    )


def _make_request(document: object) -> LaserPmtRequest:
    if type(document) is not dict or set(document) != {
        "base_machine_dir", "output_dir", "output_name", "workpiece_width",
        "workpiece_height", "columns", "numbering", "parameters", "owner_token"
    }:
        raise ValueError("LaserPMT request must contain exactly the required fields")
    numbering_value = document["numbering"]
    if type(numbering_value) is not dict or set(numbering_value) != {
        "prefix", "start", "increment", "padding"
    }:
        raise ValueError("numbering must contain exactly the required fields")
    parameter_values = document["parameters"]
    if type(parameter_values) is not list:
        raise ValueError("parameters must be a list")
    parameters: list[ParameterValues] = []
    for entry in parameter_values:
        if type(entry) is not dict or set(entry) != {"name", "values"} or type(entry["name"]) is not str:
            raise ValueError("each parameter must contain name and values")
        parameters.append(parse_explicit_values(entry["name"], entry["values"]))
    owner_token = document["owner_token"]
    machine._validate_owner_token(owner_token)
    output_name = machine.resolve_output_name(document["output_name"], datetime.now())
    return LaserPmtRequest(
        Path(document["base_machine_dir"]),
        Path(document["output_dir"]),
        output_name,
        _require_finite_positive(document["workpiece_width"], "workpiece width"),
        _require_finite_positive(document["workpiece_height"], "workpiece height"),
        _require_plain_int(document["columns"], "columns"),
        Numbering(
            numbering_value["prefix"],
            numbering_value["start"],
            numbering_value["increment"],
            numbering_value["padding"],
        ),
        tuple(parameters),
        owner_token,
    )


def load_request(path: Path) -> LaserPmtRequest:
    return _make_request(_load_json(path))


def _job_values(
    base: BaseMachine,
    combination: dict[str, object],
) -> tuple[dict[str, object], int]:
    laser = deepcopy(base.first_laser_params)
    layer_feed = base.layer_feed_um
    for name, value in combination.items():
        if name == LAYER_FEED_KEY:
            layer_feed = machine._validate_layer_step(value)
        else:
            laser[name] = value
    machine._validate_first_laser_params(laser)
    return laser, layer_feed


def _build_layout_document(
    request: LaserPmtRequest,
    base: BaseMachine,
    layout: MatrixLayout,
    combinations: tuple[dict[str, object], ...],
    patch_ranges: tuple[tuple[int, ...], ...],
) -> dict[str, object]:
    jobs = []
    for cell, combination, owned in zip(layout.cells, combinations, patch_ranges):
        _, layer_feed = _job_values(base, combination)
        jobs.append({
            "index": cell.job_index,
            "identifier": cell.identifier,
            "row": cell.row,
            "column": cell.column,
            "bounds": {
                "left": cell.left, "top": cell.top,
                "width": cell.width, "height": cell.height,
            },
            "machine_translation": {"x": cell.translate_x, "y": cell.translate_y},
            "json_file": f"{cell.identifier}machine.json",
            "laser_param_index": cell.job_index,
            "layer_feed_um": layer_feed,
            "parameters": combination,
            "patch_indices": list(owned),
        })
    return {
        "format_version": FORMAT_VERSION,
        "coordinate_system": {
            "origin": "workpiece-top-left",
            "preview_x": "right", "preview_y": "down",
            "machine_x": "right", "machine_y": "up",
        },
        "workpiece": {"width": layout.workpiece_width, "height": layout.workpiece_height},
        "unit": {"width": base.width, "height": base.height},
        "matrix": {
            "rows": layout.rows, "columns": layout.columns,
            "horizontal_gap": layout.horizontal_gap,
            "vertical_gap": layout.vertical_gap,
        },
        "numbering": {
            "prefix": request.numbering.prefix,
            "start": request.numbering.start,
            "increment": request.numbering.increment,
            "padding": request.numbering.padding,
        },
        "parameter_order": [parameter.name for parameter in request.parameters],
        "jobs": jobs,
    }


def _write_csv(
    path: Path,
    parameter_order: list[str],
    layout_document: dict[str, object],
) -> None:
    fieldnames = [
        "identifier", "row", "column", "left_mm", "top_mm",
        "laser_param_index", "layer_feed_um", "json_file", "patch_indices",
        *parameter_order,
    ]
    with path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=fieldnames)
        writer.writeheader()
        for job in layout_document["jobs"]:
            row = {
                "identifier": job["identifier"],
                "row": job["row"],
                "column": job["column"],
                "left_mm": job["bounds"]["left"],
                "top_mm": job["bounds"]["top"],
                "laser_param_index": job["laser_param_index"],
                "layer_feed_um": job["layer_feed_um"],
                "json_file": job["json_file"],
                "patch_indices": ";".join(str(index) for index in job["patch_indices"]),
            }
            row.update(job["parameters"])
            writer.writerow(row)


def _validate_generated_package(
    path: Path,
    base: BaseMachine,
    layout_document: dict[str, object],
) -> None:
    jobs = layout_document["jobs"]
    patch_count = len(jobs) * len(base.patches)
    expected_root = {
        "patches", "allmachine.json", "parameter-map.csv", "pmt-layout.json",
        *(job["json_file"] for job in jobs),
    }
    if {entry.name for entry in path.iterdir()} != expected_root:
        raise ValueError("LaserPMT package contains unexpected or missing root files")
    if {entry.name for entry in (path / "patches").iterdir()} != {
        f"{index}_0.npy" for index in range(patch_count)
    }:
        raise ValueError("LaserPMT patches directory is incomplete")
    reloaded_layout = _load_json(path / "pmt-layout.json")
    if reloaded_layout != layout_document:
        raise ValueError("pmt-layout.json does not match the generation plan")
    try:
        with (path / "parameter-map.csv").open("r", encoding="utf-8", newline="") as stream:
            csv_rows = list(csv.DictReader(stream))
    except (OSError, UnicodeError, csv.Error) as exc:
        raise ValueError("parameter-map.csv is invalid") from exc
    if len(csv_rows) != len(jobs):
        raise ValueError("parameter-map.csv job count does not match the layout")
    for csv_row, job in zip(csv_rows, jobs):
        if (
            csv_row.get("identifier") != job["identifier"]
            or csv_row.get("json_file") != job["json_file"]
            or csv_row.get("patch_indices")
            != ";".join(str(index) for index in job["patch_indices"])
        ):
            raise ValueError("parameter-map.csv does not match the layout")
    all_document = _load_json(path / "allmachine.json")
    if type(all_document) is not dict or set(all_document) != {
        "laser_params", "galvo_offset", "machine_cycle"
    }:
        raise ValueError("allmachine.json has an invalid structure")
    if len(all_document["laser_params"]) != len(jobs) + 2:
        raise ValueError("allmachine.json has an invalid laser parameter count")
    all_states = _simulate_cycles(all_document["machine_cycle"])
    if len(all_states) != patch_count:
        raise ValueError("allmachine.json has an invalid cycle count")
    cursor = 0
    for job in jobs:
        numbered = _load_json(path / job["json_file"])
        numbered_states = _simulate_cycles(numbered["machine_cycle"])
        if len(numbered_states) != len(base.patches):
            raise ValueError("numbered JSON has an invalid cycle count")
        if numbered["laser_params"][1:] != list(machine.DEFAULT_LASER_PARAMS[1:]):
            raise ValueError("numbered JSON immutable laser groups are invalid")
        if numbered["laser_params"][0] != all_document["laser_params"][job["laser_param_index"]]:
            raise ValueError("numbered and all-machine laser parameters differ")
        for local_index, base_patch in enumerate(base.patches):
            global_index = job["patch_indices"][local_index]
            numbered_payload = numbered["machine_cycle"][local_index]["galvo_0"]
            all_payload = all_document["machine_cycle"][cursor]["galvo_0"]
            if numbered_payload[2] != [global_index, 0] or all_payload[2] != [global_index, 0]:
                raise ValueError("JSON patch ownership does not match the layout")
            if numbered_payload[0] != 0 or all_payload[0] != job["laser_param_index"]:
                raise ValueError("JSON laser parameter index does not match the layout")
            patch = np.load(path / "patches" / f"{global_index}_0.npy", allow_pickle=False)
            if patch.dtype.str != "<f4" or patch.shape != base_patch.array.shape:
                raise ValueError("generated patch shape or dtype is invalid")
            expected_z = np.float32(-base_patch.layer_index * job["layer_feed_um"] / 1000)
            if not np.array_equal(
                patch[:, [2, 5]],
                np.full((patch.shape[0], 2), expected_z, dtype="<f4"),
            ):
                raise ValueError("generated patch Z is invalid")
            if not np.array_equal(patch[:, [0, 1, 3, 4]], base_patch.array[:, [0, 1, 3, 4]]):
                raise ValueError("generated patch XY changed")
            expected_local = (
                Decimal(f"{base_patch.center_x:.3f}"),
                Decimal(f"{base_patch.center_y:.3f}"),
                Decimal(f"{float(expected_z):.3f}"),
            )
            if numbered_states[local_index] != expected_local:
                raise ValueError("numbered JSON is not independently local")
            expected_global = (
                Decimal(f"{base_patch.center_x + job['machine_translation']['x']:.3f}"),
                Decimal(f"{base_patch.center_y + job['machine_translation']['y']:.3f}"),
                Decimal(f"{float(expected_z):.3f}"),
            )
            if all_states[cursor] != expected_global:
                raise ValueError("allmachine.json does not reach the planned global target")
            cursor += 1


def generate_laser_pmt(request: LaserPmtRequest) -> Path:
    base = load_base_machine(request.base_machine_dir)
    combinations = expand_combinations(request.parameters)
    identifiers = format_identifiers(request.numbering, len(combinations))
    layout = calculate_layout(
        base,
        identifiers,
        request.workpiece_width,
        request.workpiece_height,
        request.columns,
    )
    output_dir = request.output_dir.absolute()
    if not output_dir.is_dir():
        raise ValueError("LaserPMT output parent does not exist")
    final_path = output_dir / request.output_name
    temp_path = output_dir / f".{request.output_name}.building"
    lock_path = output_dir / f".{request.output_name}.lock"
    for candidate, label in ((final_path, "output"), (temp_path, "temporary"), (lock_path, "lock")):
        if os.path.lexists(candidate):
            raise FileExistsError(f"LaserPMT {label} path already exists: {candidate}")

    lock_fd: int | None = None
    lock_identity: tuple[int, int] | None = None
    temp_identity: tuple[int, int] | None = None
    try:
        lock_fd = os.open(lock_path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        lock_stat = os.fstat(lock_fd)
        lock_identity = (lock_stat.st_dev, lock_stat.st_ino)
        with os.fdopen(lock_fd, "w", encoding="ascii", closefd=False) as lock_file:
            lock_file.write(request.owner_token)
            lock_file.flush()
            os.fsync(lock_file.fileno())
        os.mkdir(temp_path)
        temp_stat = os.lstat(temp_path)
        temp_identity = (temp_stat.st_dev, temp_stat.st_ino)
        patches_path = temp_path / "patches"
        patches_path.mkdir()

        patch_ranges: list[tuple[int, ...]] = []
        all_targets: list[tuple[float, float, float]] = []
        all_patch_indices: list[int] = []
        all_laser_indices: list[int] = []
        all_laser_params: list[dict[str, object]] = []
        for job_index, (cell, combination) in enumerate(zip(layout.cells, combinations)):
            laser_params, layer_feed = _job_values(base, combination)
            all_laser_params.append(deepcopy(laser_params))
            owned: list[int] = []
            local_targets: list[tuple[float, float, float]] = []
            for base_patch in base.patches:
                global_index = job_index * len(base.patches) + base_patch.index
                owned.append(global_index)
                patch = base_patch.array.copy()
                z = np.float32(-base_patch.layer_index * layer_feed / 1000)
                patch[:, 2] = z
                patch[:, 5] = z
                np.save(patches_path / f"{global_index}_0.npy", patch)
                local_target = (base_patch.center_x, base_patch.center_y, float(z))
                local_targets.append(local_target)
                all_targets.append((
                    base_patch.center_x + cell.translate_x,
                    base_patch.center_y + cell.translate_y,
                    float(z),
                ))
                all_patch_indices.append(global_index)
                all_laser_indices.append(job_index)
            patch_ranges.append(tuple(owned))
            numbered_document = {
                "laser_params": [deepcopy(laser_params), *deepcopy(machine.DEFAULT_LASER_PARAMS[1:])],
                "galvo_offset": deepcopy(machine.DEFAULT_GALVO_OFFSET),
                "machine_cycle": _build_cycles(
                    local_targets,
                    owned,
                    [0] * len(owned),
                ),
            }
            _write_json(temp_path / f"{cell.identifier}machine.json", numbered_document)

        all_document = {
            "laser_params": [*all_laser_params, *deepcopy(machine.DEFAULT_LASER_PARAMS[1:])],
            "galvo_offset": deepcopy(machine.DEFAULT_GALVO_OFFSET),
            "machine_cycle": _build_cycles(all_targets, all_patch_indices, all_laser_indices),
        }
        _write_json(temp_path / "allmachine.json", all_document)
        layout_document = _build_layout_document(
            request,
            base,
            layout,
            combinations,
            tuple(patch_ranges),
        )
        _write_json(temp_path / "pmt-layout.json", layout_document)
        _write_csv(
            temp_path / "parameter-map.csv",
            layout_document["parameter_order"],
            layout_document,
        )
        _validate_generated_package(temp_path, base, layout_document)
        machine._clear_finder_hidden_flags(temp_path)
        machine._rename_no_replace(temp_path, final_path)
        temp_identity = None
    except Exception:
        if temp_identity is not None and machine._path_has_identity(temp_path, temp_identity):
            shutil.rmtree(temp_path)
        raise
    finally:
        try:
            if lock_identity is not None and machine._path_has_identity(lock_path, lock_identity):
                os.unlink(lock_path)
        finally:
            if lock_fd is not None:
                os.close(lock_fd)
    return final_path


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Generate a LaserPMT parameter matrix")
    parser.add_argument("request", type=Path, help="UTF-8 JSON request file")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = _build_parser().parse_args(argv)
    request = load_request(args.request)
    result = generate_laser_pmt(request)
    print("LaserPMT 生成完成")
    print(f"输出目录: {result}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
