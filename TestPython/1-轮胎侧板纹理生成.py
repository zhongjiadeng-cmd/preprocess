import json
import numpy as np

slot_gap = 0.16

all_depth = 0.3
layer_depth = 0.001

line_space = 0.01

top_width = 0.08
bottom_width = 0.05

start_x = 5.0
start_y = 5.0

all_data = {
    "machine_cycle": [],
    "laser_gcode": [],
    "laser_params": [
        {"accScale": 50,"cornerScale": 100,"endScale": 100,"frequency": 350,"power": 10,"pulseWidthIdx": 3,"scanSpeed": 2100},
    ],
}

layer_count = int(all_depth / layer_depth)
slot_count = int(start_x*2 / slot_gap)

# onfix 需要支持新的格式
# 遍历, 每层生成一个块
for i in range(0, layer_count):
    tmp_gcode = ""
    tmp_layer_width = top_width - (top_width - bottom_width) * (i / layer_count)
    tmp_layer_count = np.ceil(tmp_layer_width / line_space)
    tmp_line_space = tmp_layer_width / tmp_layer_count
    # 每层的轨迹不一致, tmp_layer_count 条线
    for k in range(0, slot_count):
        tmp_start_x = -start_x + k * slot_gap
        tmp_x = -(tmp_start_x - tmp_layer_width / 2)
        tmp_y = start_y
        for j in range(0, int(tmp_layer_count)):
            tmp_x += tmp_line_space
            tmp_gcode += f"G00X{tmp_x}Y{tmp_y}\nG01X{tmp_x}Y{-tmp_y}\n"
            tmp_y = -tmp_y
    all_data["laser_gcode"].append(tmp_gcode)
    laser_params_idx = 0
    if i>0:
        laser_params_idx = -1
    all_data["machine_cycle"].append({"galvo_0":  [laser_params_idx, f"G91\nG00Z{-layer_depth}F80", i]})

# 保存
with open("machine_1.json", "w") as f:
    json.dump(all_data, f, indent=4)
    print("Save success")


