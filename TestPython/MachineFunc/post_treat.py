import re
import numpy as np
from CommonLib.Transform3D import get_transformed_segments
from CommonLib.multiProcessFunc import func_by_pool
from MachineFunc.LKH_TSP import solve_tsp_lkh
from MachineFunc.routing_base import plot_result


# 振镜偏置计算
def getGalvoOffset(galvo_z_pos):
    """ 计算振镜偏置 """
    galvo_offset_dict = {}
    for (gal_i, tmp_galvo_name) in enumerate(galvo_z_pos['theory']['galvo_list']):
        galvo_offset = [0, 0, 0, 0]
        theo_pos = gal_i * galvo_z_pos['theory']['gap']
        meas_pos = galvo_z_pos['measure'][tmp_galvo_name] - galvo_z_pos['measure']['galvo_0']
        galvo_offset[0] = theo_pos - meas_pos
        galvo_offset_dict[tmp_galvo_name] = galvo_offset
    return galvo_offset_dict

# 解析角度和位置
def parse_gcode_z_c(gcode):
    matches = re.findall(r'[CZ]([-.\d]+)', gcode)
    c_val = float(matches[0])
    z_val = float(matches[1])
    return z_val, c_val

def parse_gcode_xyz(gcode):
    matches = re.findall(r'[XYZ]([-.\d]+)', gcode)
    if len(matches) == 0:
        return 0,0,0
    x_val = float(matches[0])
    y_val = float(matches[1])
    z_val = float(matches[2])
    return x_val, y_val, z_val

def parse_z_c_list(machine_cycle):
    z_c_list = []
    for item in machine_cycle:
        z_val, c_val = parse_gcode_z_c(item['galvo_0'][1])
        z_c_list.append((z_val, c_val))
    z_c_list = np.array(z_c_list)
    return z_c_list

def parse_xyz_list(machine_json, workpiece_mode=True):
    xyz_list = []
    if workpiece_mode:
        machine_cycle = machine_json["machine_cycle_w"]
        for item in machine_cycle:
            x_val, y_val, z_val, _, _, _ = item['galvo_0'][1]
            xyz_list.append((x_val, y_val, z_val))
    else:
        machine_cycle = machine_json["machine_cycle"]
        for item in machine_cycle:
            x_val, y_val, z_val = parse_gcode_xyz(item['galvo_0'][1])
            xyz_list.append((x_val, y_val, z_val))
    xyz_list = np.array(xyz_list)
    return xyz_list


def c_in_range(c_val, c_range):
    """
    弧度制, 需要考虑 2*pi 的周期
    :param c_val:
    :param c_range: [-pi, pi], 或者 [0, 2*pi]
    :return:
    """
    return c_range[0] <= c_val <= c_range[1] or c_range[0] <= (c_val + 2 * np.pi) <= c_range[1]



