import datetime
from os import stat_result

import numpy as np
import matplotlib.pyplot as plt
import open3d as o3d
import pyvista as pv

from CommonLib.Transform3D import rotate_matrix_2_axis
from CommonLib.dataGenaration import generate_smooth_wave
from CommonLib.pvViewer import draw_pv_points


def gen_random_pts_plane(start_x, start_y, end_x, end_y, random_pts_density, ref_z):
    """
    生成平面上的随机点
    :param start_x: 平面的起始点 x 坐标
    :param start_y: 平面的起始点 y 坐标
    :param end_x: 平面的结束点 x 坐标
    :param end_y: 平面的结束点 y 坐标
    :param random_pts_density: 随机点密度, 个/平方毫米
    :param ref_z: 随机点的 z 坐标, 用于生成参考高度
    :return:
    """
    area = (end_x - start_x) * (end_y - start_y)
    pts_count = area * random_pts_density
    pts_count = int(pts_count)
    # 生成随机点
    rng = np.random.default_rng(seed=42)
    x_arr = rng.uniform(start_x, end_x, pts_count)
    y_arr = rng.uniform(start_y, end_y, pts_count)
    z_arr = ref_z * np.ones(pts_count)
    # 生成随机点的坐标数组
    random_pts = np.vstack((x_arr, y_arr, z_arr)).T
    return random_pts

def modify_r_size(r_size, radius):
    """
    调整 r_size, 以便于在圆柱上生成整数个沟槽
    :param r_size: 原始 r_size
    :param radius: 圆柱的半径
    :return:
    """
    # 1. 计算 r 方向参数
    theta = r_size / radius
    r_count = np.round(2 * np.pi / theta)
    r_count = int(r_count)
    r_size = 2 * np.pi * radius / r_count
    r_theta = 2 * np.pi / r_count
    return r_size, r_theta, r_count

def modify_z_end(z_size, start_z, end_z):
    z_count = int(np.round((end_z - start_z) / z_size))
    end_z = start_z + z_count * z_size
    return end_z, z_count

def modify_z_size(z_size, start_z, end_z):
    z_count = int(np.round((end_z - start_z) / z_size))
    z_size = (end_z - start_z) / z_count
    return z_size, z_count


def gen_grid_pts_cylinder(r_count, z_count, start_z, end_z, radius):
    """
    生成圆柱上的网格点, 不包含 end_z
    :param r_count: 网格点在 r 方向上的数量
    :param z_count: 网格点在 z 方向上的数量
    :param start_z: 圆柱的起始高度
    :param end_z: 圆柱的结束高度, 不包含 end_z
    :param radius: 圆柱的半径
    :return:
    """
    c_arr = np.linspace(0, 2 * np.pi, r_count, endpoint=False)
    z_arr = np.linspace(start_z, end_z, z_count, endpoint=False)
    c_grid, z_grid = np.meshgrid(c_arr, z_arr)
    c_grid = c_grid.reshape(-1)
    z_grid = z_grid.reshape(-1)
    x_grid = radius * np.cos(c_grid)
    y_grid = radius * np.sin(c_grid)
    grid_pts = np.vstack((x_grid, y_grid, z_grid)).T
    return grid_pts, c_grid

def gen_grid_pts_cylinder_random_split(r_count, z_count, start_z, end_z, radius):
    """
    生成圆柱上的网格点, 并随机拆分为左右两段
    :param r_count: 网格点在 r 方向上的数量
    :param z_count: 网格点在 z 方向上的数量
    :param start_z: 圆柱的起始高度
    :param end_z: 圆柱的结束高度
    :param radius: 圆柱的半径
    :return:
    """
    # 1. 生成圆柱上的网格点
    c_arr = np.linspace(0, 2 * np.pi, r_count, endpoint=False)
    z_arr = np.linspace(start_z, end_z, z_count, endpoint=False)
    c_grid, z_grid = np.meshgrid(c_arr, z_arr)
    # 2. 生成沿 c_arr 的 z_arr 拆分曲线
    _, z_split = generate_smooth_wave(len(c_arr), min_val=start_z, max_val=end_z, closed=True)
    mask = z_grid < z_split
    mask = mask.reshape(-1)
    c_grid = c_grid.reshape(-1)
    z_grid = z_grid.reshape(-1)
    x_grid = radius * np.cos(c_grid)
    y_grid = radius * np.sin(c_grid)
    grid_pts = np.vstack((x_grid, y_grid, z_grid)).T
    grid_pts_1 = grid_pts[mask]
    grid_pts_2 = grid_pts[~mask]
    c_grid_1 = c_grid[mask]
    c_grid_2 = c_grid[~mask]
    return grid_pts_1, grid_pts_2, c_grid_1, c_grid_2

