"""Measure a real four-stage pipeline in a new output directory.

Example: python3 tools/profile_preprocessing.py input.tiff /tmp/new-profile --layers 3
The source is read-only. The output directory must not already exist.
"""
from __future__ import annotations

import argparse
import cProfile
import json
from pathlib import Path
import resource
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'src' / 'python'))
import grayscale_layers
import texture_to_hatch_dxf as hatch
import dxf_to_machine_file as machine
import laser_pmt as pmt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--layers', type=int, default=3)
    parser.add_argument('--size', type=float, default=10)
    parser.add_argument('--spacing', type=float, default=.02)
    parser.add_argument('--dpi', type=float, default=300)
    parser.add_argument('--profile', action='store_true', help='Also collect cProfile data (adds overhead)')
    args = parser.parse_args()
    if not args.input.is_file():
        parser.error('input must be an existing image')
    if not 1 <= args.layers <= 255 or args.size <= 0 or args.spacing <= 0 or args.dpi <= 0:
        parser.error('invalid layers, size, spacing or DPI')
    args.output.mkdir(parents=True, exist_ok=False)
    report = {'input': str(args.input.resolve()), 'layers': args.layers,
              'size_mm': args.size, 'spacing_mm': args.spacing,
              'profile_enabled': args.profile, 'stages': []}
    profiler = cProfile.Profile() if args.profile else None

    def measure(name, operation):
        started = time.perf_counter()
        try:
            return operation()
        finally:
            rss = resource.getrusage(resource.RUSAGE_SELF).ru_maxrss
            rss_bytes = rss if sys.platform == 'darwin' else rss * 1024
            report['stages'].append({'stage': name, 'seconds': time.perf_counter() - started,
                                     'process_high_water_rss_mib': rss_bytes / 1048576})

    if profiler:
        profiler.enable()
    try:
        layers = measure('grayscale', lambda: grayscale_layers.split_grayscale_layers(
            args.input, args.output / 'layers', args.layers))
        dxf_dir = args.output / 'dxfs'
        dxf_dir.mkdir()
        jobs = [[str(layer), str(dxf_dir / f'layer_{i + 1:03d}_hatch.dxf'),
                 '--size', str(args.size), '--spacing', str(args.spacing), '--dpi', str(args.dpi),
                 '--seed', str(12345 + (i + 1) * 7919)] for i, layer in enumerate(layers)]
        request = args.output / 'hatch-request.json'
        request.write_text(json.dumps(jobs), encoding='utf-8')
        measure('hatch_batch', lambda: hatch.main([str(request), '--batch']))
        base = measure('machine', lambda: machine.generate_machine_file(
            dxf_dir, 'machine', 3, dict(machine.DEFAULT_LASER_PARAMS[0]), block_center_positioning=True))
        request = pmt.LaserPmtRequest(
            base, args.output, 'PMT', args.size * 4, args.size * 4, 3,
            pmt.Numbering('test_', 1, 1, 4), (pmt.ParameterValues('power', (20, 30, 40)),), 'profile')
        measure('pmt', lambda: pmt.generate_laser_pmt(request))
        report['success'] = True
    except BaseException as error:
        report['success'] = False
        report['error'] = str(error)
        raise
    finally:
        if profiler:
            profiler.disable()
            profiler.dump_stats(str(args.output / 'pipeline.prof'))
        (args.output / 'performance.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
