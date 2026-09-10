import copy
import numpy as np
from sklearn.neighbors import NearestNeighbors


def principal_direction(pts, method="plane"):
    """ 分析点集的主方向 """
    pts_range = np.max(pts, axis=0) - np.min(pts, axis=0)
    if method == "plane":
        # 平面点集的主方向为极差最小的方向
        idx = np.argmin(pts_range)
        x = copy.deepcopy(pts)
        x = np.delete(x, idx, axis=1)
        y = pts[:, idx]
        return x, y, idx
    elif method == "line":
        # 直线点集的主方向为极差最大的方向
        idx = np.argmax(pts_range)
        y = copy.deepcopy(pts)
        y = np.delete(y, idx, axis=1)
        y1, y2 = y[:, 0], y[:, 1]
        x = pts[:, idx].reshape(-1, 1)
        return x, y1, y2, idx
    elif method == "line_2d":
        idx = np.argmax(pts_range)
        y = copy.deepcopy(pts)
        y = np.delete(y, idx, axis=1)
        x = pts[:, idx].reshape(-1, 1)
        return x, y, idx
    else:
        raise ValueError("method must be one of 'plane', 'line'")


def resort_plane_par(a, b, c, d, idx):
    """ 重新排序, 在 idx 处插入 c """
    # 归一化
    abc = np.array([a, b, c])
    norm_abc = np.linalg.norm(abc)
    a, b, c = abc / norm_abc
    d = d / norm_abc
    # 重新排序
    abd = np.array([a, b, d])
    a, b, c, d = np.insert(abd, idx, c, axis=0)
    return a, b, c, d


def resort_line_par(a, b, c, idx, normalize=True):
    """ 归一化并重新排序, 在 idx 处插入 c """
    if normalize:
        abc = np.array([a, b, c])
        norm_abc = np.linalg.norm(abc)
        a, b, c = abc / norm_abc
    ab = np.array([a, b])
    abc = np.insert(ab, idx, c, axis=0)
    return abc


class NNInterpolate:

    def __init__(self, pts, values, method="kd_tree"):
        self.__pts__ = pts
        self.__values__ = values
        self.__nn__ = NearestNeighbors(n_neighbors=2, algorithm=method).fit(pts)

    def interpolate(self, new_pts):
        distances, indices = self.__nn__.kneighbors(new_pts)
        t1 = self.__values__[indices[:, 0]]
        t2 = self.__values__[indices[:, 1]]
        d1 = distances[:, 0]
        d2 = distances[:, 1]
        p1 = self.__pts__[indices[:, 0]]
        p2 = self.__pts__[indices[:, 1]]
        d = np.linalg.norm(p1 - p2, axis=1)
        new_value = []
        for i in range(len(d)):
            tmp_d, tmp_d1, tmp_d2 = d[i], d1[i], d2[i]
            tmp_t1, tmp_t2 = t1[i], t2[i]
            max_d = max(tmp_d1, tmp_d2, tmp_d)
            if max_d == tmp_d:
                tn = (tmp_d1 * tmp_t2 + tmp_d2 * tmp_t1) / (tmp_d1 + tmp_d2)
            elif max_d == tmp_d1:
                tn = tmp_t2 - (tmp_t2 - tmp_t1) / tmp_d * tmp_d2
            else:
                tn = tmp_t1 - (tmp_t2 - tmp_t1) / tmp_d * tmp_d1
            new_value.append(tn)
        return np.array(new_value)
