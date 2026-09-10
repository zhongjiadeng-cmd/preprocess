import numpy as np
from CommonLib.Geometry3D import shapely_to_cnts
from IslandPatch.Functions.functions import block_uv_cnts, get_closed_contour_pts
import shapely as shp
from shapely.ops import linemerge, unary_union
import cv2


def out_to_g_code(solution_list, proj_patched_list, save_path=None, local_mod=False):
    """
    将结果输出为 g-code
    :param solution_list: 逆解的列表，[(x, y, z, a, b, c), ...]; 当 local_mod 为 True 时，为 tool_pos_norm_list = [(x, y, z, i, j, k), ...]
    :param proj_patched_list: 振镜加工轨迹, [[[x1, y1], [x2, y2], ...], ...]
    :param save_path: 保存路径, 默认不保存
    :param local_mod: 输出是否为局部坐标系; 是则输出 xyzijk
    :return: g-code, str
                G00 XYZBC, 轴移动
                G00 XY, 振镜跳转
                G01 XY, 振镜出光
    """
    out_str = ''
    for i in range(len(proj_patched_list)):
        patch = proj_patched_list[i]
        solution = solution_list[i]
        x, y, z, a, b, c = solution
        if local_mod:
            out_str += f'G00X{x:.3f}Y{y:.3f}Z{z:.3f}A{a:.3f}B{b:.3f}C{c:.3f}\n'
        else:
            b = b *180.0/np.pi
            c = c *180.0/np.pi
            out_str += f'G00X{x:.3f}Y{y:.3f}Z{z:.3f}B{b:.3f}C{c:.3f}\n'
        for cnt in patch:
            x, y = cnt[0]
            out_str += f'G00X{x:.3f}Y{y:.3f}\n'
            for j in range(1, len(cnt)):
                x, y = cnt[j]
                out_str += f'G01X{x:.3f}Y{y:.3f}\n'
    if save_path is not None:
        with open(save_path, 'w') as f:
            f.write(out_str)
    return out_str


def gen_cnt_2d_by_cv(texture_big, texture_size, polyline_img_list, img_label, block_dist, approx):
    # 1. 将 texture_big 与 texture 中心对齐, 并融合到 texture 中
    new_texture = np.ones(texture_size, dtype=np.uint8) * 255
    min_w = min(texture_big.shape[1], texture_size[1])
    min_h = min(texture_big.shape[0], texture_size[0])
    big_cx = texture_big.shape[1] // 2
    big_cy = texture_big.shape[0] // 2
    new_cx = texture_size[1] // 2
    new_cy = texture_size[0] // 2
    new_texture[new_cy - min_h // 2:new_cy + min_h // 2, new_cx - min_w // 2:new_cx + min_w // 2] = texture_big[big_cy - min_h // 2:big_cy + min_h // 2, big_cx - min_w // 2:big_cx + min_w // 2]
    # 2. 纹理图和 mesh 融合
    texture = (255 - new_texture) * (img_label > 0).astype(np.uint8)
    # 3. 提取轮廓  & 轮廓简化 (只保留拐点)
    cnts, _ = cv2.findContours(texture, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_SIMPLE)
    cnts = [cv2.approxPolyDP(cnt, approx, True) for cnt in cnts]
    cnts = [cnt for cnt in cnts if len(cnt) > 1]
    cnts = [cnt[:, 0, :] for cnt in cnts]
    # 4. 去重
    inter_lines = [shp.LineString(cnt) for cnt in cnts]
    inter_lines = unary_union(inter_lines)
    inter_lines = linemerge(list(inter_lines.geoms), directed=True) # type: ignore
    # xy 谁的尺度大, 按谁排序
    dx, dy = inter_lines.bounds[0], inter_lines.bounds[1]
    if dx > dy:
        inter_lines = sorted(list(inter_lines.geoms), key=lambda ls: (ls.bounds[0], ls.bounds[1])) # type: ignore
    else:
        inter_lines = sorted(list(inter_lines.geoms), key=lambda ls: (ls.bounds[1], ls.bounds[0])) # type: ignore
    # 5. 转 cnts
    cnts = shapely_to_cnts(inter_lines)
    # 6. 基于 img_label, 在三角形边缘将 cnts 打断为多段
    cnts = block_uv_cnts(cnts, img_label, block_dist=block_dist)
    return cnts


def gen_cnt_2d_by_shp(texture_big, texture_size, polyline_img_list, img_label, block_dist):
    # 1. 将 polyline_img_list 平移到与 texture 中心对齐
    big_cx = texture_big.shape[1] // 2
    big_cy = texture_big.shape[0] // 2
    new_cx = texture_size[1] // 2
    new_cy = texture_size[0] // 2
    move_x = new_cx - big_cx
    move_y = new_cy - big_cy
    polyline_img_list = [np.array(polyline) + np.array([move_x, move_y]) for polyline in polyline_img_list]
    # 2. 基于 shapely 构建轮廓多边形
    contours, external_idx_list, comp_idx_list = get_closed_contour_pts((img_label > 0).astype(np.uint8)*255)
    poly_list = []
    for itm in comp_idx_list:
        itm = itm[0]
        poly_list.append(shp.Polygon(shell=contours[itm[0]], holes=[contours[j] for j in itm[1:]]))
    poly = shp.MultiPolygon(poly_list)
    #  3. 基于 shapely 构建多段线
    lines = shp.MultiLineString([shp.LineString(polyline) for polyline in polyline_img_list])
    inter_lines = lines.intersection(poly)
    # 4. 线段合并
    inter_lines = [line for line in inter_lines.geoms if line.geom_type == 'LineString']  # type: ignore
    inter_lines = linemerge(inter_lines, directed=True)  # type: ignore # 连接端点
    # 5. xy 谁的尺度大, 按谁排序
    dx, dy = inter_lines.bounds[2] - inter_lines.bounds[0], inter_lines.bounds[3] - inter_lines.bounds[1]
    if dx > dy:
        inter_lines = sorted(list(inter_lines.geoms), key=lambda ls: (ls.bounds[0], ls.bounds[1])) # type: ignore
    else:
        inter_lines = sorted(list(inter_lines.geoms), key=lambda ls: (ls.bounds[1], ls.bounds[0])) # type: ignore
    # 6. 转 cnts
    cnts = shapely_to_cnts(inter_lines)
    # 7. 基于 img_label, 在三角形边缘将 cnts 打断为多段
    cnts = block_uv_cnts(cnts, img_label, block_dist=block_dist)
    return cnts