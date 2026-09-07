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
