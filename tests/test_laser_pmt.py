from __future__ import annotations

import json
import tempfile
from pathlib import Path

import numpy as np
import pytest

import dxf_to_machine_file as machine
import laser_pmt as pmt


def write_base_machine(root: Path) -> Path:
    package = root / "base-machine"
    patches = package / "patches"
    patches.mkdir(parents=True)
    placements = [
        machine.PatchPlacement(0, 2.0, 3.0),
        machine.PatchPlacement(1, 4.0, 5.0),
    ]
    arrays = [
        np.array([[0, 0, 0, 2, 0, 0], [0, 1, 0, 2, 1, 0]], dtype="<f4"),
        np.array([[0, 0, -0.003, 2, 0, -0.003]], dtype="<f4"),
    ]
    for index, array in enumerate(arrays):
        np.save(patches / f"{index}_0.npy", array)
    document = machine.build_machine_document(
        placements,
        3,
        dict(machine.DEFAULT_LASER_PARAMS[0]),
    )
    (package / "machine.json").write_text(json.dumps(document), encoding="utf-8")
    return package


def make_request(root: Path, base: Path, **overrides: object) -> pmt.LaserPmtRequest:
    values: dict[str, object] = {
        "base_machine_dir": base,
        "output_dir": root,
        "output_name": "LaserPMT_test",
        "workpiece_width": 30.0,
        "workpiece_height": 20.0,
        "columns": 2,
        "numbering": pmt.Numbering("test_", 1, 1, 4),
        "parameters": (
            pmt.ParameterValues("power", (20, 40)),
            pmt.ParameterValues("layerFeedUm", (2, 4)),
        ),
        "owner_token": "test-owner",
    }
    values.update(overrides)
    return pmt.LaserPmtRequest(**values)


def test_explicit_values_and_cartesian_order() -> None:
    parameters = (
        pmt.parse_explicit_values("power", [20, 40]),
        pmt.parse_explicit_values("scan_ahead", [True, False]),
        pmt.parse_explicit_values("layerFeedUm", [2, 5]),
    )
    combinations = pmt.expand_combinations(parameters)
    assert len(combinations) == 8
    assert combinations[:3] == (
        {"power": 20, "scan_ahead": True, "layerFeedUm": 2},
        {"power": 20, "scan_ahead": True, "layerFeedUm": 5},
        {"power": 20, "scan_ahead": False, "layerFeedUm": 2},
    )


def test_empty_parameter_list_creates_one_inherited_job() -> None:
    assert pmt.expand_combinations(()) == ({},)


@pytest.mark.parametrize(
    ("name", "values"),
    (("power", [1, 1]), ("scan_ahead", [1]), ("layerFeedUm", [0]), ("unknown", [1])),
)
def test_rejects_invalid_explicit_values(name: str, values: list[object]) -> None:
    with pytest.raises(ValueError):
        pmt.parse_explicit_values(name, values)


def test_rejects_duplicate_parameters_and_too_many_jobs() -> None:
    duplicate = pmt.ParameterValues("power", (1, 2))
    with pytest.raises(ValueError, match="unique"):
        pmt.expand_combinations((duplicate, duplicate))
    with pytest.raises(ValueError, match="at most"):
        pmt.expand_combinations((pmt.ParameterValues("power", tuple(range(1001))),))


def test_layout_uses_equal_outer_and_inner_gaps_and_left_aligns_last_row() -> None:
    with tempfile.TemporaryDirectory() as folder:
        base = pmt.load_base_machine(write_base_machine(Path(folder)))
        identifiers = ("1", "2", "3")
        layout = pmt.calculate_layout(base, identifiers, 10, 10, 2)
    assert base.width == pytest.approx(4)
    assert base.height == pytest.approx(2)
    assert layout.horizontal_gap == pytest.approx(2 / 3)
    assert layout.vertical_gap == pytest.approx(2)
    assert layout.cells[2].left == pytest.approx(layout.cells[0].left)


def test_layout_geometry_and_incomplete_row() -> None:
    with tempfile.TemporaryDirectory() as folder:
        base = pmt.load_base_machine(write_base_machine(Path(folder)))
        layout = pmt.calculate_layout(base, ("1", "2", "3"), 20, 20, 2)
    assert layout.rows == 2
    assert layout.columns == 2
    assert layout.horizontal_gap == pytest.approx((20 - 2 * base.width) / 3)
    assert layout.vertical_gap == pytest.approx((20 - 2 * base.height) / 3)
    assert layout.cells[2].column == 0
    assert layout.cells[2].left == pytest.approx(layout.cells[0].left)
    assert layout.cells[0].translate_y == pytest.approx(-layout.cells[0].top - base.max_y)


def test_layout_rejects_small_workpiece() -> None:
    with tempfile.TemporaryDirectory() as folder:
        base = pmt.load_base_machine(write_base_machine(Path(folder)))
        with pytest.raises(ValueError, match="too small"):
            pmt.calculate_layout(base, ("1", "2"), 5, 5, 2)


