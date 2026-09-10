import shapely as shp
import numpy as np
from CommonLib.Transform2D import trans_matrix_2d, get_transformed_pts_2d


def fill_polygen(poly_in, gap, angle, edge_method, ref_z=0.0):
    """
    填充多边形内的扫描线段
    :param poly_in: 多边形对象, [[x1, y1], ...], 封闭多边形
    :param gap: 扫描线段的间距
    :param angle: 多边形的旋转角度, 单位: 度
    :param edge_method: 边的处理方式, "none" or "pre" or "post" or "both"
    :param ref_z: 参考高度, 用于计算扫描线段的高度
    :return: 扫描线段对象, [[x1, y1, x2, y2], ...]
    """
    # 构建矩形范围并旋转
    rot_mat = trans_matrix_2d(theta=angle, rad_mod=False)
    poly = get_transformed_pts_2d(rot_mat, poly_in)
    # 构建扫描线段
    min_x, max_x = np.min(poly[:, 0]), np.max(poly[:, 0])
    min_y, max_y = np.min(poly[:, 1]), np.max(poly[:, 1])
    y_arr = np.arange(min_y+gap, max_y, gap).reshape((-1, 1))
    lines = [[[min_x, y], [max_x, y]] for y in y_arr]
    # 基于 poly 构建 Polygon
    poly_shp = shp.Polygon(poly)
    # 基于 lines 构建 MultiLineString
    lines = shp.MultiLineString(lines)
    # 求解 MultiLineString 与 Polygon 的交集
    inter_lines = lines.intersection(poly_shp)
    # 旋转回去
    inter_lines = np.array([np.array(line.coords) for line in inter_lines.geoms]) # type: ignore
    pts_start = inter_lines[:, 0, :]
    pts_end = inter_lines[:, 1, :]
    rot_mat = trans_matrix_2d(theta=-angle, rad_mod=False)
    pts_start = get_transformed_pts_2d(rot_mat, pts_start)
    pts_end = get_transformed_pts_2d(rot_mat, pts_end)
    inter_lines[:, 0, :] = pts_start
    inter_lines[:, 1, :] = pts_end
    inter_lines = inter_lines.tolist()
    inter_lines = shp.MultiLineString(inter_lines)
    inter_lines = np.array([np.array(line.coords) for line in inter_lines.geoms if len(line.coords) == 2])
    out_lines = np.zeros((len(inter_lines), 6))
    out_lines[:, :2] = inter_lines[:, 0, :]
    out_lines[:, 3:5] = inter_lines[:, 1, :]
    if edge_method in ["pre", "both", "post"]:
        cnt = [[poly_in[i-1][0], poly_in[i-1][1], 0, poly_in[i][0], poly_in[i][1], 0] for i in range(1, len(poly_in))]
        cnt = cnt + [[poly_in[-1][0], poly_in[-1][1], 0, poly_in[0][0], poly_in[0][1], 0]]
        cnt = np.array(cnt)
        if edge_method == "pre":
            out_lines = np.concatenate((cnt, out_lines), axis=0)
        elif edge_method == "post":
            out_lines = np.concatenate((out_lines, cnt), axis=0)
        elif edge_method == "both":
            out_lines = np.concatenate((cnt, out_lines), axis=0)
            out_lines = np.concatenate((out_lines, cnt), axis=0)
    out_lines[:, 2] = ref_z
    out_lines[:, 5] = ref_z
    return out_lines