def gen_grid_pts_cylinder_random_edge(z_size, r_size, radius, all_length, edge_length, with_end=True, with_start=True):
    """
    生成圆柱上的网格点, 并包含随机边界点
    :param z_size: 网格点在 z 方向上的间距
    :param r_size: 网格点在 r 方向上的间距
    :param radius: 圆柱的半径
    :param all_length: 圆柱的总长度
    :param edge_length: 边界点沿 Z 方向上的随机幅度/长度
    :param with_end: 是否结束边随机
    :param with_start: 是否起始边随机
    :return:
    """
    # 1. 生成圆柱上的网格点: 修正 r_size 和 z_size, 不允许变更 start_z 和 end_z
    start_z = 0
    end_z = all_length-edge_length
    r_size, _, r_count = modify_r_size(r_size, radius)
    end_z, z_count = modify_z_end(z_size, start_z, end_z)  # 此处不应该修正 z_size, 外部已经修正过了; 应当修正 end_z 以保证后续不需要任何修正
    grid_pts, c_grid= gen_grid_pts_cylinder(r_count, z_count, start_z, end_z, radius)
    # 2. 生成边缘上的网格点: 修正 r_size 和 end_z, 不允许再变更 z_size
    start_z = end_z
    end_z = all_length
    _, z_count = modify_z_size(z_size, start_z, end_z)  # 此处不需要任何修正
    grid_pts_1, grid_pts_2, c_grid_1, c_grid_2 = gen_grid_pts_cylinder_random_split(r_count, z_count, start_z, end_z, radius)
    if with_start:
        grid_pts_2[:, 2] = grid_pts_2[:, 2] - end_z
        grid_pts = np.vstack((grid_pts_2, grid_pts))
        c_grid = np.concatenate((c_grid_2, c_grid))
    if with_end:
        grid_pts = np.vstack((grid_pts, grid_pts_1))
        c_grid = np.concatenate((c_grid, c_grid_1))
    return grid_pts, c_grid, end_z

def gen_grid_pts_plane(cx, cy, x_size, y_size, x_count, y_count, uniform_z=0.0):
    """
    生成平面上的网格点
    :param cx: 平面的中心点 x 坐标
    :param cy: 平面的中心点 y 坐标
    :param x_size: 网格点在 x 方向上的间距
    :param y_size: 网格点在 y 方向上的间距
    :param x_count: 网格点在 x 方向上的数量
    :param y_count: 网格点在 y 方向上的数量
    :param uniform_z: 网格点在 z 方向上的统一高度
    :return: 网格点的坐标数组, (x, y, z) * (x_count * y_count)
    """
    x_size = x_size * (x_count -1) / 2
    y_size = y_size * (y_count -1) / 2
    x_grid, y_grid = np.meshgrid(np.linspace(-x_size, x_size, x_count), np.linspace(-y_size, y_size, y_count))
    x_grid = x_grid.reshape(-1)
    y_grid = y_grid.reshape(-1)
    x_grid += cx
    y_grid += cy
    z_grid = np.full_like(x_grid, uniform_z)
    grid_pts = np.vstack((x_grid, y_grid, z_grid)).T
    return grid_pts

def cluster_pts(pts:np.ndarray, radius, method, x_weight, y_weight, z_weight):
    """
    对点集进行聚类
    :param pts: 点集的坐标数组, (x, y, z) * (pts_count)
    :param radius: 聚类半径
    :param method: 聚类方法
    :param x_weight: x 方向的距离权重
    :param y_weight: y 方向的距离权重
    :param z_weight: z 方向的距离权重
    :return:
    """
    # 1. grid_pts 转 o3d.PointCloud 构建 kd_tree
    grid_pts = pts.copy()
    grid_pts[:, 0] *= x_weight
    grid_pts[:, 1] *= y_weight
    grid_pts[:, 2] *= z_weight
    grid_pts = grid_pts.astype(np.float32)
    # 1. 基于 kd_tree 进行聚类, 用带权重的点集聚类, 原始点集的坐标用于计算簇中心
    if method == "kmeans_rnn":
        nns = o3d.core.nns.NearestNeighborSearch(grid_pts)
        nns.fixed_radius_index(radius=radius)
        labels = np.zeros(grid_pts.shape[0], dtype=int)
        center_list = []
        label_idx = 0
        for i in range(grid_pts.shape[0]):
            if labels[i] != 0:
                continue
            neighbors_index, neighbors_distance, neighbors_splits = nns.fixed_radius_search(grid_pts[i].reshape(1, -1), radius=radius, sort=True)
            idx = neighbors_index.numpy()
            label_idx += 1
            # 计算簇中心, 用原始点集的坐标
            center_list.append(np.mean(pts[idx], axis=0))
            labels[idx] = label_idx
        return labels, center_list
    elif method == "Birch":
        # fixlater
        raise NotImplementedError("Birch 聚类方法暂未实现")
    else:
        raise ValueError(f"不支持的聚类方法: {method}")

