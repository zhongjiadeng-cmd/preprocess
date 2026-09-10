import numpy as np
from CommonLib.Transform3D import src_to_tag_axis, inverse_matrix, get_transformed_pts
import shapely as shp
from shapely.ops import linemerge, unary_union


class Plane:
    def __init__(self, **kwargs):
        for key, val in kwargs.items():
            setattr(self, key, val)
        # p0, dir 点法式
        if hasattr(self, 'p0') and hasattr(self, 'dir'):
            self.dir = self.dir / np.linalg.norm(self.dir)  # 单位向量
            self.p1 = self.p0 + self.dir
            self.a, self.b, self.c = self.dir
            self.d = -self.a * self.p0[0] - self.b * self.p0[1] - self.c * self.p0[2]
        # a,b,c,d
        elif hasattr(self, 'a') and hasattr(self, 'b') and hasattr(self, 'c') and hasattr(self, 'd'):
            abc = np.array([self.a, self.b, self.c])
            norm_abc = np.linalg.norm(abc)
            abc = abc / norm_abc
            self.a, self.b, self.c = abc
            self.d = self.d / norm_abc  # 单位向量
            max_idx = np.argmax(np.abs(abc))
            self.p0 = np.array([0, 0, 0])
            self.p0[max_idx] = -self.d / abc[max_idx]  # 主方向非零, 其余为 0
            self.dir = np.array([self.a, self.b, self.c])
            self.p1 = self.p0 + self.dir
        # p0, p1 两点式
        elif hasattr(self, 'p0') and hasattr(self, 'p1'):
            self.dir = self.p1 - self.p0
            self.dir = self.dir / np.linalg.norm(self.dir)  # 单位向量
            self.p1 = self.p0 + self.dir
            self.a, self.b, self.c = self.dir
            self.d = -self.a * self.p0[0] - self.b * self.p0[1] - self.c * self.p0[2]


class Line3D:
    def __init__(self, **kwargs):
        for key, val in kwargs.items():
            setattr(self, key, val)
        # p0, dir, 点法式
        if hasattr(self, 'p0') and hasattr(self, 'dir'):
            self.dir = self.dir / np.linalg.norm(self.dir)
            self.p1 = self.p0 + self.dir # type: ignore
        # p0, p1, 两点式
        elif hasattr(self, 'p0') and hasattr(self, 'p1'):
            self.dir = self.p1 - self.p0 # type: ignore
            self.dir = self.dir / np.linalg.norm(self.dir)
            self.p1 = self.p0 + self.dir # type: ignore


# noinspection DuplicatedCode
def project_pts_on_line3d(pts: np.ndarray, line):
    """求解点集到直线的投影坐标"""
    # line 为点斜式 line = [p0, dir]
    x0, y0, z0 = line[0]
    dx, dy, dz = line[1]
    pts = pts.reshape(-1, 3)
    x1 = pts[:, 0]
    y1 = pts[:, 1]
    z1 = pts[:, 2]
    norm_xyz = dx ** 2 + dy ** 2 + dz ** 2
    x = (dx ** 2 * x1 - dx * dy * y0 + dx * dy * y1 - dx * dz * z0 + dx * dz * z1 + dy ** 2 * x0 + dz ** 2 * x0) / norm_xyz
    y = (dx ** 2 * y0 - dx * dy * x0 + dx * dy * x1 + dy ** 2 * y1 - dy * dz * z0 + dy * dz * z1 + dz ** 2 * y0) / norm_xyz
    z = (dx ** 2 * z0 - dx * dz * x0 + dx * dz * x1 + dy ** 2 * z0 - dy * dz * y0 + dy * dz * y1 + dz ** 2 * z1) / norm_xyz
    # 变形
    x = x.reshape([-1, 1])
    y = y.reshape([-1, 1])
    z = z.reshape([-1, 1])
    out_pts = np.hstack((x, y, z))
    return out_pts