def test_generates_independent_numbered_files_and_distinct_all_motion() -> None:
    with tempfile.TemporaryDirectory() as folder:
        root = Path(folder)
        base_path = write_base_machine(root)
        result = pmt.generate_laser_pmt(make_request(root, base_path))

        layout = json.loads((result / "pmt-layout.json").read_text(encoding="utf-8"))
        assert len(layout["jobs"]) == 4
        assert layout["parameter_order"] == ["power", "layerFeedUm"]
        assert [job["identifier"] for job in layout["jobs"]] == [
            "test_0001", "test_0002", "test_0003", "test_0004"
        ]
        assert layout["jobs"][1]["patch_indices"] == [2, 3]

        first = json.loads((result / "test_0001machine.json").read_text(encoding="utf-8"))
        second = json.loads((result / "test_0002machine.json").read_text(encoding="utf-8"))
        all_document = json.loads((result / "allmachine.json").read_text(encoding="utf-8"))
        assert first["machine_cycle"][0]["galvo_0"][2] == [0, 0]
        assert second["machine_cycle"][0]["galvo_0"][2] == [2, 0]
        assert first["machine_cycle"][0]["galvo_0"][1] == second["machine_cycle"][0]["galvo_0"][1]
        assert all_document["machine_cycle"][2]["galvo_0"][1] != second["machine_cycle"][0]["galvo_0"][1]
        assert [cycle["galvo_0"][0] for cycle in all_document["machine_cycle"]] == [
            0, 0, 1, 1, 2, 2, 3, 3
        ]
        assert len(all_document["laser_params"]) == 6

        # Every job owns a separate patch set and layer feed changes the second layer Z.
        assert np.load(result / "patches" / "1_0.npy", allow_pickle=False)[0, 2] == pytest.approx(-0.002)
        assert np.load(result / "patches" / "3_0.npy", allow_pickle=False)[0, 2] == pytest.approx(-0.004)
        assert set(entry.name for entry in result.iterdir()) == {
            "patches", "allmachine.json", "parameter-map.csv", "pmt-layout.json",
            "test_0001machine.json", "test_0002machine.json",
            "test_0003machine.json", "test_0004machine.json",
        }


def test_output_conflict_does_not_overwrite() -> None:
    with tempfile.TemporaryDirectory() as folder:
        root = Path(folder)
        base = write_base_machine(root)
        collision = root / "LaserPMT_test"
        collision.mkdir()
        (collision / "sentinel").write_text("keep", encoding="utf-8")
        with pytest.raises(FileExistsError):
            pmt.generate_laser_pmt(make_request(root, base))
        assert (collision / "sentinel").read_text(encoding="utf-8") == "keep"


def test_load_request_rejects_duplicate_json_keys() -> None:
    with tempfile.TemporaryDirectory() as folder:
        path = Path(folder) / "request.json"
        path.write_text('{"a": 1, "a": 2}', encoding="utf-8")
        with pytest.raises(ValueError, match="duplicate"):
            pmt.load_request(path)


def test_cli_request_file_generates_package(capsys: pytest.CaptureFixture[str]) -> None:
    with tempfile.TemporaryDirectory() as folder:
        root = Path(folder)
        base = write_base_machine(root)
        request_path = root / "request.json"
        request_path.write_text(json.dumps({
            "base_machine_dir": str(base),
            "output_dir": str(root),
            "output_name": "LaserPMT_cli",
            "workpiece_width": 30,
            "workpiece_height": 20,
            "columns": 2,
            "numbering": {"prefix": "cli_", "start": 1, "increment": 1, "padding": 3},
            "parameters": [{"name": "power", "values": [10, 20]}],
            "owner_token": "cli-test-owner",
        }), encoding="utf-8")

        assert pmt.main([str(request_path)]) == 0
        output = root / "LaserPMT_cli"
        assert (output / "allmachine.json").is_file()
        assert (output / "cli_001machine.json").is_file()
        assert (output / "cli_002machine.json").is_file()
        assert "LaserPMT 生成完成" in capsys.readouterr().out


def test_base_loader_rejects_nonfirst_laser_index_and_layer_regression() -> None:
    with tempfile.TemporaryDirectory() as folder:
        root = Path(folder)
        base = write_base_machine(root)
        document = json.loads((base / "machine.json").read_text(encoding="utf-8"))
        document["machine_cycle"][0]["galvo_0"][0] = 1
        (base / "machine.json").write_text(json.dumps(document), encoding="utf-8")
        with pytest.raises(ValueError, match="first laser group"):
            pmt.load_base_machine(base)

    with tempfile.TemporaryDirectory() as folder:
        root = Path(folder)
        base = write_base_machine(root)
        document = json.loads((base / "machine.json").read_text(encoding="utf-8"))
        # Add a third cycle returning from layer 1 to layer 0, with a matching patch.
        document["machine_cycle"][-1]["galvo_0"][1] = (
            document["machine_cycle"][-1]["galvo_0"][1]
            .removeprefix("G91")
            .removesuffix("G90")
        )
        document["machine_cycle"].append({
            "galvo_0": [0, "G91G00X-2.000Y-2.000Z0.003F40G90", [2, 0]]
        })
        (base / "machine.json").write_text(json.dumps(document), encoding="utf-8")
        np.save(
            base / "patches" / "2_0.npy",
            np.array([[0, 0, 0, 2, 0, 0]], dtype="<f4"),
        )
        with pytest.raises(ValueError, match="earlier layer"):
            pmt.load_base_machine(base)
