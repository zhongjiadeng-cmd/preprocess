import numpy as np
from numpy.linalg import norm

# region【旋转矩阵求解】

def rotate_matrix_any_line(line, theta=0., rad_mod=True):
    """
    绕任意直线旋转的变换矩阵
    :param line: [p0, dir0]
    :param theta: rad/angel
    :param rad_mod: theta 的模式, 默认为弧度
    :return: 4x4 变换矩阵
    """
    p0, dir0 = line
    x, y, z = p0
    # 1. 平移到原点
    r1 = np.array([
        [1, 0, 0, -x],
        [0, 1, 0, -y],
        [0, 0, 1, -z],
        [0, 0, 0, 1]
    ])
    # 2. 旋转
    r2 = move_rotate_matrix(dir0, theta, rad_mod=rad_mod)
    # 3. 平移回原位置
    r3 = np.array([
        [1, 0, 0, x],
        [0, 1, 0, y],
        [0, 0, 1, z],
        [0, 0, 0, 1]
    ])
    out_matrix = r3 @ r2 @ r1
    return out_matrix

def rotate_matrix(rotate_axis=None, theta=0., rad_mod=True):
    """
    旋转矩阵
    :param rotate_axis: 旋转轴
    :param theta: 旋转角度/弧度
    :param rad_mod: theta 的输入模式是否为弧度
    :return: 旋转矩阵 3x3
    """
    if rotate_axis is None:
        rotate_axis = np.array([1, 0, 0])
    if not isinstance(rotate_axis, np.ndarray):
        rotate_axis = np.array(rotate_axis)
    rotate_axis = rotate_axis / np.linalg.norm(rotate_axis)  # 归一化
    # 转列向量
    rotate_axis = rotate_axis.reshape(-1, 1)
    rx = float(rotate_axis[0, 0])
    ry = float(rotate_axis[1, 0])
    rz = float(rotate_axis[2, 0])
    if not rad_mod:
        theta = np.radians(theta)  # 角度转弧度
    r_matrix = np.array([[rx * rx * (1 - np.cos(theta)) + np.cos(theta),
                          rx * ry * (1 - np.cos(theta)) - rz * np.sin(theta),
                          rx * rz * (1 - np.cos(theta)) + ry * np.sin(theta)],
                         [rx * ry * (1 - np.cos(theta)) + rz * np.sin(theta),
                          ry * ry * (1 - np.cos(theta)) + np.cos(theta),
                          ry * rz * (1 - np.cos(theta)) - rx * np.sin(theta)],
                         [rx * rz * (1 - np.cos(theta)) - ry * np.sin(theta),
                          ry * rz * (1 - np.cos(theta)) + rx * np.sin(theta),
                          rz * rz * (1 - np.cos(theta)) + np.cos(theta)]])
    return r_matrix

def move_rotate_matrix(rotate_axis=None, theta=0., point=None, rad_mod=True):
    """
    旋转位移矩阵，先平移后旋转
    :param rotate_axis: 旋转轴的向量
    :param theta: 旋转弧度
    :param point: 位移量在变换前的坐标系中的值；即新的原点，在原坐标系中的坐标
    :param rad_mod: theta 的输入模式是否为弧度
    :return: np.ndarray 旋转位移矩阵4x4
    """
    r_matrix = rotate_matrix(rotate_axis, theta, rad_mod)
    # 转列向量
    if point is None:
        point = np.array([0, 0, 0])
    if not isinstance(point, np.ndarray):
        point = np.array(point)
    point = point.reshape(-1, 1)
    # 旋转矩阵的逆矩阵
    r_matrix_i = np.linalg.inv(r_matrix)
    # 求解 point 在变换后的向量
    point = np.dot(r_matrix_i, point)
    # 拼接 4*4矩阵
    r_matrix = np.concatenate((r_matrix, point), 1)  # 横向拼接
    r_matrix = np.concatenate((r_matrix, np.array([0, 0, 0, 1]).reshape(1, -1)), 0)  # 纵向拼接
    return r_matrix

