from __future__ import annotations

import csv
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


def make_workflow_request_document(
    root: Path,
    base: Path,
    *,
    include_timestamp: bool = False,
) -> dict[str, object]:
    parameters = dict(machine.DEFAULT_LASER_PARAMS[0])
    parameters[pmt.LAYER_FEED_KEY] = 3
    string_parameters = {
        name: "true" if value is True else "false" if value is False else str(value)
        for name, value in parameters.items()
    }
    source_targets = [
        {
            "type": "pmt", "id": "pmt-1", "number": 1,
            "bounds": {"left": 1, "top": 1, "width": 4, "height": 2},
            "was_manually_moved": True,
        },
        {
            "type": "pmt", "id": "pmt-3", "number": 3,
            "bounds": {"left": 10, "top": 1, "width": 4, "height": 2},
            "was_manually_moved": True,
        },
    ]
    if include_timestamp:
        source_targets.append({
            "type": "timestamp", "id": "timestamp-1", "creation_order": 1,
            "text": "08310712",
            "bounds": {"left": 1, "top": 10, "width": 20, "height": 4},
        })
    compiled_targets = [
        {
            "target_id": source["id"],
            "kind": source["type"],
            "identifier": (
                f"test_{source['number']:04d}"
                if source["type"] == "pmt"
                else f"timestamp-{source['creation_order']}"
            ),
            "bounds": source["bounds"],
            "parameters": (
                {**parameters, "power": 40}
                if source["id"] == "pmt-1"
                else dict(parameters)
            ),
            **(
                {"pmt_number": source["number"]}
                if source["type"] == "pmt"
                else {
                    "creation_order": source["creation_order"],
                    "timestamp_text": source["text"],
                }
            ),
        }
        for source in source_targets
    ]
    workflow = {
        "format_version": 2,
        "coordinate_system": {"origin": "workpiece-top-left"},
        "base_machine_identity": "sha256:test-machine",
        "workpiece": {"left": 0, "top": 0, "width": 30, "height": 20},
        "hatch_spacing": 0.1,
        "viewport": {"zoom": 1, "pan_x": 0, "pan_y": 0},
        "numbering_state": {
            "pmt_columns": 2,
            "next_pmt_number": 4,
            "next_creation_order": 2 if include_timestamp else 1,
            "prefix": "test_",
            "increment": 1,
            "padding": 4,
        },
        "base_node": {
            "id": "base",
            "position": {"x": -100, "y": 0},
            "parameters": string_parameters,
            "removed_parameters": [],
        },
        "parameter_nodes": [{
            "id": "power-node",
            "position": {"x": -100, "y": 80},
            "parameter_name": "power",
            "values_text": "40",
            "ports": [{"id": "power-40", "value": "40"}],
        }],
        "targets": source_targets,
        "connections": [{
            "id": "connection-1",
            "source_node_id": "power-node",
            "source_port_id": "power-40",
            "target_id": "pmt-1",
        }],
        "compiled_targets": compiled_targets,
        "generation": None,
    }
    return {
        "request_version": 2,
        "base_machine_dir": str(base),
        "output_dir": str(root),
        "output_name": "LaserPMT_workflow",
        "owner_token": "workflow-test-owner",
        "workflow": workflow,
    }


def test_loads_version_two_explicit_targets_with_number_gap() -> None:
    with tempfile.TemporaryDirectory() as folder:
        root = Path(folder)
        base = write_base_machine(root)
        request_path = root / "workflow-request.json"
        request_path.write_text(
            json.dumps(make_workflow_request_document(root, base)),
            encoding="utf-8",
        )

        request = pmt.load_request(request_path)

    assert isinstance(request, pmt.LaserPmtWorkflowRequest)
    assert [target.pmt_number for target in request.targets] == [1, 3]
    assert [target.identifier for target in request.targets] == ["test_0001", "test_0003"]
    assert [(target.left, target.top) for target in request.targets] == [(1, 1), (10, 1)]


def test_rejects_compiled_parameters_that_do_not_match_workflow_connections() -> None:
    with tempfile.TemporaryDirectory() as folder:
        root = Path(folder)
        base = write_base_machine(root)
        document = make_workflow_request_document(root, base)
        document["workflow"]["compiled_targets"][0]["parameters"]["power"] = 41

        with pytest.raises(ValueError, match="do not match"):
            pmt._make_request(document)


