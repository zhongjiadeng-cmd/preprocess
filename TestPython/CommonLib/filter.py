import copy
import numpy as np
from sklearn import linear_model


def filter_ransac(y, thresh=0.003):
    """ 去除线性离群点, 即离群的 y 设置为 np.nan """
    # nan 值处理
    y = y.reshape(-1, )
    x = np.arange(len(y)).reshape(-1, 1)
    y_nan = y[~np.isnan(y)]
    x_nan = x[~np.isnan(y), :]
    # 拟合
    ransac = linear_model.RANSACRegressor()
    ransac.fit(x_nan, y_nan)
    # 计算误差
    y_pred = ransac.predict(x)
    delta_y = np.abs(y - y_pred)
    y[np.where(delta_y > thresh)] = np.nan
    return y.reshape(-1, 1)


def filter_thresh_2d(arr_in, axis=1, thresh=1., max_iter=1, tol=1e-6, method=np.nanmean):
    """
    thresh 均值滤波器：去除平均值离群点后求均值, 方法: 先求均值, 再依据标准差去除均值离群点, 再求均值
    :param arr_in: 输入矩阵
    :param axis: 求均值方向
    :param thresh: 阈值
    :param max_iter: 迭代次数
    :param tol: 停止条件
    :param method: 迭代求解方法, 可以是 np.nanmean, np.nanmedian
    :return:
    """
    arr = copy.deepcopy(arr_in)
    if axis == 0:
        arr = arr.T
    # 第一次应当取中值, 避免离群点过大, 带偏均值
    mean0 = np.nanmedian(arr, axis=axis).reshape(-1, 1)
    mean = mean0
    for _ in range(max_iter):
        arr[np.where(np.abs(arr - mean0) > thresh)] = np.nan
        # 求均值
        mean = method(arr, axis=axis).reshape(-1, 1)
        # 去除均值离群点
        if np.max(np.abs(mean - mean0)) < tol:
            break
        else:
            mean0 = mean
    if axis == 1:
        mean = mean.reshape(-1, 1)
    return mean, arr


def filter_thresh_1d(arr_in, thresh=1., max_iter=5, tol=1e-6, method=np.nanmean):
    """
    去除平均值离群点后求均值, 方法: 先求均值, 再依据标准差去除均值离群点, 再求均值
    :param arr_in: 输入向量
    :param thresh: 阈值
    :param max_iter: 迭代次数
    :param tol: 停止条件
    :param method: 迭代求解方法, 可以是 np.nanmean, np.nanmedian
    :return:
    """
    arr = copy.deepcopy(arr_in)
    # 第一次应当取中值, 避免离群点过大, 带偏均值
    median_val = np.nanmedian(arr)
    out_val = median_val
    for _ in range(max_iter):
        arr[np.where(np.abs(arr - median_val) > thresh)] = np.nan
        out_val = method(arr)
        if np.abs(out_val - median_val) < tol:
            break
        else:
            median_val = out_val
    return out_val, arr


def filter_by_grad_1d(arr_in, thresh=1.):
    """ 基于梯度过滤杂点 """
    arr = copy.deepcopy(arr_in)
    arr = arr.flatten()
    grad = np.abs(arr[1:] - arr[:-1])
    grad = np.append(grad, [0])
    idx = np.where(grad > thresh)[0]
    arr[idx] = np.nan
    arr[idx + 1] = np.nan
    return arr


def filter_curve(arr_in, max_iter=3, ksize=100):
    """
    对曲线进行滤波, 去除噪点
    :param arr_in: 输入
    :param max_iter: 迭代次数, 多次迭代可以去除更细微的尖刺, 以去除不同频率的高频噪声
    :param ksize: 均值的核大小, 越大则滤越平滑
    :return:
    """
    arr = arr_in.flatten()
    len_arr = len(arr)
    arr_reverse = arr[::-1]
    arr = np.hstack((arr_reverse, arr, arr_reverse))
    # 迭代滤波
    for _ in range(max_iter):
        arr = np.convolve(arr, np.ones(ksize) / ksize, mode='same')
    arr = arr[len_arr:len_arr * 2]
    return arr.reshape(arr_in.shape)


def split_arr_by_condition_1d(arr_in, condition, step=1):
    """ 按条件分割数组 """
    x_arr = np.arange(len(arr_in))
    x_in_arr = np.vstack((x_arr, arr_in)).T
    x_in_arr_valid = x_in_arr[np.where(condition)]
    x_in_arr_valid_list = np.split(x_in_arr_valid, np.where(np.diff(x_in_arr_valid[:, 0]) > step)[0] + 1)
    return x_in_arr_valid_list
