from CommonLib.ezdxf_func import load_cad_file, get_line
import numpy as np
import pyvista as pv
from CommonLib.pvViewer import draw_pv_axes, draw_pv_segments

file_name = r"C:\Users\woodsyao\Desktop\layer_1_gray_lt_255.dxf"

doc = load_cad_file(file_name)
lines = get_line(doc)


# viewPlane(file_path, workpiece_mode)