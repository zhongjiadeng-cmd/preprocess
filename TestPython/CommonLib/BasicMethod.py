import sympy


def get_sympy_result():
    """
    解符号方程参考
    :return:
    """
    theta_b, theta_c = sympy.symbols('theta_b, theta_c')
    i, j, k = sympy.symbols('i, j, k')  # 旋转后的法向; 旋转前为 (0,0,1)
    norm_org = sympy.Matrix([[0], [0], [1]])  # 旋转前的法向
    norm_after = sympy.Matrix([[i], [j], [k]])  # 旋转后的法向
    # 绕 B 轴旋转的 matrix 和法向
    R_b = sympy.Matrix([[1, 0, 0], [0, sympy.cos(theta_b), -sympy.sin(theta_b)], [0, sympy.sin(theta_b), sympy.cos(theta_b)]])
    norm_b = R_b * norm_org
    # 绕 C 轴旋转的 matrix
    R_c = sympy.Matrix([[sympy.cos(theta_c), -sympy.sin(theta_c), 0], [sympy.sin(theta_c), sympy.cos(theta_c), 0], [0, 0, 1]])
    norm_c = R_c * norm_b
    # 计算结果
    result = sympy.solve([norm_after[0] - norm_c[0], norm_after[1] - norm_c[1], norm_after[2] - norm_c[2]], [theta_b, theta_c])
    return result
