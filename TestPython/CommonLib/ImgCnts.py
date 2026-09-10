import numpy as np


def convert_hierarchy_tree_to_ccomp(hierarchy_tree):
    """
    将多层嵌套的结构转换为 2 层嵌套
    :param hierarchy_tree: 层次结构树
    :return: external_idx_list: 外部轮廓索引的列表 [idx1, idx2, idx3,...]；
             comp_idx_list: 双层的子轮廓索引的列表 [batch1, batch2, ...],
                            batch1 = [[idx1, idx2], [idx3], ...],
                                    batch 为外轮廓及其内的所有子轮廓
                                    [idx1, idx2] 和 [idx3] 代表一个双层子轮廓
                                    batch 中的第一个 idx 一定为外轮廓
    """
    hierarchy_tree = hierarchy_tree[0]
    cnts_counts = hierarchy_tree.shape[0]
    # 1. 根节点的索引, 用于索引外部轮廓
    external_idx_list = np.where(hierarchy_tree[:, 3] == -1)[0].tolist()
    # 2. 为每个轮廓划入 parent_list 或 sub_list
    all_list = list(range(cnts_counts))
    comp_idx_list = []
    for idx in external_idx_list:
        all_list.remove(idx)
        comp_idx_list.append([[idx]])
    while True:
        # 遍历 all_list, 如果其 parent 已经在 comp_idx_list 中, 则将其加入 comp_idx_list 的对应子列表
        tmp_all_list = all_list
        for idx in tmp_all_list:
            parent_idx = hierarchy_tree[idx, 3]
            # 遍历所有 batch
            idx_removed = False
            for i, batch in enumerate(comp_idx_list):
                # 遍历所有单双层子轮廓
                for j, sub_batch in enumerate(batch):
                    # 如果父轮廓在 sub_batch 中
                    if parent_idx in sub_batch:
                        # 如果 sub_batch 为单层, 则将 idx 加入到 sub_batch 中
                        if (len(sub_batch) == 1) or (parent_idx == sub_batch[0]):
                            comp_idx_list[i][j].append(idx)
                        # 如果 sub_batch 为双层, 则将 idx 加入到新的 sub_batch 中
                        else:
                            comp_idx_list[i].append([idx])
                        idx_removed = True
                        all_list.remove(idx)
                        break
                if idx_removed:
                    break
        if len(all_list) == 0:
            break
    return external_idx_list, comp_idx_list