import os
import json

root = os.path.dirname(os.path.abspath(__file__))
root = os.path.dirname(root)
root = os.path.dirname(root)
print(root)

file_path = os.path.join(root, "SubProjects/Calibration5Axis/exe/config/woodsLib/")

ref_file = "all_cfg_roller.json"
ref_data = json.load(open(os.path.join(file_path, ref_file), "r"))
tag_file_list = [
    "all_cfg_axis5_105.json",
    "all_cfg_axis5_big.json",
]

for file in tag_file_list:
    if file == ref_file:
        continue
    print(file_path)
    tag_data = json.load(open(os.path.join(file_path, file), "r"))
    for itm in ['axis', 'camera', 'galvo' ,'probe']:
        for key in ref_data[itm]["config"]:
            if key not in tag_data[itm]["config"]:
                print(f"Missing key {key} in {file} {itm}")

