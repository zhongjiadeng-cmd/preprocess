import numpy as np

def get_intersection_points(value, circle, axis_h=False):
    """
    求水平或竖直的直线与圆的交点
    :param value: 直线的截距
    :param circle: 圆的圆心坐标和半径, shape=(3,)
    :param axis_h: 是否为水平直线
    :return: 交点坐标, shape=(2,2)
    """
    # 1. 判断直线是否与圆相交
    cx, cy, r = circle
    if axis_h:
        x = cx
        y = value
    else:
        x = value
        y = cy
    dist = np.sqrt((x - cx) ** 2 + (y - cy) ** 2)
    if dist > r:
        return None
    # 2. 求解交点
    if axis_h:
        y1 = y2 = y
        delta_x = np.sqrt(r ** 2 - (y - cy) ** 2)
        x1 = cx - delta_x
        x2 = cx + delta_x
    else:
        x1 = x2 = x
        delta_y = np.sqrt(r ** 2 - (x - cx) ** 2)
        y1 = cy - delta_y
        y2 = cy + delta_y
    return np.array([[x1, y1], [x2, y2]])

def rect_as_pts(rect_in):
    """
    将矩形的四个顶点坐标转换为列表
    :param rect_in: 矩形的 (cx, cy, w, h)
    """
    cx, cy, w, h = rect_in
    pts = [(cx - w/2, cy - h/2), (cx + w/2, cy - h/2), (cx + w/2, cy + h/2), (cx - w/2, cy + h/2)]
    return pts

def is_rect_in_circle(rect_pts, circle, inner_offset=3.0):
    """
    判断矩形是否在圆内
    :param rect_pts: 矩形的四个顶点坐标, shape=(4, 2)
    :param circle: 圆的圆心坐标和半径, shape=(3,)
    :param inner_offset: 圆内偏移量
    """
    cx, cy, r = circle
    rect_pts = np.array(rect_pts)
    dist = np.linalg.norm(rect_pts - np.array([cx, cy]), axis=1)
    return np.all(dist <= r - inner_offset)

def gen_wafer_cut_lines(wafer_circle_in, center_offset_in, die_size_in, die_spacing_in):
    """
    生成切割线
    :param wafer_circle_in: wafer 圆心坐标和半径, shape=(3,)
    :param center_offset_in: 矩形中心相对于 wafer 中心的偏移量
    :param die_size_in: die 尺寸
    :param die_spacing_in: die 间距
    :return:
    """
    # 1. 计算矩形中心坐标
    cx, cy, r = wafer_circle_in
    w, h = die_size_in
    w_dist = w + die_spacing_in[0]
    h_dist = h + die_spacing_in[1]
    rect_cx = cx + center_offset_in[0]
    rect_cy = cy + center_offset_in[1]
    # 2. 遍历行列, 生成切割线
    num_w = int(r // w_dist + 5)
    num_h = int(r // h_dist + 5)
    channel_x = rect_cx + w_dist / 2
    channel_y = rect_cy + h_dist / 2
    h_lines, v_lines = [], []
    for i in range(-num_w, num_w+1, 1):
        tmp_x = channel_x + i * w_dist
        if tmp_x < cx - r or tmp_x > cx + r:
            continue
        v_lines.append(get_intersection_points(tmp_x, wafer_circle_in, axis_h=False))
    for j in range(-num_h, num_h+1, 1):
        tmp_y = channel_y + j * h_dist
        if tmp_y < cy - r or tmp_y > cy + r:
            continue
        h_lines.append(get_intersection_points(tmp_y, wafer_circle_in, axis_h=True))
    return h_lines, v_lines

def gen_wafer_rects(wafer_circle_in, center_offset_in, die_size_in, die_spacing_in):
    """
    生成 wafer 上的矩形
    :param wafer_circle_in: wafer 圆心坐标和半径, shape=(3,)
    :param center_offset_in: 矩形中心相对于 wafer 中心的偏移量
    :param die_size_in: die 尺寸
    :param die_spacing_in: die 间距
    :return:
    """
    # 1. 计算矩形中心坐标
    cx, cy, r = wafer_circle_in
    w, h = die_size_in
    w_dist = w + die_spacing_in[0]
    h_dist = h + die_spacing_in[1]
    rect_cx = cx + center_offset_in[0]
    rect_cy = cy + center_offset_in[1]
    # 2. 遍历行列, 生成矩形
    num_w = int(r // w_dist + 5)
    num_h = int(r // h_dist + 5)
    rects = []
    for i in range(-num_w, num_w+1, 1):
        for j in range(-num_h, num_h+1, 1):
            tmp_x = rect_cx + i * w_dist
            tmp_y = rect_cy + j * h_dist
            if tmp_x < cx - r or tmp_x > cx + r or tmp_y < cy - r or tmp_y > cy + r:
                continue
            rect_pts = rect_as_pts((tmp_x, tmp_y, w, h))
            if is_rect_in_circle(rect_pts, wafer_circle_in):
                rects.append(rect_pts)
    return rects

def line_length(line_in):
    """
    计算直线的长度
    :param line_in: 直线的两个端点坐标, shape=(2,2)
    :return: 直线的长度
    """
    x1, y1 = line_in[0]
    x2, y2 = line_in[1]
    return np.sqrt((x2 - x1) ** 2 + (y2 - y1) ** 2)

def segments_split(segments, max_length):
    """
    将线段按照不超过 max_length 的长度拆分成多段线
    :param segments: 线段的两个端点坐标, shape=(n,4), [[sx, sy, sz, ex, ey, ez], ...]
    :param max_length: 线段的最大长度
    :return: 拆分后的线段, [[p0, p1, ..., pm], ...]
    """
    seg_len = np.linalg.norm(segments[:, 3:6] - segments[:, 0:3], axis=1)
    out_segs = []
    for i in range(segments.shape[0]):
        if seg_len[i] > max_length:
            num_seg = np.ceil(seg_len[i] / max_length)
            seg_points = np.linspace(segments[i, 0:3], segments[i, 3:6], num=int(num_seg)+1, axis=0)
            out_segs.append(seg_points.tolist())
        else:
            out_segs.append([segments[i, 0:3].tolist(), segments[i, 3:6].tolist()])
    return out_segs


def split_polylines(polylines, max_length):
    """
    将多条折线按照不超过 max_length 的长度拆分成多段线
    :param polylines: 折线, [[p0, p1, ..., pn], ...], 2D 或 3D
    :param max_length: 线段的最大长度
    """
    out_polylines = []
    for polyline in polylines:
        tmp_poly = []
        polyline = np.array(polyline)
        for i in range(len(polyline)-1):
            seg_len = np.linalg.norm(polyline[i+1] - polyline[i])
            if seg_len > max_length:
                num_seg = np.ceil(seg_len / max_length)
                seg_points = np.linspace(polyline[i], polyline[i+1], num=int(num_seg)+1, axis=0)
                tmp_poly.extend(seg_points.tolist())
            else:
                tmp_poly.append(polyline[i].tolist())
        if len(tmp_poly) < 2:
            continue
        out_polylines.append(tmp_poly)
    return out_polylines