def project_pts_on_plane_along_dir(pts: np.ndarray, plane, direction=None):
    """ 沿特定方向投影点集到平面，默认为法向 """
    x1 = pts[:, 0]
    y1 = pts[:, 1]
    z1 = pts[:, 2]
    a, b, c, d = plane
    a, b, c, d = float(a), float(b), float(c), float(d)
    if direction is None:
        i, j, k = a, b, c  # 默认为法向投影
    else:
        i, j, k = direction
        i, j, k = float(i), float(j), float(k)
    # 解方程：投影点为 直线=【点, dir】 和 平面的交点
    x = (-b * i * y1 + b * j * x1 - c * i * z1 + c * k * x1 - d * i) / (a * i + b * j + c * k)
    y = (a * i * y1 - a * j * x1 - c * j * z1 + c * k * y1 - d * j) / (a * i + b * j + c * k)
    z = (a * i * z1 - a * k * x1 + b * j * z1 - b * k * y1 - d * k) / (a * i + b * j + c * k)
    # 变形
    x = x.reshape([-1, 1])
    y = y.reshape([-1, 1])
    z = z.reshape([-1, 1])
    out_pts = np.hstack((x, y, z))
    return out_pts

def project_pts_on_cylinder_norm(pts: np.ndarray, cylinder):
    """
    求解点集到圆柱体的投影坐标
    :param pts: 点集
    :param cylinder: 圆柱体参数 [center, axis, radius]
    :return: 投影点集
    """
    center, axis, radius = cylinder
    # 1. 投影到圆柱体轴线上
    proj_pts = project_pts_on_line3d(pts, [center, axis])
    # 2. 从投影点到原始点的方向, 距离为 r 的点即为投影点
    directions =pts - proj_pts
    directions = directions / np.linalg.norm(directions, axis=1).reshape(-1, 1)
    proj_pts = proj_pts + directions * radius
    return proj_pts
def project_points_to_cylinder(points, projection_dir,
                               cylinder_center, cylinder_axis,
                               cylinder_radius):
    """
    将大量点沿特定方向投影到无限长圆柱面的最近点

    参数:
        points: 形状为 (n, 3) 的点集
        projection_dir: 投影方向 (3,)，已归一化
        cylinder_center: 圆柱轴线上一点 (3,)
        cylinder_axis: 圆柱轴线方向 (3,)，已归一化
        cylinder_radius: 圆柱半径

    返回:
        projected_points: 投影点坐标，形状 (n, 3)
        t_values: 沿投影方向移动的距离，形状 (n,)
        valid_mask: 有效投影的布尔掩码 (n,)
    """
    # 确保输入是numpy数组
    points = np.asarray(points)
    projection_dir = np.asarray(projection_dir)
    cylinder_center = np.asarray(cylinder_center)
    cylinder_axis = np.asarray(cylinder_axis)
    # 计算点到圆柱轴线的向量
    v = points - cylinder_center  # (n, 3)
    # 计算沿轴线方向的投影
    v_parallel = np.outer(np.dot(v, cylinder_axis), cylinder_axis)  # (n, 3)
    # 计算垂直于轴线的分量
    v_perp = v - v_parallel  # (n, 3)
    # 计算投影方向垂直于轴线的分量
    proj_dir_parallel = np.dot(projection_dir, cylinder_axis) * cylinder_axis
    proj_dir_perp = projection_dir - proj_dir_parallel
    # 二次方程系数
    a = np.dot(proj_dir_perp, proj_dir_perp)  # 标量
    b = 2 * np.dot(v_perp, proj_dir_perp)  # (n,)
    c = np.sum(v_perp ** 2, axis=1) - cylinder_radius ** 2  # (n,)
    # 计算判别式
    discriminant = b ** 2 - 4 * a * c  # (n,)
    # 有效点：判别式 >= 0
    valid_mask = discriminant >= 0
    # 初始化结果
    n_points = len(points)
    t_values = np.full(n_points, np.nan)
    projected_points = np.full_like(points, np.nan)
    if np.any(valid_mask):
        # 只处理有效点
        disc_sqrt = np.sqrt(discriminant[valid_mask])
        b_valid = b[valid_mask]
        # 计算两个可能的t值
        t1 = (-b_valid - disc_sqrt) / (2 * a)
        t2 = (-b_valid + disc_sqrt) / (2 * a)
        # 选择绝对值最小的t（最近的点）
        # 可以根据需要修改选择策略
        t_min = np.where(np.abs(t1) <= np.abs(t2), t1, t2)
        # 存储结果
        t_values[valid_mask] = t_min
        # 计算投影点
        points_valid = points[valid_mask]
        projected_points[valid_mask] = points_valid + np.outer(t_min, projection_dir)

    return projected_points, t_values, valid_mask



