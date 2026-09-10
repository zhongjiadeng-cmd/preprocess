import datetime
from MachineFunc.gen_machine_file import gen_all_layer_patch_plane
from MachineFunc.machine_file_viewer import viewPlane

if __name__ == '__main__':
    # 1. 沟槽的数量和层数
    h_count = 14
    v_count = 16
    layer_count = 40  # 层数
    layer_depth = 0.13 / (layer_count-1)
    top_width = 0.35
    bottom_width = 0.12
    line_gap = 0.02
    # 2. 精加工/激光喷砂
    # fine_gap = 0.01
    fine_gap = -1
    laser_idx_list = [0] * layer_count

    # 2. 随机打点的参数
    random_pts_time = 5  # 单位: 10us, 5 代表 50us
    random_pts_density = 500  # 个/平方毫米
    random_pts_method = "none"  # "pretreat" or "post_treat" or "none" or "both"

    # 3. 分块参数
    searching_radius = 14000  # 分块搜索半径, 分块大小是此半径的2倍
    blurred_radius = 6000  # 分块内凹半径, 内凹大小是此半径的2倍
    searching_ratio = [1.0] * 40  # 从第一层开始依次使用此比例, 缩放搜索半径和模糊半径; 层数比此多时, 循环使用此列表中的值
    blurred_ratio = [1.0] * 40  # 从第一层开始依次使用此比例, 缩放模糊半径; 层数比此多时, 循环使用此列表中的值
    searching_radius_cycle = [searching_radius * searching_ratio[i] for i in range(len(searching_ratio))]
    blurred_radius_cycle = [blurred_radius * blurred_ratio[i] for i in range(len(blurred_ratio))]
    fill_line_angle_list = []  # 各层的填充角度, 单位: 度; 空, 则按照优化算法进行自动填充
    layer_first = True  # 是否优先加工层; 否则先加工一个块的所有层

    # 4. 激光参数
    laser_params =  [
        {  # 以下是加工层的激光参数
            "power": 38,
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
            "frequency": 100,
            "power": 10,
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
        },
        {  # 以下是精加工层的激光参数
            "power": 20,
            "frequency": 350,
            "pulseWidthIdx": 4,
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
    ]

    # 5. xyz 的偏置
    base_xyz = [0,0,0]  # 为空时不移动, 否则按照此处为中心点移动
    workpiece_mode = False  # 是否是工件模式, 否则需要设置 base_xyz; 是则按照 base_xyz=[0,0,0] 生成工件坐标系下的加工文件

    # 6. 沟槽的形状参数, 固定
    h_space = 1.05
    v_space = 0.87
    end_offset = 0.00  # 线头补偿(增长)值

    # 1. 生成平面加工文件
    start_time = datetime.datetime.now()
    last_time = start_time
    print()
    print("Start to generate plane machine file")
    file_path = gen_all_layer_patch_plane(laser_params, base_xyz, h_space, v_space, h_count, v_count, line_gap, top_width,
                              bottom_width, layer_count, layer_depth, True, fine_gap, laser_idx_list,
                              fill_line_angle_list, "none",
                              end_offset, True, True, False, False, "random",
                              searching_radius_cycle, blurred_radius_cycle,
                              random_pts_time, random_pts_density, random_pts_method,
                              workpiece_mode)


    end_time = datetime.datetime.now()
    print("生成时间:", end_time - start_time)

    viewPlane(file_path, workpiece_mode)