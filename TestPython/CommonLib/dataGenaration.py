import numpy as np


def generate_smooth_wave(n_points=500, n_harmonics=5, closed=False,
                         min_val=0, max_val=1):
    """
    叠加多个随机正弦波，生成平滑波浪线
    :param n_points: 点的数量
    :param n_harmonics: 正弦波的谐波数量
    :param closed: 是否闭合波浪线
    :param min_val: 波浪线的最小值
    :param max_val: 波浪线的最大值
    :return: 波浪线的 x 坐标和 y 坐标
    """
    #
    if closed:
        new_n_points = int(n_points / 2) + 1
    else:
        new_n_points = n_points


    x = np.linspace(0,  20 * np.pi, new_n_points)
    y = np.zeros(new_n_points)

    for _ in range(n_harmonics):
        amplitude = np.random.uniform(0.3, 1.5)
        frequency = np.random.uniform(0.5, 3.0)
        phase = np.random.uniform(0, 2 * np.pi)
        y += amplitude * np.sin(frequency * x + phase)
    # 归一化
    y_min = y.min()
    y_max = y.max()
    y = (y - y_min) / (y_max - y_min)
    y = min_val + (max_val - min_val) * y
    # 闭合波浪线时, 需要添加尾部的点
    if closed:
        tail_count = n_points - new_n_points
        tail_y = y[1:tail_count+1]
        tail_y = tail_y[::-1]
        x = np.array(range(n_points))
        y = np.concatenate((y, tail_y))
    return x, y