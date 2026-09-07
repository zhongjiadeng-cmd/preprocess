# 大尺寸 TIFF Pillow 限制调整 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让项目的两个 TIFF 导入入口能够读取当前约 9900 万像素的可信图像，同时保留 120,000,000 像素的 Pillow 安全上限。

**Architecture:** 在每个直接使用 Pillow 的 Python 入口导入 `Image` 后立即设置相同的 `Image.MAX_IMAGE_PIXELS` 值。这样无需改变调用方、命令行参数或用户环境，并覆盖灰度分层和纹理转 DXF 两条导入路径。

**Tech Stack:** Python 3、Pillow、NumPy、现有 unittest/pytest 测试环境。

## Global Constraints

- Pillow 上限必须是 `120_000_000`。
- 不得设置为 `None`，继续保留对明显超大图片的保护。
- 不改变输入图像内容、尺寸、输出格式或现有命令行接口。
- 只修改两个导入入口及必要的测试；保留无关工作区文件。

---

### Task 1: 提高两个 TIFF 导入入口的 Pillow 像素上限

**Files:**
- Modify: `grayscale_layers.py`，在 `from PIL import Image` 后设置上限
- Modify: `texture_to_hatch_dxf.py`，在 `from PIL import Image` 后设置上限
- Test: `tests/test_large_tiff_limit.py`

**Interfaces:**
- Consumes: Pillow 的 `Image` 模块，以及现有 `Image.open(...)` 调用。
- Produces: 两个入口加载时都将 `Image.MAX_IMAGE_PIXELS` 设置为 `120_000_000`。

- [x] **Step 1: Write the failing regression test**

```python
import importlib
import unittest

from PIL import Image


class LargeTiffLimitTests(unittest.TestCase):
    def test_import_entrypoints_raise_pillow_limit(self):
        import grayscale_layers
        import texture_to_hatch_dxf

        importlib.reload(grayscale_layers)
        importlib.reload(texture_to_hatch_dxf)

        self.assertEqual(Image.MAX_IMAGE_PIXELS, 120_000_000)


if __name__ == "__main__":
    unittest.main()
```

- [x] **Step 2: Run the regression test and verify it fails before implementation**

Run:

```bash
python3 -m unittest discover -s tests -p 'test_large_tiff_limit.py' -v
```

Expected: FAIL because the current entrypoints leave Pillow at its default limit of `89_478_485` pixels.

- [x] **Step 3: Add the minimal implementation to both entrypoints**

Immediately after each existing `from PIL import Image`, add:

```python
MAX_IMAGE_PIXELS = 120_000_000
Image.MAX_IMAGE_PIXELS = MAX_IMAGE_PIXELS
```

Keep the setting before any `Image.open(...)` call. Do not add `warnings.simplefilter("ignore")` and do not set the value to `None`.

- [x] **Step 4: Run the regression test and open the user TIFF without the warning**

Run:

```bash
python3 -m unittest discover -s tests -p 'test_large_tiff_limit.py' -v
python3 -W error -c 'from PIL import Image; import grayscale_layers; im = Image.open("火花纹COL001214.tif"); im.load(); print(im.size, im.mode); im.close()'
```

Expected: the unit test passes, and the TIFF command prints `(10536, 9399) L` without a `DecompressionBombWarning`.

- [x] **Step 5: Run the existing Python test suite and inspect the final diff**

Run:

```bash
python3 -m unittest discover -s tests -p 'test_*.py'
git diff --check
git status --short
```

Result: 214 of 215 tests passed. One pre-existing unrelated test still fails because it cannot find the expected `var pipelineContent = MakeWorkspace` source text in `tests/test_texture_to_hatch_dxf.py`. `git diff --check` passes; the diff contains only the two Python entrypoint changes, the focused regression test, and this implementation plan/spec documentation.