def merge_galvo_one_layer(layer_idx, start_z_random, z_size, z_c_list, layer_patch_list,
                          end_z, start_z, cycle_len, check_batch_gap, check_laser_idx, check_correction,
                          z_axis_range, cache_path, c_range, zc_speed,
                          galvo_z_pos, move_speed, v_g2b):
    # 1. 生成随机起点补偿: 能完全随机, 必须是整数个 z_size
    tmp_start_z = np.random.uniform(0, start_z_random)
    tmp_start_z = -np.floor(tmp_start_z / z_size) * z_size
    tmp_start_z += start_z
    # 2. 筛选出当前层的分块的索引 [[l_idx, p_idx], ...]和对应的位置 [[z_val, c_val], ...]
    tmp_layer_idx = np.where(layer_patch_list[:, 0] == layer_idx)[0]
    tmp_layer_patch_list = layer_patch_list[tmp_layer_idx, :]
    tmp_z_c_list = z_c_list[tmp_layer_idx, :]
    # 3. 基于起点和终点, 计算多个振镜需要组合的次数
    cycle_count = np.ceil((end_z - tmp_start_z) / cycle_len).astype(int) + 1
    print(f"cycle_count: {cycle_count}")
    # 4. 组合次数循环, 将每个 batch 中一个振镜的加工拓展为多个
    tmp_seg_z_start = tmp_start_z
    new_p_idx = 0
    new_machine_cycle = []
    new_zc_list = []
    for cycle_idx in range(cycle_count):
        # 5. 对 Z 偏置 tmp_seg_z_start, 作为每个振镜的加工位置; 遍历所有位置(每个分块)
        tmp_seg_z_c_list = tmp_z_c_list.copy()
        tmp_seg_z_c_list[:, 0] += tmp_seg_z_start
        # 7. 遍历所有分块, 填充 batch
        for (idx, tmp_z_c) in enumerate(tmp_seg_z_c_list):
            # 8. 遍历所有振镜, 填充 batch
            tmp_l_p_idx = tmp_layer_patch_list[idx, :]
            tmp_batch = {'common_move': f"G00C{tmp_z_c[1]:.5f}Z{(tmp_z_c[0]):.5f}F{move_speed:.0f}"}
            for (gal_i, tmp_galvo_name) in enumerate(galvo_z_pos['theory']['galvo_list']):
                # z_axis_range 筛选, 避免轴超程
                tmp_center_z = tmp_z_c[0]
                if (tmp_center_z < z_axis_range[0]) or (tmp_center_z > z_axis_range[1]):
                    # print(f"tmp_center_z: {tmp_center_z}, z_axis_range: {z_axis_range}")
                    continue
                # 加载 tmp_patch, 转全局, 加上 this_z 并筛选起点和终点都在 (start_z, end_z) 的线条
                tmp_l_idx = tmp_l_p_idx.tolist()[0]
                tmp_p_idx = tmp_l_p_idx.tolist()[1]
                tmp_center_z = tmp_z_c[0] + gal_i * galvo_z_pos['theory']['gap']
                tmp_patch = np.load(cache_path + f"{tmp_l_idx}_{tmp_p_idx}.npy")
                tmp_patch_b = get_transformed_segments(v_g2b, tmp_patch)
                tmp_patch_b[:, 2] += tmp_center_z
                tmp_patch_b[:, 5] += tmp_center_z
                # 获取每个起点的角度, 耦合 tmp_z_c[1], 为该线段角度, 筛选
                tmp_angles = np.atan2(tmp_patch_b[:, 1], tmp_patch_b[:, 0])
                tmp_angles += np.deg2rad(tmp_z_c[1])
                tmp_angles = np.arctan2(np.sin(tmp_angles), np.cos(tmp_angles))
                # 添加 c 角度筛选
                sel_idx = np.where(
                    (start_z <= tmp_patch_b[:, 2]) & (tmp_patch_b[:, 2] <= end_z) &
                    (start_z <= tmp_patch_b[:, 5]) & (tmp_patch_b[:, 5] <= end_z) &
                    (((c_range[0] <= tmp_angles) & (tmp_angles <= c_range[1])) |
                    ((c_range[0] <= (tmp_angles + 2 * np.pi)) & ((tmp_angles + 2 * np.pi) <= c_range[1])))
                )
                # 如果线条为空, 则跳过
                if len(sel_idx[0]) == 0:
                    continue
                # 如果线条减少, 则按照 [10000, new_p_idx] 保存
                if len(sel_idx[0]) < len(tmp_patch):
                    tmp_l_idx = (layer_idx + 1) * 10000
                    tmp_p_idx = new_p_idx
                    np.save(cache_path + f"{tmp_l_idx}_{tmp_p_idx}.npy", tmp_patch[sel_idx])
                    new_p_idx += 1
                # 填充 batch
                tmp_batch[tmp_galvo_name] = [-1, "", [tmp_l_idx, tmp_p_idx]]
            if len(tmp_batch) > 1:
                new_machine_cycle.append(tmp_batch)
                new_zc_list.append(tmp_z_c)
        tmp_seg_z_start += cycle_len
    # 6. 依据 new_zc_list 对 new_machine_cycle 进行排序
    zc_speed = 1.0 / np.array(zc_speed).reshape(-1, 2)
    zc_speed = zc_speed / np.min(zc_speed)
    sort_zc_list = np.array(new_zc_list) * zc_speed
    tour, _ = solve_tsp_lkh(sort_zc_list, speed_level=1, default_method="MAX_2D",
                            lkh_exe="MachineFunc/LKH-3.exe", start_idx=0, filename=f"{layer_idx}")
    # plot_result(new_zc_list, tour, 0)
    new_machine_cycle = [new_machine_cycle[idx] for idx in tour]
    # 9. 插入检验
    out_machine_cycle = []
    batch_count = 0
    for (batch_idx, tmp_batch) in enumerate(new_machine_cycle):
        out_machine_cycle.append(tmp_batch)
        batch_count += 1
        if batch_count % check_batch_gap == 0:
            for galvo_name in galvo_z_pos['theory']['galvo_list']:
                out_machine_cycle.append({"check": [galvo_name, check_laser_idx, check_correction]})
    return out_machine_cycle


