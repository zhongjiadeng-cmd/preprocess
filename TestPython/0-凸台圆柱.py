import json
import numpy as np
from CommonLib.multiProcessFunc import func_by_pool
from MachineFunc.cluster_texture import get_b2g, modify_z_size
from MachineFunc.gen_machine_file import genOneUnit, file_prepare, genAllLayersPatches
import datetime

if __name__ == '__main__':
    laser_params =  [
        {  # 以下是加工层的激光参数, 0 和 1
            "power": 27,
            "frequency": 350,
            "pulseWidthIdx": 3,
            "scanSpeed": 2100,
            "jump_vel": 6000,
            "jump_delay": 50,
            "scan_ahead": True,
            "accScale": 50,
            "cornerScale": 100,
            "endScale": 100,
            "sky_writing": True,
            "timeLag": 100,
            "laserOnShift": 18,
            "delaseroff": 32,
            "delaseron": 0,
        },
        {  # 以下是加工层的激光参数, 2 和 3
            "power": 26,
            "frequency": 350,
            "pulseWidthIdx": 3,
            "scanSpeed": 2100,
            "jump_vel": 6000,
            "jump_delay": 50,
            "scan_ahead": True,
            "accScale": 50,
            "cornerScale": 100,
            "endScale": 100,
            "sky_writing": True,
            "timeLag": 100,
            "laserOnShift": 18,
            "delaseroff": 32,
            "delaseron": 0,
        },
        {  # 以下是随机打点的激光参数
            "power": 10,
            "frequency": 100,
            "pulseWidthIdx": 3,
            "scanSpeed": 2100,
            "jump_vel": 6000,
            "jump_delay": 50,
            "scan_ahead": True,
            "accScale": 50,
            "cornerScale": 100,
            "endScale": 100,
            "sky_writing": False,
            "timeLag": 100,
            "laserOnShift": 18,
            "delaseroff": 32,
            "delaseron": 0,
        }
    ]

    is_big_machine = False
    radius = 348.08 / 2.0
    top_width = 0.35
    bottom_width = 0.09
    layer_count = 40
    z_patch_len = 250.0
    line_gap = 0.030

    searching_radius = 21.0
    blurred_radius = 9.0
    arc_height = 0.3

    layer_depth = 0.13 / (layer_count-1)
    end_offset = 0.0
    layer_first = True
    z_size = 1.05
    r_size = 0.87
    cylinder_p0 = [0,0,0]
    cylinder_dir = [0,0,1]
    arc_theta = np.acos((radius-arc_height)/radius)
    arc_length = radius * arc_theta
    xy_weight = searching_radius / arc_length  # 一半弧长

    start_time = datetime.datetime.now()
    last_time = start_time
    print()
    print("Start to generate plane machine file")

    # 0. 需要基于分段大小, 微调 z_size 以便于保证分段内为整数个沟槽
    z_size, z_count = modify_z_size(z_size, 0, z_patch_len)
    v_b2g = get_b2g(is_big_machine)  # b 中描述的向量到 g 中描述的同一个向量的旋转矩阵

    # 1. 生成 1 个 slot, 分层
    slot_lines_layers = genOneUnit(z_size, r_size, radius, cylinder_p0, cylinder_dir, layer_first,
                                   line_gap, top_width, bottom_width, layer_count, layer_depth,
                                   [], "none", end_offset,
                                   True, True, False, False, v_b2g)
    # 6. 遍历层，生成随机分块
    start_time = datetime.datetime.now()
    root_path, file_path, img_path = file_prepare()
    machine_cycle = func_by_pool(genAllLayersPatches, (list(enumerate(slot_lines_layers)),
                        r_size, z_size, radius, z_patch_len, searching_radius,
                        searching_radius, blurred_radius, xy_weight, img_path,
                        root_path, v_b2g), [0], num_processes=8)
    end_time = datetime.datetime.now()
    print("生成随机分块时间:", end_time - start_time)
    # 7. 遍历层, 生成加工文件
    machine_json = {
        "machine_cycle": machine_cycle,
        "laser_params": laser_params,
        "galvo_offset": {
            "galvo_0": [0,0,0,0]
        }
    }
    with open(file_path, "w") as f:
        json.dump(machine_json, f, indent=4)
        print(f"Save success to {file_path}")
    end_time = datetime.datetime.now()
    print("生成时间:", end_time - start_time)


