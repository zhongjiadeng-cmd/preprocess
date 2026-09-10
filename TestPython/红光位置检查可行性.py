import numpy as np

# 禁用 numpy 的科学计数法输出
np.set_printoptions(suppress=True)

work_dist = 200

print(np.rad2deg(np.atan(5/200)))

laser_off_list = np.arange(-1, 1.0001, 0.05)
laser_err_list = np.arange(-1, 1.0001, 0.05)
red_off_list = np.arange(-10, 10.0001, 0.1)

print()
data_list = []
for laser_off in laser_off_list:
    # 原始的激光角度
    angle_laser = np.atan(laser_off / work_dist)
    for laser_err in laser_err_list:
        # 旋转后新的激光角度
        new_angle_laser = np.atan((laser_err + laser_off) / work_dist)
        # 旋转角度
        angle_delta = new_angle_laser - angle_laser
        for red_off in red_off_list:
            # 原始的红光位置
            red_pos = laser_off + red_off
            # 原始的红光角度
            angle_red = np.atan((red_pos) / work_dist)
            # 旋转后新的红光角度
            new_angle_red = angle_red + angle_delta
            # 新的红光位置
            new_red_pos = np.tan(new_angle_red) * work_dist
            # 红光位置偏差
            red_err = new_red_pos - red_pos
            data_list.append([laser_err, red_off, red_err, laser_err - red_err])
data_list = np.array(data_list)
# 获取 data_list 中 laser_err - red_err 最大和最小的行
max_row = data_list[data_list[:, 3] == np.max(data_list[:, 3])]
min_row = data_list[data_list[:, 3] == np.min(data_list[:, 3])]
print(f"max_row: {max_row}")
print(f"min_row: {min_row}")
