import copy
import numpy as np
from scipy.interpolate import interp1d
from sklearn.linear_model import TheilSenRegressor, RANSACRegressor, HuberRegressor, LinearRegression

from CommonLib.filter import filter_curve, filter_thresh_2d
from CommonLib.points_base import principal_direction, resort_plane_par


class FeatureFittingRobust:
    """ 抗噪稳定型特征拟合 """

    def __init__(self, pts=None, method="ransac", max_iter=300, min_pts=300, epsilon=1., alpha=1e-4):
        self.pts = pts
        self.max_iter = max_iter
        self.min_pts = min_pts
        self.epsilon = epsilon
        self.method = method
        self.alpha = alpha

    def __change_input__(self, pts=None, method=None, max_iter=None, min_pts=None, epsilon=None, alpha=None):
        if pts is not None:
            self.pts = pts
        if method is not None:
            self.method = method
        if max_iter is not None:
            self.max_iter = max_iter
        if min_pts is not None:
            self.min_pts = min_pts
        if epsilon is not None:
            self.epsilon = epsilon
        if alpha is not None:
            self.alpha = alpha

    def __fit_robust__(self, x, y):
        if self.method == "ransac":
            if self.min_pts > len(x):
                min_pts = len(x)
            else:
                min_pts = self.min_pts
            ransac = RANSACRegressor(max_trials=self.max_iter, min_samples=min_pts, random_state=0, residual_threshold=self.epsilon)
            ransac.fit(x, y)
            a_n = -ransac.estimator_.coef_
            bias = -ransac.estimator_.intercept_
        elif self.method == "theil-sen":
            theil = TheilSenRegressor(max_iter=self.max_iter, random_state=0)
            theil.fit(x, y)
            a_n = -theil.coef_
            bias = -theil.intercept_
        elif self.method == "huber":
            huber = HuberRegressor(max_iter=self.max_iter, epsilon=self.epsilon, alpha=self.alpha)
            huber.fit(x, y)
            a_n = -huber.coef_
            bias = -huber.intercept_
        elif self.method == "OLS":
            reg = LinearRegression().fit(x, y)
            a_n = -reg.coef_
            bias = -reg.intercept_
        else:
            raise ValueError("method must be one of 'ransac', 'theil-sen', 'huber'")
        return a_n, bias

    def fit_line_2d(self, pts=None, method=None, max_iter=None, min_pts=None, epsilon=None, alpha=None):
        self.__change_input__(pts, method, max_iter, min_pts, epsilon, alpha)
        # 判断法矢的主方向 (即极差最小的方向), 提高拟合精度 ax + by + c = 0, y = -a/b*x - c/b
        x, y, idx = principal_direction(self.pts, method="line_2d") # type: ignore
        b = 1
        a, c = self.__fit_robust__(x, y)
        a = a[0][0]
        c = c[0] # type: ignore
        if idx == 0:
            a, b, c = a, b, c
        elif idx == 1:
            a, b, c = b, a, c
        return a, b, c

    def fit_plane(self, pts=None, method=None, max_iter=None, min_pts=None, epsilon=None, alpha=None):
        self.__change_input__(pts, method, max_iter, min_pts, epsilon, alpha)
        # 判断法矢的主方向 (即极差最小的方向), 提高拟合精度 z = -a/c*x - b/c*y - d/c, z 为主方向
        x, y, idx = principal_direction(self.pts, method="plane") # type: ignore
        c = 1
        ab, d = self.__fit_robust__(x, y)
        a, b = ab
        return resort_plane_par(a, b, c, d, idx)


class FeatureFittingImg:
    """ 用于批量拟合图片中的特征 """
    
    def __init__(self, arr=None, y0=0, par_dict=None, y_pitch=1) -> None:
        """
        批量拟合特征
        :param arr: 待拟合特征的矩阵
        :param y0: 起始点的 y 值, 单位为像素, 因此后续需要乘以 y_pitch
        :param par_dict: 参数字典
        """
        self.arr = arr
        if par_dict is None:
            par_dict = {}
        if 'thresh' not in par_dict:  # 梯度过滤器的固定阈值
            par_dict['thresh'] = 0.1
        if 'filter_size' not in par_dict:  # k 和 bias 平滑的窗口大小
            par_dict['filter_size'] = 100
        if 'filter_iter' not in par_dict:  # k 和 bias 平滑的迭代次数
            par_dict['filter_iter'] = 3
        if 'interp_method' not in par_dict:  # 插值的方法
            par_dict['interp_method'] = 'slinear'
        self.par_dict = par_dict
        self.y0 = y0
        self.y_pitch = y_pitch
        
    def fit_lines(self, arr=None, y0=None, par_dict=None, y_pitch=None):
        """ 批量拟合 2D 直线, 按行输出 k 和 bias 向量 """
        if arr is not None:
            self.arr = arr
        if y0 is not None:
            self.y0 = y0
        if par_dict is not None:
            self.par_dict = par_dict
        if y_pitch is not None:
            self.y_pitch = y_pitch
        self.par_dict = copy.deepcopy(self.par_dict)
        self.arr = copy.deepcopy(self.arr)
        # 求解斜率
        z_grad = (self.arr[:, 1:] - self.arr[:, :-1]) / self.y_pitch # type: ignore
        # 求解斜率
        k, z_grad = filter_thresh_2d(z_grad, axis=1, thresh=self.par_dict['thresh'], method=np.nanmean)
        # 构建 y_arr
        y_arr = np.arange(self.arr.shape[1], dtype=float) * self.y_pitch # type: ignore
        y_arr = np.tile(y_arr, (self.arr.shape[0], 1)) # type: ignore
        # z_grad 为 nan 则 y、z 和 k 置为 nan, 避免参与均值计算
        idx_left = np.where(np.isnan(z_grad))
        idx_right = (idx_left[0], idx_left[1] + 1)
        self.arr[idx_left] = np.nan # type: ignore
        self.arr[idx_right] = np.nan # type: ignore
        y_arr[idx_left] = np.nan
        y_arr[idx_right] = np.nan
        # 按行求中点坐标
        y_mean = np.nanmean(y_arr, axis=1).reshape(-1, 1)
        z_mean = np.nanmean(self.arr, axis=1).reshape(-1, 1) # type: ignore
        # 求解 bias
        y_mean = y_mean + self.y0 * self.y_pitch
        bias = z_mean - y_mean * k
        # 对 k 和 bias 进行插值, 填充 nan 区域
        x = np.arange(len(k)).reshape(-1, 1)
        x_valid = x[~np.isnan(y_mean)]
        k = k[x_valid]
        bias = bias[x_valid]
        interp_k = interp1d(x_valid, k, kind=self.par_dict['interp_method'], fill_value='extrapolate', axis=0) # type: ignore
        interp_bias = interp1d(x_valid, bias, kind=self.par_dict['interp_method'], fill_value='extrapolate', axis=0) # type: ignore
        k = interp_k(x).reshape(-1, 1)
        bias = interp_bias(x).reshape(-1, 1)
        # 对 k 和 bias 进行滤波
        k = filter_curve(k, self.par_dict['filter_iter'], self.par_dict['filter_size'])
        bias = filter_curve(bias, self.par_dict['filter_iter'], self.par_dict['filter_size'])
        return k, bias
