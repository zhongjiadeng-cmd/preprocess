import datetime
import orjson
from MachineFunc.cluster_texture import modify_z_size, get_b2g
from MachineFunc.machine_file_viewer import viewCylinder
from MachineFunc.post_treat import merge_galvo_layers, getGalvoOffset
import os
# 禁用科学计法
import numpy as np
np.set_printoptions(suppress=True)

if __name__ == '__main__':
    machine_file = 'machine_file_20260812_093151/machine.json'
    galvo_list = ['galvo_0', 'galvo_1', 'galvo_2', 'galvo_3']
    laser_idx_list = {
        'galvo_0': [0] + [-1] * 100,
        'galvo_1': [0] + [-1] * 100,
        'galvo_2': [1] + [-1] * 100,
        'galvo_3': [1] + [-1] * 100,
    }
    # galvo_list = ['galvo_0']
    galvo_gap = 250.0  # 100.0

    check_batch_gap = 5e12
    check_laser_idx = -2  # -1 表示红光, -2 表示残留光; ≥0 为激光参数索引
    check_correction = False
    z_size = 1.05
    v_b2g = get_b2g(is_big=False)
    move_speed = 80

    test_mode = True
    z_range = [215.0, 1115.0]
    c_range = [0,20]
    layer_idx_list = [20]
    z_axis_range = [151, 1000]
    # onfix
    start_z_random = 50.0
    zc_speed = [80,80]


    cfg_path = 'D:/BaiduSyncdisk/1_code/EVision/SubProjects/Calibration5Axis/exe/config/woodsLib/all_cfg_roller.json'
    # cfg_path = 'E:/0-yxm/EVision/APP/Calibration5Axis/x64/Debug/Config/woodsLib/all_cfg_roller.json'
    # cfg_path = 'D:/0-yxm/EVision/APP/Calibration5Axis/x64/Debug/Config/woodsLib/all_cfg_roller.json'
    with open(cfg_path, "rb") as f:
        all_cfg = orjson.loads(f.read())

    galvo_z_pos = {
        'theory': {
            'galvo_list': galvo_list,
            'gap': galvo_gap,
        },
        'measure': {'galvo_0': 0, 'galvo_1': 250, 'galvo_2': 500, 'galvo_3': 750}
    }
    for galvo_name in galvo_z_pos['theory']['galvo_list']:
        galvo_z_pos['measure'][galvo_name] = all_cfg['galvo'][galvo_name]['pos'][2]
    z_size, _ = modify_z_size(z_size, 0, galvo_z_pos['theory']['gap'])


    with open(machine_file, "rb") as f:
        machine_json = orjson.loads(f.read())
    machine_cycle = machine_json['machine_cycle']
    # galvo_0 一定是基准振镜0
    # 1. 计算振镜偏置
    machine_json['galvo_offset'] = getGalvoOffset(galvo_z_pos)
    # 2. 多振镜拼接
    cache_path = os.path.split(machine_file)[0] + "/patches/"
    start_time = datetime.datetime.now()
    machine_cycle = merge_galvo_layers(machine_cycle, cache_path, v_b2g, galvo_z_pos, z_size,
                                       z_axis_range, z_range, c_range, layer_idx_list,
                                       start_z_random, move_speed,
                                       check_batch_gap, check_laser_idx, check_correction, zc_speed)
    end_time = datetime.datetime.now()
    print(f"merge_galvo_layers time: {end_time - start_time}")
    # 3. 基于 laser_idx_list 修正激光参数索引
    done_list = {}  # tmp_layer: galvo_name_list
    for itm in machine_cycle:
        for (tmp_galvo_name, tmp_laser_idx_list) in laser_idx_list.items():  # 遍历 laser_idx_list
            if tmp_galvo_name in itm:  # 当前块有该振镜
                tmp_layer, tmp_patch = itm[tmp_galvo_name][2]
                if tmp_layer >= 10000:
                    tmp_layer = int(tmp_layer / 10000 - 1)
                if not tmp_layer in done_list:  # 加入层号到 done_list
                    done_list[tmp_layer] = []
                if not tmp_galvo_name in done_list[tmp_layer]:  # 加入振镜到 done_list
                    done_list[tmp_layer].append(tmp_galvo_name)
                    itm[tmp_galvo_name][0] = tmp_laser_idx_list[tmp_layer]
    # 4. 保存文件
    now_str = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    machine_file = machine_file.replace('machine.json', f'machine_{now_str}.json')
    machine_json['machine_cycle'] = machine_cycle


    # 测试模式: 进行可视化, 并减小激光功率
    if test_mode:
        for i in range(len(machine_json["laser_params"])):
            machine_json["laser_params"][i]["power"] = 5
            machine_json["laser_params"][i]["frequency"] = 100
            machine_json["laser_params"][i]["pulseWidthIdx"] = 0

    with open(machine_file, 'w') as f:
        json_str = orjson.dumps(machine_json, option=orjson.OPT_SERIALIZE_NUMPY
                                                    | orjson.OPT_INDENT_2
                                                    | orjson.OPT_APPEND_NEWLINE
                                ).decode("utf-8")
        f.write(json_str)

    if test_mode:
        # 5. 可视化
        start_time = datetime.datetime.now()
        viewCylinder(machine_file, galvo_z_pos, v_b2g)
        end_time = datetime.datetime.now()
        print(f"viewCylinder time: {end_time - start_time}")