def distance_pts_to_line3d(pts: np.ndarray, line):
    """求解点到直线的距离"""
    proj_pts = project_pts_on_line3d(pts, line)
    distant = proj_pts - pts
    distant = distant * distant
    distant = np.sqrt(np.sum(distant, axis=1))
    return distant


def distance_pts_to_plane(pts: np.ndarray, plane):
    """
    点到平面的距离，与平面方向相同为正，相反为负
    :param pts: 点集
    :param plane: 平面 a,b,c,d
    :return: d = (a*x+b*y+c*z+d) / (a*a + b*b + c*c)**0.5
    """
    a, b, c, d = plane
    x, y, z = pts[:, 0], pts[:, 1], pts[:, 2]
    distance = (a * x + b * y + c * z + d) / np.sqrt(a * a + b * b + c * c)
    return distance


def intersection_line_of_plane(plane1, plane2, x0=None, y0=None, z0=None):
    """
    求解两个平面的交线
    :param plane1: a1, b1, c1, d1
    :param plane2: a2, b2, c2, d2
    :param x0: p0 的 x
    :param y0: p0 的 y
    :param z0: p0 的 z
    :return: 交线 [p0, norm0]
    """
    # norm0 为平面法向的向量积
    a1, b1, c1, d1 = plane1
    a2, b2, c2, d2 = plane2
    norm0 = np.cross(np.array([a1, b1, c1]), np.array([a2, b2, c2]))
    norm0 = norm0 / np.linalg.norm(norm0)
    # 令 p0 在 norm0 的主方向为 0，两平面方程求 Y、Z
    abs_norm0 = np.abs(norm0)
    idx = np.where(abs_norm0 == abs_norm0.max())[0]
    x, y, z = 0, 0, 0
    if x0 is not None:
        idx = 0
        x = x0
    elif y0 is not None:
        idx = 1
        y = y0
    elif z0 is not None:
        idx = 2
        z = z0
    if idx == 0:
        y = (c1 * d2 - c2 * d1) / (b1 * c2 - b2 * c1)
        z = (-b1 * d2 + b2 * d1) / (b1 * c2 - b2 * c1)
    elif idx == 1:
        x = (c1 * d2 - c2 * d1) / (a1 * c2 - a2 * c1)
        z = (-a1 * d2 + a2 * d1) / (a1 * c2 - a2 * c1)
    elif idx == 2:
        x = (b1 * d2 - b2 * d1) / (a1 * b2 - a2 * b1)
        y = (-a1 * d2 + a2 * d1) / (a1 * b2 - a2 * b1)
    else:
        raise ValueError
    return [np.array([x, y, z]), norm0]


def points_on_line(array, line, axis=0):
    """ 求解直线上的点集 """
    p0, norm0 = line  # 点法式
    array = array.reshape(-1, 1)
    base = (array - p0[axis]) / norm0[axis]
    pts = np.hstack((p0[0] + base * norm0[0], p0[1] + base * norm0[1], p0[2] + base * norm0[2]))
    return pts


def plane_from_two_planes_reflect_mirror(plane1, plane2):
    """ 两个平面的反射镜面 """
    line = intersection_line_of_plane(plane1, plane2)  # 交线[p0, dir]
    a1, b1, c1, d1 = plane1
    a2, b2, c2, d2 = plane2
    norm0 = angle_bisector_direction(np.array([a1, b1, c1]), np.array([a2, b2, c2]))  # 角平分线为法向
    plane = Plane(p0=line[0], dir=norm0)
    return plane.a, plane.b, plane.c, plane.d


def plane_from_two_planes_reflect_normal(plane1, plane2):
    """ 两个平面的角平分面 """
    line = intersection_line_of_plane(plane1, plane2)  # 交线[p0, dir]
    a1, b1, c1, d1 = plane1
    a2, b2, c2, d2 = plane2
    line1 = angle_bisector_direction(np.array([a1, b1, c1]), np.array([a2, b2, c2]))  # 角平分线为平面上的线
    norm0 = np.cross(line, line1)  # 角平分线与交线的法向
    plane = Plane(p0=line[0], dir=norm0)
    return plane.a, plane.b, plane.c, plane.d


