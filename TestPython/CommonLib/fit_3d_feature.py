import numpy as np
from scipy import optimize
from sklearn.linear_model import LinearRegression
from CommonLib.Geometry3D import distance_pts_to_line3d, project_pts_on_plane_along_dir
from CommonLib.fit_2d_feature import FeatureFitting2d
from CommonLib.points_base import principal_direction, resort_line_par, resort_plane_par


class FeatureFitting3d:
    def __init__(self, points: np.ndarray, rms_limit=0.5, max_iter=5):
        self.points = points  # 3d 点群
        self.rms_limit = rms_limit  # 拟合收敛的判据
        self.max_iter = max_iter  # 最大循环次数

    def fit_plane(self, points=None):
        """拟合平面"""
        if points is not None:
            self.points = points
        x, y, idx = principal_direction(self.points, method="plane")  # type: ignore # 求解主方向索引及拟合点集
        c = 1
        reg = LinearRegression().fit(x, y)
        a, b = -reg.coef_
        d = -reg.intercept_
        a, b, c, d = resort_plane_par(a, b, c, d, idx)  # 重新排序
        # 求解点到平面的距离
        distance = np.abs(np.dot(self.points, [a, b, c]) + d) / np.sqrt(a ** 2 + b ** 2 + c ** 2)
        p_rms = np.sqrt(np.mean(np.square(distance)))  # 平面 RMS
        return a, b, c, d, p_rms

    def fit_line(self, points=None):
        """拟合直线"""
        if points is not None:
            self.points = points
        x, y1, y2, idx = principal_direction(self.points, method="line")  # type: ignore # 寻找主方向
        reg1 = LinearRegression().fit(x, y1)
        reg2 = LinearRegression().fit(x, y2)
        k = 1
        i = reg1.coef_[0]
        j = reg2.coef_[0]
        x0 = reg1.intercept_
        y0 = reg2.intercept_
        z0 = 0
        p0 = resort_line_par(x0, y0, z0, idx, False)
        dir0 = resort_line_par(i, j, k, idx)
        return p0, dir0

    def fit_circle(self):
        """ 拟合圆 """
        # 拟合平面
        a, b, c, d, p_rms = self.fit_plane()
        # 投影点
        pts = project_pts_on_plane_along_dir(self.points, [a, b, c, d])
        max_idx = np.argmax(np.array([abs(a), abs(b), abs(c)]))  # 找到 a,b,c 中最大值, 即为依据平面方程求解的值

        def get_center(i1, i2):
            """ 求解圆心, i1, i2 为 xy 或 yz 或 xz """
            if max_idx == 0:
                tmp_x = (-d - b * i1 - c * i2) / a
                tmp_pt = [tmp_x, i1, i2]
            elif max_idx == 1:
                tmp_x = (-d - a * i1 - c * i2) / b
                tmp_pt = [i1, tmp_x, i2]
            else:
                tmp_x = (-d - a * i1 - b * i2) / c
                tmp_pt = [i1, i2, tmp_x]
            return tmp_pt

        # 投影点拟合圆 x,y,z,r 使得 全局 rms_limit 最小
        def rms_err(input_arr):
            i1, i2, r0 = input_arr  # x,y,r 或者 x,z,r 或者 y,z,r
            distance = pts - np.array(get_center(i1, i2))
            distance = np.sqrt(np.sum(np.square(distance), axis=1))
            rms = np.sqrt(np.sum(np.square(distance - r0)) / len(distance))
            return rms

        # 全局最小, 优化求解
        init_x = [0., 0., 5.]
        solution = optimize.basinhopping(rms_err, init_x)
        x1, x2, r = solution.x
        c_rms = solution.fun
        return get_center(x1, x2), [a, b, c], r, p_rms, c_rms

    def fit_cylinder(self):
        """ 拟合圆柱, 轴线沿 Z 向 """
        axis_0 = np.array([0, 0, 1])  # 圆柱轴

        def cylinder_err(pars, pts):
            """ 点到圆心的距离与半径的差值的平方 """
            tmp_cx, tmp_cy, tmp_i, tmp_j, tmp_r = pars
            # 求解点到圆柱的距离
            dist = distance_pts_to_line3d(pts, [[tmp_cx, tmp_cy, 0], [tmp_i, tmp_j, np.sqrt(1 - tmp_i ** 2 - tmp_j ** 2)]])
            rms = (dist - tmp_r) ** 2  # 圆柱 RMS
            return rms
        
        par = optimize.leastsq(cylinder_err, [0, 0, 0, 0, 100], self.points, maxfev=500000)
        x0, y0, i0, j0, r0 = par[0]
        rms_err = cylinder_err([x0, y0, i0, j0, r0], self.points)
        rms_err = np.sqrt(np.sum(rms_err) / len(rms_err))
        return [x0, y0, 0], [i0, j0, np.sqrt(1 - i0 ** 2 - j0 ** 2)], r0, rms_err

    def fit_ball(self):
        """ 拟合球 """
        centroid = np.mean(self.points, axis=0)  # 质心坐标
        x0, y0, z0 = centroid

        def ball_err(pars, pts):
            """ 点到圆心的距离与半径的差值的平方 """
            x_ball, y_ball, z_ball = pars
            x1 = pts[:, 0]
            y1 = pts[:, 1]
            z1 = pts[:, 2]
            distance = ((x1 - x_ball) ** 2 + (y1 - y_ball) ** 2 + (z1 - z_ball) ** 2) ** 0.5
            r_ball = np.mean(distance)
            return (distance - r_ball) ** 2

        par = optimize.leastsq(ball_err, [x0, y0, z0], self.points, maxfev=500000)
        x0, y0, z0 = par[0]
        r_vec = self.points - np.array([[x0, y0, z0]])
        r_vec = r_vec * r_vec
        r_vec = np.sqrt(np.sum(r_vec, axis=1))
        r0 = np.mean(r_vec)
        rms_err = ball_err([x0, y0, z0], self.points)
        rms_err = np.sqrt(np.sum(rms_err) / len(rms_err))
        return [x0, y0, z0], r0, rms_err

    def fit_ball_with_alpha_gamma(self, standard_r):
        """
        依据传感器测量的偏斜球面点, 拟合速度相对于线激光传感器的偏斜摆角
        @param standard_r: 标准球的半径
        @return:
        """
        centroid = np.mean(self.points, axis=0)  # 质心坐标
        x0, y0, z0 = centroid

        def alpha_gamma_to_ijk(alpha, gamma):
            """
            alpha 和 gamma 转 ijk
            @param alpha: 移动轴与 XOZ 平面的夹角
            @param gamma: 移动轴与 XOY 平面的夹角在 YOZ 平面的投影
            @param alpha:
            @param gamma:
            @return:
            """
            i1 = np.cos(alpha)
            j1 = np.sin(alpha) * np.cos(gamma)
            k1 = np.sin(alpha) * np.sin(gamma)
            return i1, j1, k1

        def ball_r_rms(pars, pts):
            """ 点到圆心的距离与半径的差值的平方 """
            x_ball, y_ball, z_ball, alpha, gamma = pars
            n_x1 = pts[:, 0]
            n_y1 = pts[:, 1]
            n_z1 = pts[:, 2]
            i1, j1, k1 = alpha_gamma_to_ijk(alpha, gamma)
            x1 = n_x1 / i1
            y1 = n_y1 - x1 * j1
            z1 = n_z1 - x1 * k1
            distance = ((x1 - x_ball) ** 2 + (y1 - y_ball) ** 2 + (z1 - z_ball) ** 2) ** 0.5
            err = (distance - standard_r) ** 2
            return err

        out_par = [[x0, y0, z0], [1, 0, 0], 1e10]
        for _ in range(self.max_iter):
            alpha0, gamma0 = (np.random.random((2,)) - 0.5) / 0.5 * np.pi / 18  # 随机初值
            par = optimize.leastsq(ball_r_rms, [x0, y0, z0, alpha0, gamma0], self.points, maxfev=500000)
            x0, y0, z0, alpha0, gamma0 = par[0]
            rms = ball_r_rms([x0, y0, z0, alpha0, gamma0], self.points)
            rms = np.sqrt(np.sum(rms) / len(rms))
            if rms < out_par[2]:
                i0, j0, k0 = alpha_gamma_to_ijk(alpha0, gamma0)
                out_par = [x0, y0, z0], [i0, j0, k0], rms
            if rms < self.rms_limit:
                break
        return out_par

    def fit_multi_balls_with_alpha_gamma(self, standard_r_list):
        pts_list = self.points
        par_list = []
        for (pts, r) in zip(pts_list, standard_r_list):
            self.points = pts
            tmp_par = self.fit_ball_with_alpha_gamma(r)
            par_list.append(tmp_par)
        return par_list

    def fit_move_vector(self):
        """
        依据传感器测量的偏斜球面点, 拟合速度相对于线激光传感器的偏斜方向；此时的点集为列表
        @return:
        """
        # 遍历求解圆心
        center_list = []
        fitter_2d = FeatureFitting2d(None)
        for pts in self.points:
            fitter_2d.points = pts[:, 1:]
            tmp_x = np.mean(pts[:, 0])
            tmp_y, tmp_z, _, _ = fitter_2d.fit_circle_2d(ratio_remove=20)
            center_list.append([tmp_x, tmp_y, tmp_z])
        center_list = np.array(center_list)
        # 拟合直线
        self.points = center_list
        return self.fit_line()  # 质心, 法矢, 偏差
