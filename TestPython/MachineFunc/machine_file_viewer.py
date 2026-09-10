import os
import json
import numpy as np
import pyvista as pv

from CommonLib.Transform3D import get_transformed_segments
from CommonLib.pvViewer import draw_pv_segments, draw_pv_axes
from MachineFunc.cluster_texture import get_b2g
from MachineFunc.gen_machine_file import layer_offset
from MachineFunc.post_treat import parse_z_c_list, parse_xyz_list, parse_gcode_z_c


def viewPlane(machine_file_path, workpiece_mode=True):
    """ 平板可视化 """
    root_path = os.path.dirname(machine_file_path)
    npy_path = f'{root_path}/patches'
    machine_json = json.load(open(machine_file_path))
    pos_list = parse_xyz_list(machine_json, workpiece_mode)
    cycle_key = 'machine_cycle_w' if workpiece_mode else 'machine_cycle'
    layer_patch_list = np.array([item['galvo_0'][2] for item in machine_json[cycle_key]])
    pl = pv.Plotter()
    for (tmp_xyz, tmp_l_p) in zip(pos_list, layer_patch_list):
        # 加载线段, 转全局坐标, 绘制
        tmp_patch = np.load(f'{npy_path}/{tmp_l_p[0]}_{tmp_l_p[1]}.npy')
        if not workpiece_mode:
            # 平板可视化: 平移到全局坐标
            tmp_patch[:, :3] += tmp_xyz
            tmp_patch[:, 3:] += tmp_xyz
        random_color = np.random.uniform(0, 1, 3).tolist()
        draw_pv_segments(pl, tmp_patch, random_color)
    draw_pv_axes(pl, 5.0)
    pl.show()


def viewCylinder(machine_file_path, galvo_z_pos, v_b2g):
    """ 圆柱可视化 """
    root_path = os.path.dirname(machine_file_path)
    npy_path = f'{root_path}/patches'
    machine_json = json.load(open(machine_file_path))
    pl = pv.Plotter()
    v_g2b = np.linalg.inv(v_b2g)
    for (i, tmp_batch) in enumerate(machine_json['machine_cycle']):
        if "check" in tmp_batch.keys():
            continue
        # z 和 c 来源于 tmp_batch 中第一个 gcode
        first_galvo = list(tmp_batch.keys())[1]
        tmp_z, tmp_c = parse_gcode_z_c(tmp_batch["common_move"])
        # 耦合多振镜的 gap 偏移
        for galvo_name in tmp_batch.keys():
            if galvo_name == "common_move":
                continue
            # 计算多振镜的 gap 偏移
            galvo_idx = galvo_z_pos["theory"]["galvo_list"].index(galvo_name)
            z_offset = galvo_z_pos["theory"]["gap"] * galvo_idx
            # 加载线段, 转全局坐标, 绘制
            tmp_l_p = tmp_batch[galvo_name][2]
            tmp_patch = np.load(f'{npy_path}/{tmp_l_p[0]}_{tmp_l_p[1]}.npy')
            # 振镜坐标系转工件坐标系
            tmp_patch = get_transformed_segments(v_g2b, tmp_patch)
            # 耦合 z、c 转全局坐标
            tmp_patch = layer_offset(tmp_patch, np.deg2rad(tmp_c), tmp_z+z_offset)
            tmp_color = np.random.uniform(0, 1, 3).tolist()
            draw_pv_segments(pl, tmp_patch, tmp_color)
    draw_pv_axes(pl, 50.0)
    pl.show()
