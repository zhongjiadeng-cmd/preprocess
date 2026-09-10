import random
import matplotlib.pyplot as plt
from ortools.constraint_solver import routing_enums_pb2, pywrapcp
from scipy.spatial.distance import cdist
import numpy as np
from MachineFunc.LKH_TSP import solve_tsp_lkh


def generate_points(n=120, seed=42):
    """在 0~100 的方形区域内随机生成 n 个点"""
    random.seed(seed)
    points = [(random.uniform(0, 100), random.uniform(0, 100)) for _ in range(n)]
    return points


def solve_tsp_ortools(points, time_limit=15, scale_factor=100.0):
    """
    参数:
        points: [(x, y), ...] 坐标列表
        time_limit: 求解时间上限（秒）
        scale_factor: 距离缩放因子（默认 1000）
    返回:
        (tour, total_dist)
        tour: 最优路径上的点索引序列（包含起点闭合但不重复）
        total_dist: 路径总长度
    """
    n = len(points)
    # 预计算距离矩阵（整数化以避免浮点精度问题）
    dist_matrix = cdist(points, points, metric="euclidean") * scale_factor
    dist_matrix = dist_matrix.astype(int)

    # 创建路由模型
    manager = pywrapcp.RoutingIndexManager(n, 1, 0)  # 1 辆车，起点为索引 0
    routing = pywrapcp.RoutingModel(manager)

    def distance_callback(from_idx, to_idx):
        from_node = manager.IndexToNode(from_idx)
        to_node = manager.IndexToNode(to_idx)
        return dist_matrix[from_node, to_node]

    transit_idx = routing.RegisterTransitCallback(distance_callback)
    routing.SetArcCostEvaluatorOfAllVehicles(transit_idx)

    # 搜索参数配置
    search_params = pywrapcp.DefaultRoutingSearchParameters()
    search_params.first_solution_strategy = (
        routing_enums_pb2.FirstSolutionStrategy.CHRISTOFIDES
    )
    search_params.local_search_metaheuristic = (
        routing_enums_pb2.LocalSearchMetaheuristic.GUIDED_LOCAL_SEARCH
    )
    search_params.time_limit.seconds = time_limit
    search_params.log_search = False  # 输出搜索日志

    # 求解
    solution = routing.SolveWithParameters(search_params)

    if not solution:
        print("未找到可行解")
        return None, None

    # 提取路径
    idx = routing.Start(0)
    tour = []
    while not routing.IsEnd(idx):
        tour.append(manager.IndexToNode(idx))
        idx = solution.Value(routing.NextVar(idx))

    total_dist = solution.ObjectiveValue() / scale_factor  # 恢复真实距离
    return tour, total_dist


def plot_result(points, tour, total_dist):
    """绘制散点图 + 最短路径连线"""
    xs = [points[i][0] for i in tour] + [points[tour[0]][0]]
    ys = [points[i][1] for i in tour] + [points[tour[0]][1]]

    fig, ax = plt.subplots(figsize=(10, 8))
    ax.plot(xs, ys, "b-", linewidth=0.8, alpha=0.7, label="Shortest Path")
    ax.scatter(
        [p[0] for p in points],
        [p[1] for p in points],
        c="red",
        s=20,
        zorder=5,
        label="Points",
    )
    # 标记起点
    ax.scatter(
        points[tour[0]][0], points[tour[0]][1],
        c="green", s=80, marker="*", zorder=6, label="Start",
    )
    ax.set_title(f"TSP Shortest Path — {len(points)} Points (Total: {total_dist:.2f})", fontsize=14)
    ax.set_xlabel("X")
    ax.set_ylabel("Y")
    ax.legend(loc="upper right")
    ax.set_aspect("equal")
    plt.tight_layout()
    plt.show()


