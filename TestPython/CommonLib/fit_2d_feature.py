from math import sqrt
import numpy as np
from numpy import polyval, polyfit, sqrt
from scipy import optimize


class FeatureFitting2d:
    def __init__(self, points):
        self.points = points  # 2D 点群

    def fit_line_2d(self, ratio_remove=7):
        """
        拟合2D直线,  以 p0, dir0 的方式输出 —— 点法式
        @param ratio_remove: 首尾去除的直线比例
        @return:
        """
        # 去除首尾的点
        if len(self.points) > ratio_remove > 0:
            remove_count = int(np.ceil(len(self.points) / ratio_remove))
            self.points = self.points[remove_count:(len(self.points) - remove_count - 1), :]
        # 拟合点
        x_tmp = self.points[:, 0]
        mean_x = np.mean(x_tmp)
        y_tmp = self.points[:, 1]
        delta_x = np.max(x_tmp) - np.min(x_tmp)
        delta_y = np.max(y_tmp) - np.min(y_tmp)
        if delta_y > delta_x:
            # 拟合 y-x
            (k, b) = polyfit(y_tmp, x_tmp, 1)  # x, y, 1阶
            xr = polyval([k, b], y_tmp)
            out_rms = sqrt(sum((xr - x_tmp) ** 2) / len(y_tmp))
            k = np.round(k, 8)
            b = np.round(b, 8)
            if k == 0:
                p0 = [b, 0]
            else:
                p0 = [mean_x, (mean_x-b) / k]
            dir0 = [k / sqrt(k * k + 1), 1 / sqrt(k * k + 1)]  # 归一化后的方向
            # 保持 dir0 和输入方向一致
            if y_tmp[0] > y_tmp[-1]:
                dir0 = [-dir0[0], -dir0[1]]
        else:
            # 拟合 x-y
            (k, b) = polyfit(x_tmp, y_tmp, 1)  # x, y, 1阶
            k = np.round(k, 8)
            b = np.round(b, 8)
            yr = polyval([k, b], x_tmp)
            if k == 0:
                p0 = [0, b]
            else:
                p0 = [mean_x, k * mean_x + b]
            dir0 = [1 / sqrt(k * k + 1), k / sqrt(k * k + 1)]  # 归一化后的方向
            out_rms = sqrt(sum((yr - y_tmp) ** 2) / len(x_tmp))
            if x_tmp[0] > x_tmp[-1]:
                dir0 = [-dir0[0], -dir0[1]]
        p0 = np.array(p0)
        dir0 = np.array(dir0)
        return p0, dir0, out_rms

    def fit_circle_2d(self, ratio_remove=6):
        """拟合2D圆弧"""
        if len(self.points) > ratio_remove > 0:
            # 去除首尾的点
            remove_count = int(np.ceil(len(self.points) / ratio_remove))
            self.points = self.points[remove_count:(len(self.points) - remove_count - 1), :]
        x = self.points[:, 0]
        y = self.points[:, 1]
        # coordinates of the barycenter
        x_m = np.mean(x)
        y_m = np.mean(y)

        def calc_r(xc, yc):
            """ 计算每个2D点到中心(xc, yc)的距离"""
            return np.sqrt((x - xc) ** 2 + (y - yc) ** 2)

        def f_2(c):
            """ 计算数据点与以 c=(xc, yc) 为中心的平均圆之间的代数距离 """
            ri = calc_r(*c)
            return ri - ri.mean()

        center_estimate = x_m, y_m
        center_2, _ = optimize.leastsq(f_2, center_estimate)
        xc_2, yc_2 = center_2
        ri_2 = calc_r(*center_2)
        r_2 = ri_2.mean()
        residue_2 = sqrt(sum((ri_2 - r_2) ** 2) / len(self.points))
        return xc_2, yc_2, r_2, residue_2

    def fit_ellipse_2d(self, boundary_ratio: float = 0.5):
        # 拟合圆
        x0, y0, r0, _ = self.fit_circle_2d(ratio_remove=-1)

        # 边界条件
        boundary = boundary_ratio * r0
        boundary = np.array([-boundary, boundary])

        # 计算各个点下的长轴长度
        def a_len(f1, f2):
            """ 计算各个点下的长轴长度 a """
            pf1 = np.sqrt(np.sum(np.square(self.points - f1), axis=1))
            pf2 = np.sqrt(np.sum(np.square(self.points - f2), axis=1))
            return (pf1 + pf2) / 2

        # 计算长轴 a 和 迭代a 的差异向量
        def deviation_vector(init_input):
            """ 计算长轴 a 和均值的差异向量 """
            f1_x, f1_y, f2_x, f2_y = init_input
            f1 = np.array([f1_x, f1_y])
            f2 = np.array([f2_x, f2_y])
            deviation = a_len(f1, f2)
            opt_a = deviation.mean()
            factor = 1
            return (deviation - opt_a) * factor

        def rms_err(init_input):
            """ 依据焦点, 求解 rms_limit """
            f1_x, f1_y, f2_x, f2_y = init_input
            rms = deviation_vector([f1_x, f1_y, f2_x, f2_y])
            rms = sqrt(np.sum(np.square(rms)) / len(self.points)) * 1e8
            return rms

        # 初始值
        f1_0 = np.array([x0 - r0 * boundary_ratio / 2, y0 - r0 * boundary_ratio / 2])
        f2_0 = np.array([x0 + r0 * boundary_ratio / 2, y0 + r0 * boundary_ratio / 2])
        # 边界条件
        bounds = (tuple(boundary + x0),  # f1_X,
                  tuple(boundary + y0),  # f1_Y,
                  tuple(boundary + x0),  # f2_X,
                  tuple(boundary + y0))  # f2_Y,
        # minimize
        init = np.hstack((f1_0, f2_0))
        solution = optimize.minimize(rms_err, init, bounds=bounds, tol=1e-10, options={'maxiter': 1e10},
                                     method='L-BFGS-B')
        f12 = solution.x
        rms_out = solution.fun
        # 准备输出数据
        f1_out_x, f1_out_y, f2_out_x, f2_out_y = f12
        f1_out = np.array([f1_out_x, f1_out_y])
        f2_out = np.array([f2_out_x, f2_out_y])
        a = a_len(f1_out, f2_out).mean()
        c = np.linalg.norm(f1_out - f2_out) / 2
        b = sqrt(a ** 2 - c ** 2)
        return f1_out, f2_out, a, b, rms_out / 1e8