def rotate_matrix_2_axis(org_axis, target_axis):
    """
    将两个不平行的轴, 旋转到目标两个轴的旋转矩阵
    :param org_axis: [axis1, axis2]
    :param target_axis: [axis1, axis2]
    :return: np.ndarray 旋转矩阵 3x3
    """
    # 归一化
    org_axis = org_axis / norm(org_axis, axis=1).reshape(-1, 1)
    target_axis = target_axis / norm(target_axis, axis=1).reshape(-1, 1)
    # 第一步: 旋转至第一个轴重合
    axis0, theta0 = axis_theta_from_two_axis(org_axis[0], target_axis[0])
    r_0 = rotate_matrix(axis0, theta0)
    # 第二步: 旋转至两个轴的公垂线重合
    org_axis = get_transformed_pts(r_0, org_axis)
    org_common = np.cross(org_axis[0], org_axis[1])
    target_common = np.cross(target_axis[0], target_axis[1])
    axis1, theta1 = axis_theta_from_two_axis(org_common, target_common, rad_mod=True, ref_axis=target_axis[0])
    r_1 = rotate_matrix(axis1, theta1)
    return r_1 @ r_0

def axis_transform_matrix(axis_from, axis_to):
    """
    轴变换矩阵
    :param axis_from: 原坐标系的轴向量, [v1, v2, v3]
    :param axis_to: 目标坐标系的轴向量, [v1, v2, v3]
    :return: 3x3 变换矩阵
    """
    if not isinstance(axis_from, np.ndarray):
        axis_from = np.array(axis_from)
    if not isinstance(axis_to, np.ndarray):
        axis_to = np.array(axis_to)
    axis_from = axis_from.T
    axis_to = axis_to.T
    inv_axis_from = inverse_matrix(axis_from)
    r_matrix = np.dot(axis_to, inv_axis_from)
    return r_matrix

def src_to_tag_axis(tag_pos, tag_axis, src_axis, src_pos=None):
    """
    求解将 src 坐标系的坐标转换到 tag 坐标系的旋转位移矩阵
    :param tag_pos: tag 坐标系的原点
    :param tag_axis: tag 坐标系的轴, [v1, v2, v3]
    :param src_axis: src 坐标系的轴, [v1, v2, v3]
    :param src_pos: src 坐标系的原点
    :return: np.ndarray 旋转位移矩阵 4x4
    """
    if src_pos is None:
        src_pos = np.array([0, 0, 0])
    # 1. 旋转矩阵
    out_matrix = np.eye(4)
    out_matrix[:3, :3] = axis_transform_matrix(src_axis, tag_axis)
    # 2. 平移向量在旋转后的向量
    move_vec = tag_pos - src_pos
    # 3. 合并
    out_matrix[:3, 3] = move_vec.reshape(-1)
    return out_matrix

def axis_theta_from_two_axis(source_axis, target_axis, rad_mod=True, ref_axis=None):
    """
    根据两个轴向量，计算旋转轴和旋转角度
    :param source_axis:  旋转前的轴向量
    :param target_axis:  旋转后的轴向量
    :param rad_mod:
    :param ref_axis: 参考的公共轴
    :return:
    """
    # 求解公垂线及旋转角度
    out_axis = np.cross(source_axis, target_axis)
    # 1. 若 out_axis 长度为 0, 则说明 source_axis 和 target_axis 平行, 直接返回 0 角度 或 180 角度
    if norm(out_axis) < 1e-12:
        theta = 0.
        if np.dot(source_axis, target_axis) < 0:
            theta = np.pi
        out_axis = 1 / source_axis * np.array([1, 1, -2]).reshape(source_axis.shape)
        # nan 置为 1e6
        out_axis1 = np.zeros(out_axis.shape)
        out_axis1[np.isinf(out_axis)] = 1
        if np.sum(out_axis1) > 0.5:
            out_axis = out_axis1
        out_axis = out_axis / np.linalg.norm(out_axis)
        if ref_axis is not None:  # 有参考的旋转轴
            if np.abs(np.dot(ref_axis, source_axis)) < 1e-6:  # ref_axis 与 source_axis 垂直
                out_axis = ref_axis
    # 2. 否则, 计算旋转角度
    else:
        out_axis = out_axis / norm(out_axis)
        dot_axis = np.dot(source_axis, target_axis)
        theta = np.arccos(dot_axis / norm(source_axis) / norm(target_axis))
    if not rad_mod:
        theta = theta / np.pi * 180  # 转换为角度
    return out_axis, theta