def test_generates_version_two_explicit_pmt_targets_without_filling_number_gap() -> None:
    with tempfile.TemporaryDirectory() as folder:
        root = Path(folder)
        base = write_base_machine(root)
        document = make_workflow_request_document(root, base)
        request = pmt._make_request(document)

        result = pmt.generate_laser_pmt(request)

        layout = json.loads((result / "pmt-layout.json").read_text(encoding="utf-8"))
        jobs = layout["generation"]["jobs"]
        assert layout["format_version"] == 2
        assert layout["targets"] == document["workflow"]["targets"]
        assert [job["identifier"] for job in jobs] == ["test_0001", "test_0003"]
        assert [(job["bounds"]["left"], job["bounds"]["top"]) for job in jobs] == [
            (1, 1), (10, 1)
        ]
        assert (result / "test_0001machine.json").is_file()
        assert not (result / "test_0002machine.json").exists()
        assert (result / "test_0003machine.json").is_file()
        all_document = json.loads((result / "allmachine.json").read_text(encoding="utf-8"))
        assert [cycle["galvo_0"][0] for cycle in all_document["machine_cycle"]] == [0, 0, 1, 1]
        with (result / "parameter-map.csv").open(encoding="utf-8", newline="") as stream:
            rows = list(csv.DictReader(stream))
        sources = json.loads(rows[0]["parameter_sources"])
        assert rows[0]["target_id"] == "pmt-1"
        assert sources["power"] == {
            "node_id": "power-node", "port_id": "power-40", "port_number": 1
        }
        assert sources["frequency"] == {
            "node_id": "base", "port_id": None, "port_number": None
        }


def test_generates_timestamp_after_all_pmts_without_independent_json() -> None:
    with tempfile.TemporaryDirectory() as folder:
        root = Path(folder)
        base = write_base_machine(root)
        document = make_workflow_request_document(root, base, include_timestamp=True)
        request = pmt._make_request(document)

        result = pmt.generate_laser_pmt(request)

        layout = json.loads((result / "pmt-layout.json").read_text(encoding="utf-8"))
        jobs = layout["generation"]["jobs"]
        assert [job["target_type"] for job in jobs] == ["pmt", "pmt", "timestamp"]
        assert jobs[-1]["json_file"] is None
        assert jobs[-1]["identifier"] == "timestamp-1"
        assert len(jobs[-1]["patch_indices"]) == 1
        reference = jobs[-1]["patch_indices"][0]
        patch = np.load(result / "patches" / f"{reference[0]}_{reference[1]}.npy")
        assert patch.shape[1] == 6
        assert np.array_equal(patch[:, 1], patch[:, 4])
        assert not (result / "timestamp-1machine.json").exists()
        all_document = json.loads((result / "allmachine.json").read_text(encoding="utf-8"))
        assert [cycle["galvo_0"][0] for cycle in all_document["machine_cycle"]] == [
            0, 0, 1, 1, 2
        ]


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


