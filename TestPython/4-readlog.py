
import datetime
import numpy as np
from matplotlib import pyplot as plt
import json


# 1. 统计各个 batch 的加工时间
log_path = r"C:\Users\woodsyao\Desktop\log\2026-06-05.log"
with open(log_path, "r") as f:
    lines = f.readlines()
    time_list = []
    for i in range(len(lines)):
        # 仅取包含 [Info] 的行
        if "Mark gcode in batch 0 " in lines[i]:
            time_list.clear()
        if "Mark gcode in batch" in lines[i] and i+1 < len(lines):
            start_time = lines[i][1:20]
            end_time = lines[i+1][1:20]
            end_time = datetime.datetime.strptime(end_time, "%Y-%m-%d %H-%M-%S")
            start_time = datetime.datetime.strptime(start_time, "%Y-%m-%d %H-%M-%S")
            time_second = (end_time-start_time).total_seconds()
            time_list.append(time_second)


# fixlater 2. 统计各个 batch 的大小
data = "machine_file_20260604_143243/machine_20260605_141739.json"
with open(data, "r") as f:
    data = json.load(f)
    patch_idx = []
    patch_size = []
    for i in range(len(time_list)):
        patch_info = data["machine_cycle"][i]["galvo_0"][2]
        patch_idx.append(patch_info)
        patch_data = np.load(f"machine_file_20260604_143243/patches/{patch_info[0]}_{patch_info[1]}.npy")
        patch_size.append(patch_data.shape[0])



time_list = np.array(time_list)
patch_size = np.array(patch_size)
# 分别归一化
time_list = time_list / time_list.max()
patch_size = patch_size / patch_size.max()

plt.plot(time_list, "b-")
plt.plot(patch_size, "r--")
plt.grid()
plt.show()
