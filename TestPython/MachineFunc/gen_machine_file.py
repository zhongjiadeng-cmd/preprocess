import json
import os
import pyvista as pv
import datetime
import numpy as np
from CommonLib.FillByLines import fill_polygen
from CommonLib.Geometry3D import project_pts_on_cylinder_norm
from CommonLib.Transform3D import move_rotate_matrix, get_transformed_segments
from CommonLib.pvViewer import draw_pv_segments, draw_pv_axes
from MachineFunc.cluster_texture import gen_grid_pts_plane, clustering_grid_pts, save_clustering_result, \
    sort_patches_in_layer, gen_random_pts_plane, gen_grid_pts_cylinder_random_edge, show_cloud, get_b2g, modify_r_size


# region 【轧辊纹理-分层】
def file_prepare():
    now = datetime.datetime.now()
    time_str = now.strftime("%Y%m%d_%H%M%S")
    root_path = f"machine_file_{time_str}"
    os.makedirs(root_path, exist_ok=True)
    file_path = f"{root_path}/machine.json"
    npy_path = root_path + "/patches"
    os.makedirs(npy_path, exist_ok=True)
    img_path = root_path + "/img"
    os.makedirs(img_path, exist_ok=True)
    return npy_path, file_path, img_path

def gen_one_slot(max_line_length, line_gap, top_width, bottom_width, layer_count, layer_depth,
                 fill_line_angle_list, edge_method, end_offset,
                 is_v_line, center_pt, zig_zag, double_dir, center_to_edge, reverse_layer):
    """
    生成一个凸台的沟槽纹理, 按层排布
    :param max_line_length: 最大线长(正中间的线段长度), 其余部位的线长度需要基于此长度, 减去到此线的距离的 2 倍
    :param line_gap: 线间距
    :param top_width: 顶部宽度
    :param bottom_width: 底部宽度
    :param layer_count: 层数
    :param layer_depth: 层深度
    :param fill_line_angle_list: 填充线角度列表
    :param edge_method: 边缘填充方法: 边的处理方式, "none" or "pre" or "post" or "both", 绕边一圈的策略
    :param end_offset: 线头增长偏移量
    :param is_v_line: 是否为垂直方向
    :param center_pt: 中心点
    :param zig_zag: 是否为 zigzag 方向
    :param double_dir: 是否为双方向
    :param center_to_edge: 是否为中心到边缘方向
    :param reverse_layer: 是否反转当前层的线槽纹理线段列表
    :return: 沟槽纹理线段列表
    """
    slot_lines_layers = []
    for layer_idx in range(0, layer_count):
        # 1. 计算当前层的宽度, 并根据宽度计算线段数量和间距
        if layer_count == 1:
            tmp_layer_width = top_width
        else:
            tmp_layer_width = top_width - (top_width - bottom_width) * (layer_idx / (layer_count - 1))
        if len(fill_line_angle_list) > 0:
            # 等间距, 按照 fill_line_angle_list[layer_idx] 的角度进行多边形填充
            tmp_layer_angle = fill_line_angle_list[layer_idx]
            max_x = tmp_layer_width / 2.0 + end_offset
            max_y = max_line_length / 2.0 + end_offset
            median_y = max_y - max_x
            poly = [[-max_x, -median_y], [0, -max_y], [max_x, -median_y],
                    [max_x, median_y], [0, max_y], [-max_x, median_y], [-max_x, -median_y]]
            tmp_layer_lines_3d = fill_polygen(poly, line_gap, tmp_layer_angle, edge_method, ref_z=-layer_depth * layer_idx)
        else:  # 没有填充线角度列表, 则按普通线槽纹理处理, 变间距
            tmp_layer_count = np.floor(tmp_layer_width / line_gap / 2.0) * 2 + 1
            tmp_layer_count = int(tmp_layer_count)  # 奇数条线
            if tmp_layer_count == 1:
                tmp_line_space = 0
            else:
                tmp_line_space = tmp_layer_width / (tmp_layer_count-1)
            # 2. 循环单层生成的线条
            x_start = -(tmp_layer_count // 2) * tmp_line_space
            y_start = - max_line_length / 2.0 - end_offset
            tmp_layer_lines = []
            for j in range(0, tmp_layer_count):
                tmp_x = x_start + j * tmp_line_space
                tmp_y = y_start + abs(tmp_x)
                tmp_layer_lines.append((tmp_x, tmp_y, tmp_x, -tmp_y))
            tmp_layer_lines = np.array(tmp_layer_lines)
            tmp_layer_lines_3d = - np.ones((len(tmp_layer_lines), 6)) * layer_depth * layer_idx
            tmp_layer_lines_3d[:, :2] = tmp_layer_lines[:, :2]
            tmp_layer_lines_3d[:, 3:5] = tmp_layer_lines[:, 2:]
        tmp_layer_lines_3d = sort_slot_lines_layer(tmp_layer_lines_3d, zig_zag, double_dir, center_to_edge, reverse_layer)
        if not is_v_line:
            # 3. 竖直线转水平线, 分别交换 01 列 和 34 列
            new_tmp_layer_lines_3d = tmp_layer_lines_3d.copy()
            tmp_layer_lines_3d[:, 0] = new_tmp_layer_lines_3d[:, 1]
            tmp_layer_lines_3d[:, 1] = new_tmp_layer_lines_3d[:, 0]
            tmp_layer_lines_3d[:, 3] = new_tmp_layer_lines_3d[:, 4]
            tmp_layer_lines_3d[:, 4] = new_tmp_layer_lines_3d[:, 3]
        # 4. 按照 center_pt 偏移 slot_lines
        tmp_layer_lines_3d[:, :3] += center_pt
        tmp_layer_lines_3d[:, 3:] += center_pt
        slot_lines_layers.append(tmp_layer_lines_3d)
    return slot_lines_layers

def sort_slot_lines_layer(slot_lines_cur_layer, zig_zag, double_dir, center_to_edge, reverse_layer):
    """
    重新排序当前层的线槽纹理线段列表
    :param slot_lines_cur_layer: 当前层的线槽纹理线段列表
    :param zig_zag: 是否为 zigzag 方向
    :param double_dir: 是否为双方向
    :param center_to_edge: 是否为中心到边缘方向
    :param reverse_layer: 是否反转当前层的线槽纹理线段列表
    :return:
    """
    # 1. 线分布方式: 双方向, 为中心到边缘方向, 常规从左到右
    if double_dir:
        # 双方向
        # 分别取 slot_lines 的奇数索引和偶数索引的线
        slot_lines_1 = slot_lines_cur_layer[::2, :].copy()
        slot_lines_2 = slot_lines_cur_layer[1::2, :].copy()
        # slot_lines_2 反转排序
        slot_lines_2 = slot_lines_2[::-1]
        sorted_lines = np.vstack((slot_lines_1, slot_lines_2))
    elif center_to_edge:
        # 中心到边缘方向
        # 取 slot_lines 的正中间索引
        center_idx = len(slot_lines_cur_layer) // 2
        sorted_lines = []
        sorted_lines.append(slot_lines_cur_layer[center_idx, :])
        for i in range(0, center_idx):
            sorted_lines.append(slot_lines_cur_layer[center_idx - i - 1, :])
            sorted_lines.append(slot_lines_cur_layer[center_idx + i + 1, :])
        sorted_lines = np.array(sorted_lines)
    else:
        # 常规从左到右
        sorted_lines = np.array(slot_lines_cur_layer)
    # 是否跳线反向排序
    if zig_zag:
        # zigzag 方向: 每隔 1 条线, 交换前两列和后两列
        new_sorted_lines = sorted_lines.copy()
        start_pts = new_sorted_lines[:, :3]
        end_pts = new_sorted_lines[:, 3:]
        sorted_lines[::2, :3] = end_pts[::2, :]
        sorted_lines[::2, 3:] = start_pts[::2, :]
    else:
        # 常规从左到右
        pass
    # 是否反转当前层的线槽纹理线段列表
    if reverse_layer:
        sorted_lines = sorted_lines[::-1]
    return sorted_lines

def gen_array_patch_by_centers(center_pt_list, slot_lines_cur_layer, sort_method):
    """
    根据中心点列表生成凸台纹理数组
    :param center_pt_list: 中心点列表
    :param slot_lines_cur_layer: 当前层的线槽纹理线段列表
    :param sort_method: center_pt_list 的排序方法 , "by_x", "by_y", "by_z", "random"
    :return:
    """
    patch_lines = []
    # 1. 对 center_pt_list 进行排序
    center_pt_list = np.array(center_pt_list)
    if sort_method == "by_x":
        center_pt_list = center_pt_list[center_pt_list[:, 0].argsort()]
    elif sort_method == "by_y":
        center_pt_list = center_pt_list[center_pt_list[:, 1].argsort()]
    elif sort_method == "by_z":
        center_pt_list = center_pt_list[center_pt_list[:, 2].argsort()]
    elif sort_method == "random":
        center_pt_list = center_pt_list[np.random.permutation(center_pt_list.shape[0]), :]
    # 2. 生成凸台纹理
    for center_pt in center_pt_list:
        slot_lines = slot_lines_cur_layer.copy()
        slot_lines[:, :3] += center_pt
        slot_lines[:, 3:] += center_pt
        patch_lines.append(slot_lines)
    patch_lines = np.vstack(patch_lines)
    return patch_lines

def gen_all_layer_patch_plane(laser_params, base_xyz, h_space, v_space, h_count, v_count, line_gap, top_width, bottom_width,
                              layer_count, layer_depth, layer_first, fine_gap, laser_idx_list,
                              fill_line_angle_list, edge_method,
                              end_offset, zig_zag, double_dir, center_to_edge, reverse_layer, sort_in_patch,
                              searching_radius_list, blurred_radius_list,
                              random_pts_time, random_pts_density, random_pts_method, workpiece_mode):
    """
    生成所有层的凸台纹理
    :param laser_params: 激光参数
    :param base_xyz: 平面的中心点坐标数组
    :param h_space: 水平方向上的间距
    :param v_space: 垂直方向上的间距
    :param h_count: 水平方向上的网格点数量
    :param v_count: 垂直方向上的网格点数量
    :param line_gap: 线槽纹理线段间距
    :param top_width: 线槽纹理顶部宽度
    :param bottom_width: 线槽纹理底部宽度
    :param layer_count: 层数量
    :param layer_depth: 层深度
    :param layer_first: 层优先, True 则按层分块, False 则不分层
    :param fine_gap: 精细加工最后一层的加工间距
    :param laser_idx_list: 所有层的激光参数
    :param fill_line_angle_list: 填充线角度列表
    :param edge_method: 边缘填充方法: 边的处理方式, "none" or "pre" or "post" or "both"
    :param end_offset: 结束偏移量
    :param zig_zag: 是否为 zigzag 方向
    :param double_dir: 是否为双方向
    :param center_to_edge: 是否为中心到边缘方向
    :param reverse_layer: 是否反转当前层的线槽纹理线段列表
    :param sort_in_patch: 分块内的排序方法, "by_x", "by_y", "by_z", "by_c", "random"
    :param searching_radius_list: 聚类半径列表
    :param blurred_radius_list: 模糊半径列表
    :param random_pts_time: 随机点时间
    :param random_pts_density: 随机点密度
    :param random_pts_method: 随机打点的方法, "pretreat" or "post_treat" or "none" or "both"
    :param workpiece_mode: 是否是工件模式, 否则需要设置 base_xyz; 是则按照 base_xyz=[0,0,0] 生成工件坐标系下的加工文件
    :return:
    """
    # 1. 生成 1 个 slot, 分层
    slot_lines_layers_h = gen_one_slot(h_space, line_gap, top_width, bottom_width, layer_count, layer_depth, fill_line_angle_list, edge_method, end_offset,
                                     False, np.array([0,-v_space/2,0]), zig_zag, double_dir, center_to_edge, reverse_layer)
    slot_lines_layers_v = gen_one_slot(v_space, line_gap, top_width, bottom_width, layer_count, layer_depth, fill_line_angle_list, edge_method, end_offset,
                                     True, np.array([-h_space/2,0,0]), zig_zag, double_dir, center_to_edge, reverse_layer)
    # 添加精细加工层
    if fine_gap > 0:
        slot_layer_h_fine = gen_one_slot(h_space, fine_gap, top_width, bottom_width, 1, layer_depth, fill_line_angle_list, edge_method, end_offset,
                                         False, np.array([0,-v_space/2,-layer_depth*layer_count]), zig_zag, double_dir, center_to_edge, reverse_layer)
        slot_layer_v_fine = gen_one_slot(v_space, fine_gap, top_width, bottom_width, 1, layer_depth, fill_line_angle_list, edge_method, end_offset,
                                         True, np.array([-h_space/2,0,-layer_depth*layer_count]), zig_zag, double_dir, center_to_edge, reverse_layer)
        slot_lines_layers_h = slot_lines_layers_h + slot_layer_h_fine
        slot_lines_layers_v = slot_lines_layers_v + slot_layer_v_fine
        laser_idx_list.append(2)
        layer_count += 1
    no_move = False
    if workpiece_mode:  # 工件模式, 基于工件坐标系生成加工文件
        base_xyz = [0, 0, 0]
    elif (base_xyz is None or len(base_xyz) == 0):  # 不分块模式, 没有轴移动
        no_move = True
        base_xyz = [0,0,0]
    grid_pts = gen_grid_pts_plane(base_xyz[0], base_xyz[1], h_space, v_space, h_count, v_count, uniform_z=base_xyz[2])
    while len(searching_radius_list) < layer_count:
        searching_radius_list = searching_radius_list * 2
    while len(blurred_radius_list) < layer_count:
        blurred_radius_list = blurred_radius_list * 2
    if len(laser_idx_list) == 0:
        laser_idx_list = [0]
    if len(laser_idx_list) < layer_count:
        layer_count = layer_count * layer_count
    # 2. 遍历层，生成随机分块
    slot_lines_layers = []
    if layer_first:  # 层优先: 交叉合并 slot_lines_layers_h 和 slot_lines_layers_v, 保持层数
        for (tmp_h_layer, tmp_v_layer) in zip(slot_lines_layers_h, slot_lines_layers_v):
            slot_lines_layers.append(np.concatenate((tmp_h_layer, tmp_v_layer), axis=0))
    else:  # 块优先, 将输入处理为 1 层
        slot_lines_layers_h = np.vstack(slot_lines_layers_h)
        slot_lines_layers_v = np.vstack(slot_lines_layers_v)
        slot_lines_layers = [np.concatenate((slot_lines_layers_h, slot_lines_layers_v), axis=0)]
    all_layers = []
    root_path, file_path, img_path = file_prepare()
    for (i, slot_lines_cur_layer) in enumerate(slot_lines_layers):
        # 2.0 z 下降 layer_depth
        grid_pts[:, 2] -= layer_depth
        # 2.1 当前层随机分块
        grid_pts = grid_pts[np.random.permutation(grid_pts.shape[0]), :]
        _, _, center_list_b, out_labels = clustering_grid_pts(grid_pts, searching_radius_list[i], blurred_radius_list[i])
        save_clustering_result(grid_pts, out_labels, i, img_path, False)
        # 2.2 遍历分块, 生成当前层的加工轨迹
        tmp_layer = []
        for i in range(len(center_list_b)):
            tag_pos = center_list_b[i]
            # 获取 out_labels 中 值为 i+1 的索引
            idx = np.where(out_labels == i+1)
            center_pt_list = grid_pts[idx[0]]
            if not workpiece_mode:
                # center_pt_list 中的所有点减去 tag_pos, 将相对坐标转绝对
                center_pt_list -= tag_pos
            tmp_patch_lines = gen_array_patch_by_centers(center_pt_list, slot_lines_cur_layer, sort_in_patch)
            tmp_layer.append([tmp_patch_lines, tag_pos])
        # 对 tmp_layer 进行排序
        tmp_layer = sort_patches_in_layer(tmp_layer, method="plane")
        all_layers.append(tmp_layer)
    # 3. 生成加工轨迹
    machine_json = gen_matching_file_by_layer_patch(root_path, all_layers, laser_params, laser_idx_list, no_move, workpiece_mode)
    # 4. 添加抛光层
    if random_pts_method == "both" or random_pts_method == "post_treat":
        machine_json = append_random_pts_layer_patches(root_path, layer_count, base_xyz, h_space, v_space, h_count, v_count, random_pts_density, searching_radius_list[0], blurred_radius_list[0],
                                                       machine_json, random_pts_time, "post_treat")
        layer_count += 1
        print("Add post_treat layer patches success")
    if random_pts_method == "both" or random_pts_method == "pretreat":
        machine_json = append_random_pts_layer_patches(root_path, layer_count, base_xyz, h_space, v_space, h_count, v_count, random_pts_density, searching_radius_list[0], blurred_radius_list[0],
                                                       machine_json, random_pts_time, "pretreat")
        layer_count += 1
        print("Add pretreat layer patches success")
    # 5. 保存加工文件
    with open(file_path, "w") as f:
        json.dump(machine_json, f, indent=4)
        print(f"Save success to {file_path}")
    return file_path


def append_random_pts_layer_patches(root_path, layer_count, base_xyz, h_space, v_space, h_count, v_count, random_pts_density,
                                    searching_radius, blurred_radius, machine_json, random_pts_time, random_pts_method,
                                    x_weight=1.0, y_weight=1.0, z_weight=1.0):
    """
    生成平面上的随机点, 并对聚类结果进行保存
    :param root_path: 保存路径
    :param layer_count: 已有的层数
    :param base_xyz: 平面的中心点坐标数组
    :param h_space: 水平方向上的间距
    :param v_space: 垂直方向上的间距
    :param h_count: 水平方向上的网格点数量
    :param v_count: 垂直方向上的网格点数量
    :param random_pts_density: 随机点密度, 个/平方毫米
    :param searching_radius: 搜索半径
    :param blurred_radius: 模糊半径
    :param machine_json: 加工文件
    :param random_pts_time: 随机点生成时间, 单位 ms
    :param random_pts_method: 随机打点的方法, "pretreat" or "post_treat"
    :param x_weight: x 方向的权重
    :param y_weight: y 方向的权重
    :param z_weight: z 方向的权重
    :return:
    """
    base_xyz_empty = False
    if len(base_xyz) == 0 or base_xyz is None:
        base_xyz = [0, 0, 0]
        base_xyz_empty = True
    # 1. 生成随机点并分块
    start_x = -h_space * h_count / 2 + base_xyz[0]
    end_x = h_space * h_count / 2 + base_xyz[0]
    start_y = -v_space * v_count / 2 + base_xyz[1]
    end_y = v_space * v_count / 2 + base_xyz[1]
    random_pts = gen_random_pts_plane(start_x, start_y, end_x, end_y, random_pts_density, base_xyz[2])
    _, _, center_list_b, out_labels = clustering_grid_pts(random_pts, searching_radius, blurred_radius, x_weight, y_weight, z_weight)
    random_pts_layer = []
    for i in range(len(center_list_b)):
        # 获取当前块的中心点
        tag_pos = center_list_b[i]
        # 获取当前块的 点集
        idx = np.where(out_labels == i + 1)
        r_pt_list = random_pts[idx[0]]
        r_pt_list -= tag_pos
        random_pts_layer.append([r_pt_list, tag_pos])
    # 2. 生成加工文件
    laser_idx = len(machine_json["laser_params"]) - 1
    for patch_idx in range(len(random_pts_layer)):
        tmp_patch_pts, tag_pos = random_pts_layer[patch_idx]
        # 1. 保存 patch_pts 到文件
        t_arr = np.ones((tmp_patch_pts.shape[0], 1)) * random_pts_time
        tmp_patch_pts = np.hstack((tmp_patch_pts, t_arr))
        tmp_patch_pts = tmp_patch_pts.astype(np.float32)
        np.save(f"{root_path}/{layer_count}_{patch_idx}.npy", tmp_patch_pts)
        # 2. 生成 machine_cycle
        if random_pts_method == "pretreat":
            # 插入在加工轨迹的开头
            if base_xyz_empty:
                machine_json["machine_cycle"].insert(0, {"galvo_0": [laser_idx, "", [layer_count, patch_idx]]})
            else:
                machine_json["machine_cycle"].insert(0, {"galvo_0": [laser_idx, f"G00X{tag_pos[0]:.3f}Y{tag_pos[1]:.3f}Z{tag_pos[2]:.3f}F40", [layer_count, patch_idx]]})
        elif random_pts_method == "post_treat":
            if base_xyz_empty:
                machine_json["machine_cycle"].append(0, {"galvo_0": [laser_idx, "", [layer_count, patch_idx]]})
            else:
                machine_json["machine_cycle"].append({"galvo_0": [laser_idx, f"G00X{tag_pos[0]:.3f}Y{tag_pos[1]:.3f}Z{tag_pos[2]:.3f}F40", [layer_count, patch_idx]]})
        else:
            raise ValueError(f"random_pts_method {random_pts_method} is not supported")
        laser_idx = -1
    return machine_json

def gen_matching_file_by_layer_patch(root_path, all_layers, laser_params, laser_idx_list,
                                     no_move=False, workpiece_mode=False):
    """
    生成加工文件, 按层拼接
    :param root_path: 加工文件根路径, 不包含文件名
    :param all_layers: [[[patch_lines, pos], ...], ...]
    :param laser_params:
    :return:
    """
    machine_cycle = []
    laser_idx = 0
    for layer_idx in range(len(all_layers)):
        tmp_layer = all_layers[layer_idx]
        tmp_laser_idx = laser_idx_list[layer_idx]
        for patch_idx in range(len(tmp_layer)):
            # 1. 保存 patch_lines 到文件
            tmp_patch_lines, tag_pos = tmp_layer[patch_idx]
            tmp_patch_lines = tmp_patch_lines.astype(np.float32)
            np.save(f"{root_path}/{layer_idx}_{patch_idx}.npy", tmp_patch_lines)
            # 2. 生成 machine_cycle
            if no_move:
                move_gcode = ""
            elif workpiece_mode:
                move_gcode = [tag_pos[0], tag_pos[1], tag_pos[2], 0.0, 0.0, 1.0]
            else:
                move_gcode = f"G00X{tag_pos[0]:.3f}Y{tag_pos[1]:.3f}Z{tag_pos[2]:.3f}F40"
            if workpiece_mode:
                machine_cycle.append({"galvo_0": [tmp_laser_idx, move_gcode, [layer_idx, patch_idx], [0,0,0,-1e12,0,0]]})
            else:
                machine_cycle.append({"galvo_0": [tmp_laser_idx, move_gcode, [layer_idx, patch_idx]]})
            tmp_laser_idx = -1
    # 3. 保存
    machine_json = {
        "laser_params": laser_params,
        "galvo_offset": {
            "galvo_0": [0,0,0,0]
        }
    }
    if workpiece_mode:
        machine_json["machine_cycle_w"] = machine_cycle
    else:
        machine_json["machine_cycle"] = machine_cycle
    return machine_json



# endregion 【轧辊纹理=-分层】

# region 【轧辊纹理-圆柱分层】

# 将线段集合投影到圆柱上
def project_lines_to_cylinder(lines:np.ndarray, radius, p0, dir0):
    """
    将线段投影到圆柱上
    :param lines:
    :param radius:
    :param p0:
    :param dir0:
    :return:
    """
    start_pts = lines[:, :3]
    end_pts = lines[:, 3:]
    start_pts = project_pts_on_cylinder_norm(start_pts, [p0, dir0, radius])
    end_pts = project_pts_on_cylinder_norm(end_pts, [p0, dir0, radius])
    lines = np.hstack((start_pts, end_pts))
    return lines

def project_slot_on_cylinder(slot_lines_layers, radius, p0, dir0):
    """
    将槽线段投影到圆柱上
    :param slot_lines_layers:
    :param radius:
    :param p0:
    :param dir0:
    :return:
    """
    for slot_lines in slot_lines_layers:
        slot_lines = project_lines_to_cylinder(slot_lines, radius, p0, dir0)
    return slot_lines_layers

def layer_offset(lines:np.ndarray, c_offset, z_offset):
    """
    对线段集合进行偏移
    :param lines: 线段集合
    :param c_offset: c 方向的偏移量 (旋转)
    :param z_offset: z 方向的偏移量
    :return: 旋转偏移后的线段集合
    """
    r_mat = move_rotate_matrix(np.array([0,0,1]), c_offset, np.array([0,0,z_offset]), True)
    return get_transformed_segments(r_mat, lines)

def genOneUnit(z_size, r_size, radius, cylinder_p0, cylinder_dir, layer_first,
               line_gap, top_width, bottom_width, layer_count, layer_depth,
               fill_line_angle_list, edge_method, end_offset,
               zig_zag, double_dir, center_to_edge, reverse_layer, v_b2g
               ):
    """
    生成加工沟槽的一个单元
    """
    r_size, r_theta, _ = modify_r_size(r_size, radius)
    slot_lines_layers_h = gen_one_slot(z_size, line_gap, top_width, bottom_width, layer_count, layer_depth,
                                       fill_line_angle_list, edge_method, end_offset,
                                       False, np.array([0,0,radius]), zig_zag, double_dir, center_to_edge, reverse_layer)
    slot_lines_layers_v = gen_one_slot(r_size, line_gap, top_width, bottom_width, layer_count, layer_depth,
                                       fill_line_angle_list, edge_method, end_offset,
                                       True, np.array([0,0,radius]), zig_zag, double_dir, center_to_edge, reverse_layer)
    # 2.坐标转换矩阵, 引入初始的 g2b 矩阵
    v_g2b = np.linalg.inv(v_b2g)
    slot_lines_layers_h = [get_transformed_segments(v_g2b, layer) for layer in slot_lines_layers_h]
    slot_lines_layers_v = [get_transformed_segments(v_g2b, layer) for layer in slot_lines_layers_v]
    # 3. 将 slot_lines_layers_h 和 slot_lines_layers_v 按层投影到圆柱上, 圆柱半径为 x 的值
    slot_lines_layers_h = [project_lines_to_cylinder(layer, layer[0,0], cylinder_p0, cylinder_dir) for layer in slot_lines_layers_h]
    slot_lines_layers_v = [project_lines_to_cylinder(layer, layer[0,0], cylinder_p0, cylinder_dir) for layer in slot_lines_layers_v]
    # 4. 按照中心点的 z 和 c 偏移 slot_lines_layers_v
    slot_lines_layers_v = [layer_offset(layer, -r_theta/2, -z_size/2) for layer in slot_lines_layers_v]
    # 5. 合并 slot_lines_layers_h 和 slot_lines_layers_v, 作为零位的线条
    slot_lines_layers = []
    if layer_first:  # 层优先
        for (tmp_h_layer, tmp_v_layer) in zip(slot_lines_layers_h, slot_lines_layers_v):
            slot_lines_layers.append(np.concatenate((tmp_h_layer, tmp_v_layer), axis=0))
    else:  # 块优先, 只有 1 层
        slot_lines_layers = slot_lines_layers_h + slot_lines_layers_v
        slot_lines_layers = np.concatenate(slot_lines_layers, axis=0)
        slot_lines_layers = [slot_lines_layers]
    return slot_lines_layers


def genOnePatch(root_path, patch_idx, unit_cur_layer, layer_idx,
                labels, c_grid, grid_pts, v_b2g):
    """
    生成一个分块
    """
    # 获取 out_labels 中 值为 i+1 的索引
    idx = np.where(labels == patch_idx + 1)
    # 获取这些点的 c 和 z 坐标
    c_arr = c_grid[idx[0]]
    z_arr = grid_pts[idx[0], 2]
    # 遍历这些点, 得到分块的线段: 工件坐标系下
    patch_lines = []
    for (c_offset, z_offset) in zip(c_arr, z_arr):
        slot_lines = layer_offset(unit_cur_layer, c_offset, z_offset)
        patch_lines.append(slot_lines)
    patch_lines = np.concatenate(patch_lines, axis=0)
    # patch_lines 整体偏移 -center_c 和 -center_z
    center_pt = grid_pts[idx[0]].mean(axis=0)
    center_c = np.atan2(center_pt[1], center_pt[0])
    center_z = center_pt[2]
    patch_lines = layer_offset(patch_lines, -center_c, -center_z)
    # 转换为振镜坐标系
    patch_lines = get_transformed_segments(v_b2g, patch_lines)

    # pl = pv.Plotter()
    # draw_pv_segments(pl, patch_lines)
    # draw_pv_axes(pl, 100)
    # pl.show()

    if layer_idx==0 and patch_idx==0:
        laser_idx = 0
    else:
        laser_idx = -1
    center_c = center_c / np.pi * 180
    tmp_batch = {"galvo_0": [laser_idx, f"G00C{center_c:.5f}Z{center_z:.3f}F40", [layer_idx, patch_idx]]}
    patch_lines = patch_lines.astype(np.float32)
    np.save(f"{root_path}/{layer_idx}_{patch_idx}.npy", patch_lines)
    return center_c, center_z, tmp_batch

def gen_one_layer_grid(r_size, z_size, radius, z_patch_len, z_trans_len, layer_idx,
                       searching_radius, blurred_radius, xy_weight, img_path):
    """
    生成一个分层的网格点, 并聚类; 从 0 开始, 含 0
    """
    # 1. 生成单段曲面
    grid_pts, c_grid, z_patch_len = gen_grid_pts_cylinder_random_edge(z_size, r_size, radius, z_patch_len, z_trans_len,
                                                                      True, True)
    # 2. 单段曲面聚类
    random_idx = np.random.permutation(grid_pts.shape[0])
    grid_pts = grid_pts[random_idx, :]
    c_grid = c_grid[random_idx]
    _, _, _, out_labels = clustering_grid_pts(grid_pts, searching_radius, blurred_radius, xy_weight, xy_weight, 1)
    show_cloud(grid_pts, out_labels, layer_idx, img_path, show=False)
    return grid_pts, c_grid, out_labels

def genOneLayerPatches(r_size, z_size, radius, z_patch_len, z_trans_len, layer_idx,
                       searching_radius, blurred_radius, xy_weight, img_path,
                       root_path, slot_lines_cur_layer, v_b2g):
    last_time = datetime.datetime.now()
    machine_cycle = []
    # 6.1 生成随机网格点并分块
    grid_pts, c_grid, out_labels = gen_one_layer_grid(r_size, z_size, radius, z_patch_len, z_trans_len, layer_idx,
                                                      searching_radius, blurred_radius, xy_weight, img_path)
    print(f"层 {layer_idx} 分块数: {out_labels.max()}")
    # 6.2 遍历分块, 生成当前层的加工轨迹
    tmp_layer = []
    for patch_idx in range(out_labels.max()):
        center_c, center_z, tmp_batch = genOnePatch(root_path, patch_idx, slot_lines_cur_layer, layer_idx,
                                                    out_labels, c_grid, grid_pts, v_b2g)
        tmp_layer.append([center_c, center_z, tmp_batch])
    # 6.3 层内排序
    tmp_layer.sort(key=lambda x: x[0])
    for tmp_batch in tmp_layer:
        machine_cycle.append(tmp_batch[2])
    print(f"层 {layer_idx} 生成完成, 分块数: {len(tmp_layer)}, 耗时: ", datetime.datetime.now() - last_time)
    return machine_cycle

def genAllLayersPatches(layer_idx_slot_lines_layers,
                        r_size, z_size, radius, z_patch_len, z_trans_len,
                        searching_radius, blurred_radius, xy_weight, img_path,
                        root_path, v_b2g):
    machine_cycle = []
    for (layer_idx, slot_lines_cur_layer) in layer_idx_slot_lines_layers:
        machine_cycle += genOneLayerPatches(r_size, z_size, radius, z_patch_len, z_trans_len, layer_idx,
                                            searching_radius, blurred_radius, xy_weight, img_path,
                                            root_path, slot_lines_cur_layer, v_b2g)
    return machine_cycle
# endregion 【轧辊纹理-圆柱分层】

