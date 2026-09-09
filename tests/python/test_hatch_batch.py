import json
from pathlib import Path

import numpy as np
from PIL import Image
import pytest

import texture_to_hatch_dxf as hatch


def job(source, output, angle):
    return [str(source), str(output), '--size', '2', '--spacing', '.1',
            '--angle', str(angle), '--blocks', '4', '--seed', '17', '--max-block-area', '2',
            '--boundary-blur', '.02', '--preview-output', str(output.with_suffix('.preview.png'))]


def test_batch_matches_individual_outputs_and_order(tmp_path):
    source = tmp_path / 'texture.tiff'
    Image.fromarray(np.tile(np.array([[0, 255], [255, 0]], dtype=np.uint8), (10, 10))).save(source, dpi=(254, 254))
    jobs = []
    for index, angle in enumerate((0, 37, 90)):
        hatch.main(job(source, tmp_path / f'single-{index}.dxf', angle))
        jobs.append(job(source, tmp_path / f'batch-{index}.dxf', angle))
    request = tmp_path / 'request.json'
    request.write_text(json.dumps(jobs))
    hatch.main([str(request), '--batch'])
    for index in range(3):
        for extension in ('.dxf', '.blocks.json', '.preview.png'):
            assert (tmp_path / f'single-{index}{extension}').read_bytes() == (tmp_path / f'batch-{index}{extension}').read_bytes()


@pytest.mark.parametrize('jobs', [[], [[]], [[12]], [['request.json', '--batch']], [['source', '--inspect-image']]])
def test_invalid_or_nested_batch_is_rejected(tmp_path, jobs):
    request = tmp_path / 'request.json'
    request.write_text(json.dumps(jobs))
    with pytest.raises(ValueError):
        hatch.main([str(request), '--batch'])


def test_invalid_later_arguments_are_checked_before_any_output(tmp_path):
    request = tmp_path / 'request.json'
    output = tmp_path / 'first.dxf'
    request.write_text(json.dumps([job(tmp_path / 'missing.tiff', output, 0), ['missing.tiff', 'second.dxf']]))
    with pytest.raises(SystemExit):
        hatch.main([str(request), '--batch'])
    assert not output.exists()


def test_failed_job_does_not_start_later_jobs(tmp_path):
    request = tmp_path / 'request.json'
    output = tmp_path / 'later.dxf'
    request.write_text(json.dumps([job(tmp_path / 'missing.tiff', tmp_path / 'first.dxf', 0),
                                   job(tmp_path / 'missing.tiff', output, 0)]))
    with pytest.raises(FileNotFoundError):
        hatch.main([str(request), '--batch'])
    assert not output.exists()