def test_patch_group_equality_requires_matching_dtype_shape_and_values() -> None:
    original = (np.array([[1, 2]], dtype="<f4"),)
    assert pmt._patch_groups_equal(original, (original[0].copy(),))
    assert not pmt._patch_groups_equal(original, (np.array([[1, 2]], dtype="<f8"),))
    assert not pmt._patch_groups_equal(original, (np.array([1, 2], dtype="<f4"),))
    assert not pmt._patch_groups_equal(original, (np.array([[1, 3]], dtype="<f4"),))


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
        assert [job["patch_indices"] for job in layout["jobs"]] == [
            [[0, 0], [0, 1]],
            [[1, 0], [1, 1]],
            [[0, 0], [0, 1]],
            [[1, 0], [1, 1]],
        ]

        first = json.loads((result / "test_0001machine.json").read_text(encoding="utf-8"))
        second = json.loads((result / "test_0002machine.json").read_text(encoding="utf-8"))
        all_document = json.loads((result / "allmachine.json").read_text(encoding="utf-8"))
        assert first["machine_cycle"][0]["galvo_0"][2] == [0, 0]
        assert first["machine_cycle"][1]["galvo_0"][2] == [0, 1]
        assert second["machine_cycle"][0]["galvo_0"][2] == [1, 0]
        assert second["machine_cycle"][1]["galvo_0"][2] == [1, 1]
        assert first["machine_cycle"][0]["galvo_0"][1] == second["machine_cycle"][0]["galvo_0"][1]
        assert all_document["machine_cycle"][2]["galvo_0"][1] != second["machine_cycle"][0]["galvo_0"][1]
        assert [cycle["galvo_0"][0] for cycle in all_document["machine_cycle"]] == [
            0, 0, 1, 1, 2, 2, 3, 3
        ]
        assert [cycle["galvo_0"][2] for cycle in all_document["machine_cycle"]] == [
            [0, 0], [0, 1], [1, 0], [1, 1],
            [0, 0], [0, 1], [1, 0], [1, 1],
        ]
        assert len(all_document["laser_params"]) == 6

        # Laser-only differences share patch groups; layer feed differences do not.
        assert {entry.name for entry in (result / "patches").iterdir()} == {
            "0_0.npy", "0_1.npy", "1_0.npy", "1_1.npy",
        }
        assert np.load(result / "patches" / "0_1.npy", allow_pickle=False)[0, 2] == pytest.approx(-0.002)
        assert np.load(result / "patches" / "1_1.npy", allow_pickle=False)[0, 2] == pytest.approx(-0.004)
        csv_lines = (result / "parameter-map.csv").read_text(encoding="utf-8").splitlines()
        assert "0_0;0_1" in csv_lines[1]
        assert "1_0;1_1" in csv_lines[2]
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


def test_load_request_accepts_utf8_bom() -> None:
    with tempfile.TemporaryDirectory() as folder:
        root = Path(folder)
        base = write_base_machine(root)
        path = root / "request.json"
        path.write_text(json.dumps({
            "base_machine_dir": str(base),
            "output_dir": str(root),
            "output_name": "LaserPMT_bom",
            "workpiece_width": 30,
            "workpiece_height": 20,
            "columns": 1,
            "numbering": {"prefix": "bom_", "start": 1, "increment": 1, "padding": 3},
            "parameters": [],
            "owner_token": "bom-request-test",
        }), encoding="utf-8-sig")

        request = pmt.load_request(path)
        assert request.output_name == "LaserPMT_bom"
        assert request.parameters == ()


def test_base_loader_accepts_utf8_bom_machine_json() -> None:
    with tempfile.TemporaryDirectory() as folder:
        base = write_base_machine(Path(folder))
        machine_path = base / "machine.json"
        document = json.loads(machine_path.read_text(encoding="utf-8"))
        machine_path.write_text(json.dumps(document), encoding="utf-8-sig")

        loaded = pmt.load_base_machine(base)
        assert len(loaded.patches) == 2


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
        first = json.loads((output / "cli_001machine.json").read_text(encoding="utf-8"))
        second = json.loads((output / "cli_002machine.json").read_text(encoding="utf-8"))
        assert [cycle["galvo_0"][2] for cycle in first["machine_cycle"]] == [[0, 0], [0, 1]]
        assert [cycle["galvo_0"][2] for cycle in second["machine_cycle"]] == [[0, 0], [0, 1]]
        assert {entry.name for entry in (output / "patches").iterdir()} == {
            "0_0.npy", "0_1.npy",
        }
        assert "LaserPMT 生成完成" in capsys.readouterr().out


def test_cli_inspect_base_prints_workflow_metadata(
    capsys: pytest.CaptureFixture[str],
) -> None:
    with tempfile.TemporaryDirectory() as folder:
        base = write_base_machine(Path(folder))

        assert pmt.main(["--inspect-base", str(base)]) == 0

        metadata = json.loads(capsys.readouterr().out)
        assert metadata["base_machine_identity"] == str(base.absolute())
        assert metadata["unit"] == {"width": 4.0, "height": 2.0}
        assert metadata["parameters"]["layerFeedUm"] == 3
        assert metadata["parameters"]["power"] == machine.DEFAULT_LASER_PARAMS[0]["power"]


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
