import json
import numpy as np
from matplotlib import pyplot as plt
import open3d as o3d


def merge_pts(pts_list, voxel_size):
    """
    合并多张图采集的轮廓点数据
    :param pts_list: 各图的轮廓点数据,格式为[[[x11, y11], [x12, y12],...], [[x21, y21], [x22, y22],...],...]
    :param voxel_size: 输出的体素大小,单位为mm
    :param show_fig: 是否显示合并结果图
    :return: 合并后的轮廓点数据
    """
    # fig = plt.figure()
    # ax = fig.add_subplot(111)
    # for i in range(4):
    #     ax.scatter(pts_list[i][:,0], pts_list[i][:,1], label=str(i+1))
    # ax.legend()
    # plt.show()
    # 1. 合并并转 3D
    pts = np.vstack(pts_list)
    pts = np.hstack((pts, np.zeros((pts.shape[0], 1))))
    pcd = o3d.geometry.PointCloud()
    pcd.points = o3d.utility.Vector3dVector(pts)
    # 2. 降采样
    new_pcd = pcd.voxel_down_sample(voxel_size=voxel_size)
    # 3. 统计滤波
    new_pcd, ind = new_pcd.remove_statistical_outlier(nb_neighbors=20, std_ratio=2.0)
    # if show_fig:
    #     pcd.paint_uniform_color([1, 0, 0])
    #     new_pcd.paint_uniform_color([0, 1, 0])
    #     o3d.visualization.draw_geometries([new_pcd, pcd]) # type: ignore
    out_pts = np.array(new_pcd.points)
    # 按照 x 坐标排序
    out_pts = out_pts[out_pts[:,0].argsort()]
    # if show_fig:
    #     fig = plt.figure()
    #     ax = fig.add_subplot(111)
    #     ax.plot(out_pts[:,0], out_pts[:,1], c='r', linestyle='-', marker='*', label='filtered')
    #     ax.scatter(pts[:,0], pts[:,1], c='b', label='raw')
    #     ax.legend()
    #     plt.show()
    return out_pts
