import numpy as np
import os
import pyvista as pv

from CommonLib.pvViewer import draw_pv_axes, draw_pv_segments

depth = 0.13/40
data_root = r"C:\Users\woodsyao\Desktop\machine_file_20260812_103747\machine_file_20260812_103747\patches"
patches = []
for i in range(40):
    patch_path = os.path.join(data_root, f"{i}_0.npy")
    patch = np.load(patch_path)
    # 转 float 64
    patch = np.astype(patch, np.float64)
    # 第 2 和 5 列加上 i*depth
    # patch[:, 2] += float(i)*depth
    # patch[:, 5] += float(i)*depth
    patches.append(patch)
# 合并 patches
patches = np.concatenate(patches, axis=0)
pl = pv.Plotter()
draw_pv_axes(pl)
draw_pv_segments(pl, patches)
pl.show()