def get_distance_mat(points:np.ndarray, method:str = "euclidean"):
    """
    计算距离矩阵
    :param points: 点坐标, 形状为 (n, m)
    :param method: 距离计算方法, "euclidean" 或 "max" 或 "min
    :return: 距离矩阵, 形状为 (n, n)
    """
    if method == "euclidean":
        distance_mat = cdist(points, points, metric='euclidean')
    elif method == "max":
        distance_mat = cdist(points, points, metric='chebyshev')
    elif method == "min":
        distance_mat = cdist(points, points, metric='chebyshev')
        distance_mat = 1.0/(distance_mat+1.0)
    else:
        raise ValueError(f"未知距离计算方法: {method}")
    return distance_mat


def nearest_neighbor_baseline_distance(distance_mat:np.ndarray):
    """贪心最近邻作为对比基准"""
    n = len(distance_mat)
    unvisited = set(range(1, n))
    tour = [0]
    current = 0
    while unvisited:
        nxt = min(unvisited, key=lambda j: distance_mat[current, j])
        tour.append(nxt)
        unvisited.remove(nxt)
        current = nxt
    dist = sum(distance_mat[tour[i], tour[i-1]] for i in range(1, n))
    return tour, dist


if __name__ == "__main__":
    from datetime import datetime

    points = generate_points(n=2000, seed=42)


    # 贪心基准
    print("=" * 55)
    print("计算贪心最近邻基准...")
    start_time = datetime.now()
    distance_mat = get_distance_mat(points, "max")
    nn_tour, nn_dist = nearest_neighbor_baseline_distance(distance_mat)
    print(f"贪心最近邻路径总长: {nn_dist:.2f}")
    end_time = datetime.now()
    print(f"贪心最近邻耗耗时: {end_time - start_time}")
    plot_result(points, nn_tour, nn_dist)

    print("=" * 55)
    print("LKH 求解（低精度）")
    start_time = datetime.now()
    res  = solve_tsp_lkh(np.array(points), speed_level=2, default_method="MAX_2D")
    end_time = datetime.now()
    print(f"LKH 求解耗耗时: {end_time - start_time}")
    if res:
        tour, lkh_cost = res
        print(f"LKH 路径总长: {lkh_cost:.2f}")
        print(f"相比贪心最近邻，提升: {(nn_dist - lkh_cost) / nn_dist * 100:.1f}%")
        plot_result(points, tour, lkh_cost)

    print("=" * 55)
    print("LKH 求解（中精度）")
    start_time = datetime.now()
    res  = solve_tsp_lkh(np.array(points), speed_level=1, default_method="MAX_2D")
    end_time = datetime.now()
    print(f"LKH 求解耗耗时: {end_time - start_time}")
    if res:
        tour, lkh_cost = res
        print(f"LKH 路径总长: {lkh_cost:.2f}")
        print(f"相比贪心最近邻，提升: {(nn_dist - lkh_cost) / nn_dist * 100:.1f}%")
        plot_result(points, tour, lkh_cost)


    print("=" * 55)
    print("LKH 求解（低精度）-距离")
    start_time = datetime.now()
    distance_mat = get_distance_mat(points, "max")
    res  = solve_tsp_lkh(np.array(points), speed_level=2, distance_mat=distance_mat)
    end_time = datetime.now()
    print(f"LKH 求解耗耗时: {end_time - start_time}")
    if res:
        tour, lkh_cost = res
        print(f"LKH 路径总长: {lkh_cost:.2f}")
        print(f"相比贪心最近邻，提升: {(nn_dist - lkh_cost) / nn_dist * 100:.1f}%")
        plot_result(points, tour, lkh_cost)

    print("=" * 55)
    print("LKH 求解（中精度）-距离")
    start_time = datetime.now()
    distance_mat = get_distance_mat(points, "max")
    res = solve_tsp_lkh(np.array(points), speed_level=1, distance_mat=distance_mat)
    end_time = datetime.now()
    print(f"LKH 求解耗耗时: {end_time - start_time}")
    if res:
        tour, lkh_cost = res
        print(f"LKH 路径总长: {lkh_cost:.2f}")
        print(f"相比贪心最近邻，提升: {(nn_dist - lkh_cost) / nn_dist * 100:.1f}%")
        plot_result(points, tour, lkh_cost)