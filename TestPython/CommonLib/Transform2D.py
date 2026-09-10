import numpy as np


def trans_matrix_2d(point=np.array([0, 0]), theta=0., rad_mod=True):
    """ 2d 齐次矩阵，先旋转再平移 """
    if not rad_mod:
        theta = np.pi / 180 * theta
    matrix = np.array([[np.cos(theta), -np.sin(theta), point[0]],
                       [np.sin(theta), np.cos(theta), point[1]],
                       [0, 0, 1]])
    return matrix


def rotate_matrix_2d(point=np.array([0, 0]), theta=0., rad_mod=True):
    """ 2d 旋转矩阵, 绕任意点 point """
    return np.array([
        [np.cos(theta), -np.sin(theta), -point[0] * np.cos(theta) + point[1] * np.sin(theta) + point[0]],
        [np.sin(theta), np.cos(theta), -point[0] * np.sin(theta) - point[1] * np.cos(theta) + point[1]],
        [0, 0, 1]
    ])


def get_transformed_pts_2d(matrix, pts_2d):
    """ 2D 点集变换 """
    pts_2d = np.hstack((pts_2d, np.ones((len(pts_2d), 1))))
    pts_2d = pts_2d.T
    out_pts = np.dot(matrix, pts_2d)
    out_pts = out_pts[0:2, :]
    out_pts = out_pts.T
    return out_pts


def get_transformed_vec_2d(matrix, vec_2d):
    """ 2D 向量变换 """
    org_pt = np.array([0, 0]).reshape((-1, 2))
    vec_2d = vec_2d.reshape((-1, 2))
    org_pt = np.hstack((org_pt, vec_2d))
    new_pt = get_transformed_pts_2d(matrix, org_pt)
    out_vec = new_pt[0, 2:] - new_pt[0, 0:2]
    return out_vec