def merge_galvo_layers_single_process(all_layer_idx, start_z_random, z_size,
                                      z_c_list, layer_patch_list, end_z,
                                      start_z, cycle_len,
                                      check_batch_gap, check_laser_idx, check_correction, zc_speed,
                                      z_axis_range, cache_path, c_range,
                                      galvo_z_pos, move_speed, v_g2b):
    new_machine_cycle = []
    for layer_idx in all_layer_idx:
        new_machine_cycle += merge_galvo_one_layer(layer_idx, start_z_random, z_size, z_c_list, layer_patch_list,
                                                    end_z, start_z, cycle_len,
                                                    check_batch_gap, check_laser_idx, check_correction,
                                                    z_axis_range, cache_path, c_range, zc_speed,
                                                    galvo_z_pos, move_speed, v_g2b)
    return new_machine_cycle


# 多振镜拼接
def merge_galvo_layers(machine_cycle,cache_path, v_b2g, galvo_z_pos, z_size,
                       z_axis_range, z_range, c_range, layer_idx_list,
                       start_z_random, move_speed,
                       check_batch_gap, check_laser_idx, check_correction, zc_speed):
    """
    由 1 个振镜拼接生成多个振镜轨迹
    :param machine_cycle:
    :param cache_path: 缓存路径
    :param v_b2g: 旋转矩阵
    :param galvo_z_pos: 多振镜的描述信息
    :param z_size: z 轴缩放因子
    :param z_range: z 轴范围
    :param c_range: c 轴角度范围
    :param start_z_random: 开始 z 位置随机范围, 可以在 [-start_z_random, 0] 之间随机
    :param move_speed: 移动速度
    :param check_batch_gap: 每隔 check_batch_gap 个 batch, 进行一次校验
    :param check_laser_idx: 校验激光参数索引, -1 表示红光, 其他为激光参数索引
    :param check_correction: 是否校验校正
    :param zc_speed: [z_speed, c_speed], 倒数作为排序的距离权重
    :return:
    """
    # c_range 调整到 -pi 到 pi
    if (c_range is None) or len(c_range) == 0:
        c_range = np.array([-np.pi, np.pi])
    else:
        c_range = np.deg2rad(np.array(c_range))
    c_range = np.arctan2(np.sin(c_range), np.cos(c_range))
    if c_range[0] > c_range[1]:
        c_range[1] += 2 * np.pi
    end_z = z_range[1]
    start_z = z_range[0]
    # 0. 参数准备
    layer_patch_list = np.array([item['galvo_0'][2] for item in machine_cycle])
    z_c_list = parse_z_c_list(machine_cycle)
    cycle_len = galvo_z_pos['theory']['gap'] * len(galvo_z_pos['theory']['galvo_list'])
    if (not layer_idx_list is None) and len(layer_idx_list) > 0:
        all_layer_idx = layer_idx_list
    else:
        all_layer_idx = np.unique(layer_patch_list[:, 0])
    all_layer_idx.sort()
    v_g2b = np.linalg.inv(v_b2g)
    # 引入多进程
    return func_by_pool(merge_galvo_layers_single_process, (all_layer_idx, start_z_random, z_size,
                                    z_c_list, layer_patch_list, end_z, start_z, cycle_len,
                                    check_batch_gap, check_laser_idx, check_correction, zc_speed,
                                    z_axis_range, cache_path, c_range, galvo_z_pos, move_speed, v_g2b),
                        [0], num_processes=8)