def angle_bisector_direction(dir1, dir2):
    """ 两个向量的角平分线的方向 """
    dir1 = dir1 / np.linalg.norm(dir1)
    dir2 = dir2 / np.linalg.norm(dir2)
    out_dir = dir1 + dir2
    out_dir = out_dir / np.linalg.norm(out_dir)
    return out_dir


def angle_of_two_vectors(v1, v2, rad_mode=False):
    """ v1 到 v2 的旋转角, [0, pi] """
    v1 = np.array(v1)
    v2 = np.array(v2)
    cos_theta = np.dot(v1, v2) / (np.linalg.norm(v1) * np.linalg.norm(v2))
    sin_theta = np.linalg.norm(np.cross(v1, v2)) / (np.linalg.norm(v1) * np.linalg.norm(v2))  # 必然为正
    theta = np.arctan2(sin_theta, cos_theta)
    if rad_mode:
        return theta
    theta = theta / np.pi * 180
    return theta


def get_convex_box(pts, axis_in):
    """
    求解点集 pts 在方向为 axis_x, axis_z 的包络盒
    :param pts: 点集
    :param axis_in: [vx, vy, vz]
    :return: (l, w, h), (cx, cy, cz)
    """
    src_pos = np.array([0,0,0])
    src_x = np.array([1,0,0])
    src_y = np.array([0, 1, 0])
    src_z = np.array([0,0,1])
    matrix = src_to_tag_axis(src_pos, axis_in, [src_x, src_y, src_z], src_pos)
    inv_matrix = inverse_matrix(matrix)
    new_pts = get_transformed_pts(inv_matrix, pts)
    box_size = np.max(new_pts, axis=0) - np.min(new_pts, axis=0)
    center = (np.max(new_pts, axis=0) + np.min(new_pts, axis=0)) / 2
    center = get_transformed_pts(matrix, center)
    return box_size, center


def shapely_to_cnts(inter_lines):
    inter_lines_out = []
    for line in inter_lines: # type: ignore
        poly = np.array(line.coords).astype(np.int32)
        if poly.shape[0] < 2:
            continue
        inter_lines_out.append(poly)
    cnts = inter_lines_out
    return cnts


def polys_to_segments(cnts, is_3d=False):
    """
    多边形转线段
    :param polys: 多边形
    :param is_3d: 是否为 3D 线段
    :return: 线段
    """
    segments = []
    idx_list = []
    start_idx = 0
    for poly in cnts:
        poly = np.array(poly)
        for i in range(len(poly) - 1):
            if is_3d:
                tmp_seg = [poly[i, 0], poly[i, 1], poly[i, 2], poly[i+1, 0], poly[i+1, 1], poly[i+1, 2]]
            else:
                tmp_seg = [poly[i, 0], poly[i, 1], poly[i+1, 0], poly[i+1, 1]]
            segments.append(tmp_seg)
        idx_list.append(start_idx)
        start_idx += len(poly) - 1
    segments = np.array(segments)
    return segments, idx_list

def segments_to_polys(segments, idx_list=None, is_3d=False):
    """
    线段转多边形
    :param segments: 线段
    :param idx_list: 是否有顺序要求
    :param is_3d: 是否为 3D 线段
    :return: 多边形
    """
    if idx_list is not None:
        cnts = []
        for i in range(len(idx_list) - 1):
            tmp_poly = segments[idx_list[i]:idx_list[i+1], :]
            if is_3d:
                tmp_poly = np.vstack((tmp_poly[:, :3], tmp_poly[-1, 3:]))
            else:
                tmp_poly = np.vstack((tmp_poly[:, :2], tmp_poly[-1, 2:]))
            cnts.append(tmp_poly)
        tmp_poly = segments[idx_list[-1]:, :]
        if is_3d:
            tmp_poly = np.vstack((tmp_poly[:, :3], tmp_poly[-1, 3:]))
        else:
            tmp_poly = np.vstack((tmp_poly[:, :2], tmp_poly[-1, 2:]))
        cnts.append(tmp_poly)
    else:
        lines = [shp.LineString([[segments[i,0], segments[i,1]], 
                                 [segments[i,2],segments[i,3]]]) for i in range(len(segments))]
        lines = unary_union(lines)
        lines = linemerge(list(lines.geoms), directed=True) # type: ignore
        cnts = shapely_to_cnts(lines)
    return cnts