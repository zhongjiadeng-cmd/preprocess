import json
import numpy as np
from datetime import datetime, timedelta

# data_path = r"C:\Users\woodsyao\Desktop\2_6.npy"
# data = np.load(data_path)
# print(data.shape)



machine_file = r"C:\Users\woodsyao\Desktop\machine_20260724_134130.json"
with open(machine_file, "r") as f:
    data = json.load(f)
    start_time = datetime.strptime("2026-07-24 13-47-42", "%Y-%m-%d %H-%M-%S")
    now_time = datetime.strptime("2026-07-28 08-37-34", "%Y-%m-%d %H-%M-%S")
    done_count = 28436
    done_ratio = done_count/len(data["machine_cycle"])
    done_time = (now_time - start_time).total_seconds()/60.0/60.0/24.0
    all_time = done_time/done_ratio
    time_left = all_time - done_time
    all_done_time = start_time + timedelta(hours=all_time*24.0)

    print("总块数:", len(data["machine_cycle"]))
    print("已加工块数:", done_count)
    print("已加工占比:", done_ratio)
    print("预期总时间:", all_time)
    print("剩余时间:", time_left)
    print("预计完成时间:", all_done_time)