def inverse_matrix(matrix):
    """ 求解逆变换矩阵 """
    if len(matrix) == 3:
        out_matrix = np.linalg.inv(matrix)
    elif len(matrix) == 4:
        r_matrix = np.linalg.inv(matrix[:3, :3])
        pts = matrix[:3, 3]
        out_matrix = np.concatenate((r_matrix, -np.dot(r_matrix, pts).reshape(-1, 1)), axis=1)
        out_matrix = np.concatenate((out_matrix, np.array([[0, 0, 0, 1]])), axis=0)
    else:
        raise ValueError("matrix must be 3x3 or 4x4")
    return out_matrix
# endregion

# region【BC 轴机床变换】

def get_bc_by_norm(norm_dir, last_b=0, last_c=0):
    """
    基于 norm 计算 B 摆和 C 摆的角度
    :param norm_dir: 平均法向, 即振镜的轴向
    :param last_b: 上一次的 B 摆角度, 默认为 0, 单位为弧度
    :param last_c: 上一次的 C 摆角度, 默认为 0, 单位为弧度
    :return:
    """
    norm_dir = norm_dir / norm(norm_dir)
    i, j, k = norm_dir
    b_theta1 = np.arccos(k)  # B 摆角度为正, C 摆从 X+ 开始
    b_theta2 = -np.arccos(k)  # B 摆角度为负, C 摆从 X- 开始
    c_theta1 = np.arctan2(j, i)  # C 摆角度
    c_theta2 = np.arctan2(-j, -i)  # C 摆角度
    delta_1 = abs(b_theta1 - last_b) + abs(c_theta1 - last_c)
    delta_2 = abs(b_theta2 - last_b) + abs(c_theta2 - last_c)
    if delta_1 < delta_2:
        return b_theta1, c_theta1
    else:
        return b_theta2, c_theta2

# endregion

# region【元素的刚性变换】
def get_transformed_pts(r_matrix, input_points: np.ndarray):
    """
    :param r_matrix: 旋转位移矩阵 4x4 或 3x3
    :param input_points: 点列, nx3
    """
    if len(input_points.shape) == 1:
        input_points = input_points.reshape(1, -1)
    if len(r_matrix) == 4:
        expand_column = np.ones((len(input_points), 1))
        pts_t = np.hstack((input_points, expand_column))
        pts_t = np.dot(r_matrix, pts_t.T)  # 点乘
        pts_t = pts_t[0:3, :].T
    elif len(r_matrix) == 3:
        pts_t = np.dot(r_matrix, input_points.T).T
    else:
        raise ValueError('r_matrix must be 3x3 or 4x4')
    return pts_t

def get_transformed_segments(r_matrix, input_segments: np.ndarray):
    """
    :param r_matrix: 旋转位移矩阵 4x4 或 3x3
    :param input_segments: 线段列, nx6
    """
    segments_t = np.zeros_like(input_segments)
    segments_t[:, 0:3] = get_transformed_pts(r_matrix, input_segments[:, 0:3])
    segments_t[:, 3:6] = get_transformed_pts(r_matrix, input_segments[:, 3:6])
    return segments_t

def get_transformed_vec(r_matrix, input_vec: np.ndarray):
    """
    :param r_matrix: 旋转位移矩阵 4x4 或 3x3
    :param input_vec: 向量列, nx3
    """
    vec_t = np.dot(r_matrix[:3, :3], input_vec.T).T
    return vec_t
# endregion