def sort_patches_in_layer(layer_patches, method="plane"):
    """
    对层内的块进行排序
    :param layer_patches: 层内的块, 每个块是一个 (patch_lines, tag_pos) 元组
    :param method: 排序方法, 可选值为 "plane" 或 "cylinder"
    :return:
    """
    center_list = [tmp_layer[1] for tmp_layer in layer_patches]
    center_list = np.vstack(center_list)
    if method == "plane":
        # 1. 求解 center_list 的 xy 哪个方向的距离大, 并根据距离大的方向排序簇中心
        delta_x = np.max(np.abs(center_list[:, 0]))
        delta_y = np.max(np.abs(center_list[:, 1]))
        if delta_x > delta_y:
            # 按 x 方向排序
            idx = np.argsort(center_list[:, 0])
        else:
            # 按 y 方向排序
            idx = np.argsort(center_list[:, 1])
        out_layer_patches = [layer_patches[i] for i in idx]
        return out_layer_patches
    else:
        raise ValueError(f"不支持的排序方法: {method}")

def clustering_grid_pts(grid_pts:np.ndarray, searching_radius, blurred_radius=3,
                        x_weight=1.0, y_weight=1.0, z_weight=1.0):
    """
    对网格点进行聚类
    :param grid_pts: 网格点的坐标数组, (x, y, z) * (x_count * y_count)
    :param searching_radius: 搜索半径
    :param blurred_radius: 模糊半径
    :param x_weight: x 方向的距离权重
    :param y_weight: y 方向的距离权重
    :param z_weight: z 方向的距离权重
    :return:
    """
    # 2. 基于 kd_tree 进行聚类
    # 2.1 聚小簇
    labels_s, center_list_s = cluster_pts(grid_pts, blurred_radius, "kmeans_rnn", x_weight, y_weight, z_weight)
    center_list_s = np.vstack(center_list_s)
    # 2.2 对小簇中心聚类
    labels_b, center_list_b = cluster_pts(center_list_s, searching_radius-blurred_radius, "kmeans_rnn", x_weight, y_weight, z_weight)
    center_list_b = np.vstack(center_list_b)
    # 3. 基于 labels 和 labels_b 合并小簇
    label_idx_all = 0
    out_labels = np.ones(grid_pts.shape[0], dtype=int) * (-1)
    np_labels_s = np.asarray(labels_s)
    for i in range(1, labels_b.max()+1):
        # 获得 labels_b[i] 对应的 labels 索引, 即为小簇的标号 - 1
        idx = np.where(labels_b == i)
        # 找到 labels 中和 idx 值相等的索引
        lb_idx = np.isin(np_labels_s, idx[0]+1)
        label_idx_all += 1
        out_labels[lb_idx] = label_idx_all
    return labels_s, labels_b, center_list_b, out_labels

def save_clustering_result(grid_pts:np.ndarray, labels:np.ndarray, idx:int, img_path:str, show=False):
    """
    保存聚类结果
    :param grid_pts:
    :param labels:
    :param idx: 图片索引
    :param img_path: 图片保存路径
    :param show: 是否显示可视化结果
    :return:
    """
    fig = plt.figure()
    ax = fig.add_subplot(111)
    ax.scatter(grid_pts[:, 0], grid_pts[:, 1], c=labels, cmap='tab20', s=50, alpha=0.8)
    ax.set_xlabel('X')
    ax.set_ylabel('Y')
    plt.title(f'Complete Linkage Clustering: {labels.max()} clusters')
    plt.axis('equal')
    if show:
        plt.show()
    # 保存可视化结果
    plt.savefig(f'{img_path}/clustering_result_{idx}.png')
    plt.close()

def show_cloud(grid_pts:np.ndarray, labels:np.ndarray, idx:int, img_path:str, show=False):
    """
    显示点云
    :param grid_pts:
    :param labels:
    :param idx: 图片索引
    :param img_path: 图片保存路径
    :param show: 是否显示可视化结果
    :return:
    """
    pl = pv.Plotter(off_screen=True)
    for i in range(1, labels.max()+1):
        tmp_pts = grid_pts[labels == i]
        if tmp_pts.shape[0] == 0:
            continue
        tmp_color = np.random.uniform(0, 1, 3).tolist()
        draw_pv_points(pl, tmp_pts, tmp_color, 10.0)
    pl.show(screenshot=f'{img_path}/cloud_{idx}.png')
    if show:
        pl = pv.Plotter()
        for i in range(1, labels.max() + 1):
            tmp_pts = grid_pts[labels == i]
            if tmp_pts.shape[0] == 0:
                continue
            tmp_color = np.random.uniform(0, 1, 3).tolist()
            draw_pv_points(pl, tmp_pts, tmp_color, 10.0)
        pl.show()


def get_b2g(is_big=False):
    if is_big:
        base_vec = np.array([
            [0, 0, -1],
            [0, -1, 0]
        ])
    else:
        base_vec = np.array([
            [0, 0, 1],
            [0, 1, 0]
        ])
    galvo_vec = np.array([
        [1, 0, 0],
        [0, -1, 0]
    ])

    b2g = rotate_matrix_2_axis(base_vec, galvo_vec)

    # print(b2g)
    # print()
    # print((b2g @ galvo_vec.T).T)
    # print()
    # print((np.linalg.inv(b2g) @ base_vec.T).T)

    return b2g