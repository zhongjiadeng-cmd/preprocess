from ezdxf.addons import odafc
import ezdxf
import numpy as np
import cv2
from skimage import io

def load_cad_file(filename):
    """
    加载 CAD 文件, 返回 ezdxf.drawing.Drawing 对象
    :param filename: CAD 文件名
    :note: 将 odafc.py 的 _get_odafc_path 函数中的默认 path 修改为 path = os.path.join(C_pkgs, 'ODA/ODAFileConverter.exe')
    """
    if filename.endswith('.dxf'):
        doc = ezdxf.readfile(filename) # type: ignore
    elif filename.endswith('.dwg'):
        doc = odafc.readfile(filename)
    else:
        raise ValueError('Unsupported file type.')
    return doc

def get_polylines(doc, min_len=0.0):
    """
    获取 CAD 文件中的所有多边形线, 
    :param doc: ezdxf.drawing.Drawing 对象
    :return: [[p1, p2, p3,...], ...], 多段线的点列表
    """
    polylines = []
    for entity in doc.modelspace().query('POLYLINE'):
        points = []
        for vertex in entity.vertices:
            points.append((vertex.dxf.location.x, vertex.dxf.location.y))
        np_points = np.array(points)
        poly_arc_len = np.sum(np.linalg.norm(np_points[1:] - np_points[:-1], axis=1))
        if poly_arc_len > min_len:
            polylines.append(points)
    return polylines


def get_line(doc, min_len=0.0):
    """
    获取 CAD 文件中的所有 line
    :param doc: ezdxf.drawing.Drawing 对象
    :return: [[line1], ...], 线段列表; ndarray 格式, n*6
    """
    lines = []
    for entity in doc.modelspace().query('LINE'):
        start = entity.dxf.start
        end = entity.dxf.end
        line_len = np.linalg.norm(np.array([end.x - start.x, end.y - start.y, end.z - start.z]))
        if line_len > min_len:
            lines.append([start.x, start.y, start.z, end.x, end.y, end.z])
    return np.array(lines)


def gen_image(polylines, pix_size=0.01, save_path=None, rotate=False):
    """
    生成图像
    :param polylines: 多条多段线
    :param save_path: 保存路径
    :param pix_size: 像素大小
    :param rotate: 是否旋转 90°
    :return: 图像
    """
    if rotate:
        new_polylines = []
        for polyline in polylines:
            new_polyline = []
            for pt in polyline:
                new_polyline.append((pt[1], -pt[0]))
            new_polylines.append(new_polyline)
        polylines = new_polylines
    # 1. 获取图像边界
    all_pts = [pt for polyline in polylines for pt in polyline]
    all_pts = np.array(all_pts)
    min_x, max_x = all_pts[:, 0].min(), all_pts[:, 0].max()
    min_y, max_y = all_pts[:, 1].min(), all_pts[:, 1].max()
    width = int((max_x - min_x) / pix_size) + 1
    height = int((max_y - min_y) / pix_size) + 1
    # 2. 生成图像
    img = np.ones((height, width), dtype=np.uint8) * 255  # 白色背景
    polyline_img_list = []
    for polyline in polylines:
        polyline_img = [(int((pt[0] - min_x) / pix_size), int((pt[1] - min_y) / pix_size)) for pt in polyline]
        polyline_img_list.append(polyline_img)
        cv2.polylines(img, [np.array(polyline_img, dtype=np.int32)], False, 0, 1) # type: ignore
    img = cv2.flip(img, 0)  # 上下翻转
    # 3. 保存图像
    if save_path is not None:
        io.imsave(save_path, img)
    return img, polyline_img_list