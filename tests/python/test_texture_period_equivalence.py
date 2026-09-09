"""Preserve the original exact/approximate period and seam selection rules."""
import numpy as np
import pytest

import texture_to_hatch_dxf as hatch


def reference_period(source, axis, threshold):
    packed = np.packbits(source, axis=0).T if axis == 1 else np.packbits(source, axis=1)
    _, tokens = np.unique(packed, axis=0, return_inverse=True)
    scores = [float(np.mean(tokens[p:] == tokens[:-p]))
              for p in range(2, len(tokens) // 2 + 1)]
    for i, score in enumerate(scores):
        if score >= 1.0 - 1e-12:
            return i + 2, score
    for i, score in enumerate(scores):
        left = scores[i - 1] if i else -1.0
        right = scores[i + 1] if i + 1 < len(scores) else -1.0
        if score >= threshold and score >= left and score >= right:
            return i + 2, score
    raise hatch.RepeatPeriodNotFoundError()


@pytest.mark.parametrize('axis', [0, 1])
@pytest.mark.parametrize('shape', [(0, 9), (9, 0), (1, 1), (7, 13), (17, 33), (129, 257)])
def test_tokens_preserve_exact_row_classes_for_strides_and_padding(axis, shape):
    rng = np.random.default_rng(503)
    source = rng.random(shape) < 0.4
    for image in (source, source[::-1, ::-1], source.T, np.tile(source, (2, 2)),
                  np.zeros(shape, dtype=bool), np.ones(shape, dtype=bool)):
        packed = np.packbits(image, axis=0).T if axis == 1 else np.packbits(image, axis=1)
        _, expected = np.unique(packed, axis=0, return_inverse=True)
        actual = hatch._axis_tokens(image, axis)
        assert actual.shape == expected.shape
        pairs = set(zip(expected.tolist(), actual.tolist()))
        assert len(pairs) == len(set(expected.tolist())) == len(set(actual.tolist()))


@pytest.mark.parametrize('axis', [0, 1])
def test_period_matches_original_rule_for_noise_and_exact_patterns(axis):
    rng = np.random.default_rng(391)
    for _ in range(60):
        source = rng.random((8, 16)) < rng.uniform(0.05, 0.95)
        for image in (source, np.tile(source, (3, 3)), np.zeros((8, 16), dtype=bool)):
            for threshold in (0.3, 0.98):
                try:
                    expected = reference_period(image, axis, threshold)
                except hatch.RepeatPeriodNotFoundError:
                    with pytest.raises(hatch.RepeatPeriodNotFoundError):
                        hatch._detect_axis_period(image, axis, min_similarity=threshold)
                else:
                    assert hatch._detect_axis_period(image, axis, min_similarity=threshold) == expected


@pytest.mark.parametrize('axis', [0, 1])
def test_seam_matches_original_pixels_and_tie_breaking_across_chunks(axis):
    rng = np.random.default_rng(917)
    for shape in ((7, 11), (259, 513), (514, 257)):
        for source in (rng.random(shape) < 0.6, np.ones(shape, dtype=bool), np.zeros(shape, dtype=bool)):
            length = shape[axis]
            for period in (2, 3, 7, length // 2, length):
                best = (0, float('inf'))
                for phase in range(period):
                    positions = np.arange(phase, length, period)
                    positions = positions[positions > 0]
                    if not positions.size:
                        continue
                    boundary = (source[:, positions] & source[:, positions - 1] if axis == 1
                                else source[positions, :] & source[positions - 1, :])
                    score = float(np.mean(boundary))
                    if score < best[1]:
                        best = phase, score
                assert hatch._best_seam(source, period, axis) == best
