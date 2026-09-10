import json


if __name__ == '__main__':
    continue_batch_id = 6
    remove_galvo_list = []
    laser_idx_list = {'galvo_0': 0, 'galvo_1': 0, 'galvo_2': 1, 'galvo_3': 1}
    # remove_galvo_list = ["galvo_1"]

    machine_file = 'machine_file_20260625_165259/machine_20260625_170426.json'
    machine_json = json.load(open(machine_file))
    machine_cycle = machine_json['machine_cycle']
    machine_cycle = machine_cycle[continue_batch_id:]

    # 需要强制设定振镜参数为 0, 抵制振镜初始化对激光器参数/振镜参数的变更
    new_machine_cycle_0 = machine_cycle[0].copy()
    for galvo in machine_cycle[0]:
        if galvo == "common_move":
            continue
        if galvo in remove_galvo_list:
            del new_machine_cycle_0[galvo]
        else:
            new_machine_cycle_0[galvo][0] = 0
    machine_cycle[0] = new_machine_cycle_0

    machine_json['machine_cycle'] = machine_cycle
    machine_file = machine_file.replace('.json', f'_continue_{continue_batch_id}.json')

    # 设置激光器参数
    done_list = []
    for itm in machine_cycle:
        for (galvo_name, laser_idx) in laser_idx_list.items():
            if galvo_name in itm and galvo_name not in done_list:
                itm[galvo_name][0] = laser_idx
                done_list.append(galvo_name)

    with open(machine_file, 'w') as f:
        json.dump(machine_json, f, indent=4)

