import math
import subprocess
from pathlib import Path
from typing import List, Tuple
import numpy as np
import os
import shutil


def ensure_ramdisk(drive="R:", size_mb=1024):
    """确保内存盘存在，不存在则创建"""
    if not os.path.exists(drive):
        subprocess.run(
            ["imdisk", "-a", "-s", f"{size_mb}M", "-m", drive,
            "-p", '/fs:NTFS /v:RAMDisk /q /y'],
            shell=True, check=True
        )


def write_tsplib_file(
        filepath: Path,
        name: str,
        points: np.ndarray,
        comment: str = "",
        distance_mat: np.ndarray = None,
        default_method: str = "EUC_2D",
) -> None:
    """
    将二维点集写入 TSPLIB 标准格式的 .tsp 文件。

    使用 EUC_2D 类型，TSPLIB 库定义的距离函数为整数欧几里得距离：
        d = int(sqrt((dx)^2 + (dy)^2) + 0.5)
    这与 LKH 默认行为一致。

    参数
    ----
    filepath : Path
        输出文件路径（建议 .tsp 后缀）。
    name : str
        问题名称。
    points : np.ndarray, shape (n, 2)
        点坐标数组。
    comment : str
        注释文本。
    default_method : str
        默认距离计算方法，"EUC_2D"(欧式距离) 或 "MAX_2D"(切比雪夫距离)。
    """
    if distance_mat is None:
        n = len(points)
        lines = [
            f"NAME : {name}",
            f"COMMENT : {comment}" if comment else f"COMMENT : {n} points",
            "TYPE : TSP",
            f"DIMENSION : {n}",
            f"EDGE_WEIGHT_TYPE : {default_method}",
            "NODE_COORD_SECTION",
        ]
        for i, (x, y) in enumerate(points, start=1):
            # TSPLIB 要求坐标可以是整数或浮点数；LKH 均支持
            lines.append(f"{i} {x:.6f} {y:.6f}")
    else:
        n = len(points)
        lines = [
            f"NAME : {name}",
            f"COMMENT : {comment}" if comment else f"COMMENT : {n}*{n} distance matrix",
            "TYPE : TSP",
            f"DIMENSION : {n}",
            "EDGE_WEIGHT_TYPE : EXPLICIT",
            "EDGE_WEIGHT_FORMAT : UPPER_ROW",
            "EDGE_WEIGHT_SECTION",
        ]
        distance_mat = distance_mat.tolist()
        for i in range(n):
            lines.append(" ".join(f"{distance_mat[i][j]:.6f}" for j in range(i + 1, n)))
    lines.append("EOF")

    with open(filepath, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


def write_par_file(
        filepath: Path,
        problem_file: Path,
        tour_file: Path,
        params: dict
) -> None:
    """
    写入 LKH 参数文件。

    参数
    ----
    filepath : Path
        .par 文件路径。
    problem_file : Path
        .tsp 问题文件的绝对路径。
    tour_file : Path
        输出 tour 文件的路径。
    params : dict
        LKH 参数字典。
    """
    lines = [
        f"PROBLEM_FILE = {problem_file.as_posix()}",
        f"TOUR_FILE = {tour_file.as_posix()}",
    ]
    for key, value in params.items():
        if isinstance(value, str):
            lines.append(f"{key} = {value}")
        elif isinstance(value, bool):
            lines.append(f"{key} = {'YES' if value else 'NO'}")
        else:
            lines.append(f"{key} = {value}")

    with open(filepath, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


def run_lkh(lkh_exe: str, par_file: Path, timeout: int = 300) -> subprocess.CompletedProcess:
    """
    通过子进程调用 LKH 求解器。

    参数
    ----
    lkh_exe : str
        LKH 可执行文件路径。
    par_file : Path
        .par 参数文件路径。
    timeout : int
        超时时间（秒）。

    返回
    ----
    subprocess.CompletedProcess
        子进程结果对象。

    异常
    ----
    FileNotFoundError
        LKH.exe 未找到。
    subprocess.TimeoutExpired
        求解超时。
    RuntimeError
        LKH 返回非零退出码。
    """
    if not Path(lkh_exe).exists():
        raise FileNotFoundError(
            f"LKH 可执行文件未找到: {lkh_exe}\n"
            f"请从 http://akira.ruc.dk/~keld/research/LKH-3/ 下载并编译/放置 LKH.exe"
        )

    cmd = [lkh_exe, str(par_file)]
    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=timeout,
        )
    except subprocess.TimeoutExpired:
        print("[✗] LKH 求解超时！请增大 TIME_LIMIT 或减少点数。")
        raise

    # 打印 LKH 输出（方便查看求解日志）
    if result.stderr:
        print("[LKH STDERR]:", result.stderr)

    if result.returncode != 0:
        raise RuntimeError(
            f"LKH 退出码非零 ({result.returncode})，请检查 .tsp / .par 文件格式。"
        )

    return result



def parse_tour_file(tour_file: Path) -> Tuple[List[int], str, float]:
    """
    解析 LKH 输出的 .tour 文件。

    返回
    ----
    tour : List[int]
        访问序列（1-based 节点编号），不包含起始点重复。
    name : str
        Tour 名称。
    cost : float
        路径总长度。
    """
    if not tour_file.exists():
        raise FileNotFoundError(f"Tour 文件未生成: {tour_file}，请检查 LKH 是否正常完成。")

    with open(tour_file, "r", encoding="utf-8") as f:
        content = f.read()

    # 提取路径长度
    cost = None
    for line in content.splitlines():
        if line.startswith("COMMENT : Length ="):
            cost = float(line.split("=")[-1].strip())
            break
        if line.startswith("COMMENT : Cost ="):
            cost = float(line.split("=")[-1].strip())
            break

    # 提取名称
    name = ""
    for line in content.splitlines():
        if line.startswith("NAME"):
            name = line.split(":")[-1].strip()
            break

    # 提取 TOUR_SECTION
    in_section = False
    tour = []
    for line in content.splitlines():
        line = line.strip()
        if line == "TOUR_SECTION":
            in_section = True
            continue
        if in_section:
            if line == "-1" or line == "EOF":
                break
            if line:
                tour.append(int(line))

    if not tour:
        raise ValueError(f"Tour 文件中未找到有效路径序列: {tour_file}")
    return tour, name, cost


def compute_tour_distance(points: np.ndarray, tour: List[int]) -> float:
    """
    根据坐标点和访问序列计算完整回路距离。

    使用 TSPLIB EUC_2D 规范：d = int(sqrt(dx² + dy²) + 0.5)

    参数
    ----
    points : np.ndarray, shape (n, 2)
        点坐标（0-based 索引）。
    tour : List[int]
        访问序列（1-based 节点编号）。

    返回
    ----
    float
        总回路距离。
    """
    total = 0.0
    n = len(tour)

    for i in range(n):
        idx_a = tour[i] - 1  # 转为 0-based
        idx_b = tour[(i + 1) % n] - 1
        dx = points[idx_a, 0] - points[idx_b, 0]
        dy = points[idx_a, 1] - points[idx_b, 1]
        total += math.sqrt(dx * dx + dy * dy)

    return total


def solve_tsp_lkh(points: np.ndarray, speed_level=1, distance_mat=None, default_method="EUC_2D",
                  lkh_exe = "LKH-3.exe", start_idx=-1, filename="temp"):
    """
    使用 LKH 求解 TSP 问题。

    参数
    ----
    points : np.ndarray, shape (n, 2)
        点坐标（0-based 索引）。
    speed_level : int
        求解速度等级（0=慢速, 1=快速, 2=超快）
    distance_mat : np.ndarray, shape (n, n)
        距离矩阵, 非空则使用该矩阵，否则根据 points 计算。
    default_method : str
        默认距离计算方法，"EUC_2D"(欧式距离) 或 "MAX_2D"(切比雪雪夫距离)
    返回
    ----
    tour : List[int]
        访问序列（1-based 节点编号），不包含起始点重复。
    cost : float
        路径总长度。
    """
    # 工作目录
    if distance_mat is not None:
        ensure_ramdisk()
        PARAM_DIR = Path(f"R:/Data/lkh_params/{filename}")
    else:
        PARAM_DIR = Path(os.getcwd() + f"/{filename}")
    # 创建工作目录
    PARAM_DIR.mkdir(parents=True, exist_ok=True)
    # 文件名（不含后缀）
    PROBLEM_NAME = "temp"
    # LKH 求解参数
    LKH_PARAMS = {
        "RUNS": 3,  # 独立运行次数，取最优
        "MAX_TRIALS": 300,  # 每次运行最大尝试次数
        "MOVE_TYPE": 3,  # 3-opt 移动
        "PATCHING_C": 3,
        "PATCHING_A": 2,
        "CANDIDATE_SET_TYPE": "POPMUSIC",  # 大规模实例推荐 POPMUSIC
        "POPMUSIC_SAMPLE_SIZE": 10,
        "POPMUSIC_SOLUTIONS": 50,
        "POPMUSIC_MAX_NEIGHBORS": 5,
        "SEED": 42,
        "TRACE_LEVEL": 1,  # 输出详细程度（0=静默, 1=正常, 2=更多）
        "TIME_LIMIT": 120,  # 总时间上限（秒），可按需调整
    }
    LKH_PARAMS_FAST = {
        "RUNS": 1,  # 只跑一次
        "MAX_TRIALS": 50,  # 限制尝试次数
        "MOVE_TYPE": 2,  # 降到 2-opt
        "MAX_CANDIDATES": 3,  # 减少候选边
        "CANDIDATE_SET_TYPE": "QUADRANT",  # 象限法，最快
        "SUBGRADIENT": "NO",  # 跳过下界计算
        "BACKTRACKING": "NO",  # 关闭回溯
        "SEED": 42,
        "TRACE_LEVEL": 0,  # 静默，减少 I/O 开销
        "TIME_LIMIT": 30,
        "NONSEQUENTIAL_MOVE_TYPE": 4,
    }
    LKH_PARAMS_ULTRA_FAST = {
        # ── 核心加速 ──
        "RUNS": 1,
        "MAX_TRIALS": 50,
        "MOVE_TYPE": 2,
        "MAX_CANDIDATES": 3,
        "CANDIDATE_SET_TYPE": "QUADRANT",
        # ── 关闭可选搜索机制 ──
        "SUBGRADIENT": "NO",
        "BACKTRACKING": "NO",
        "PATCHING_A": 1,
        "PATCHING_C": 1,
        "SUBSEQUENT_PATCHING": "NO",
        "SUBSEQUENT_MOVE_TYPE": 0,
        "GAIN23": "NO",
        # ── 不可关闭的核心机制（设最小值）──
        "NONSEQUENTIAL_MOVE_TYPE": 4,
        "KICKS": 1,

        # ── 初始解 ──
        "INITIAL_TOUR_ALGORITHM": "NEAREST-NEIGHBOR",
        "INITIAL_TOUR_FRACTION": 1.0,

        # ── 约束 ──
        "RESTRICTED_SEARCH": "YES",
        "MAX_SWAPS": 500,
        "SEED": 42,
        "TRACE_LEVEL": 0,
        "TIME_LIMIT": 10,
    }
    params_list = [LKH_PARAMS, LKH_PARAMS_FAST, LKH_PARAMS_ULTRA_FAST]
    params = params_list[speed_level]
    if distance_mat is not None:
        params["CANDIDATE_SET_TYPE"] = "POPMUSIC"
    # 文件写入
    tsp_path = PARAM_DIR / f"{PROBLEM_NAME}.tsp"
    write_tsplib_file(
        filepath=tsp_path,
        name=PROBLEM_NAME,
        points=points,
        comment=f"{points.shape[0]} points for LKH",
        distance_mat=distance_mat,
        default_method=default_method,
    )
    par_path = PARAM_DIR / f"{PROBLEM_NAME}.par"
    tour_path = PARAM_DIR / f"{PROBLEM_NAME}.tour"
    write_par_file(
        filepath=par_path,
        problem_file=tsp_path.resolve(),
        tour_file=tour_path.resolve(),
        params=params,
    )
    # 求解 TSP 问题
    try:
        run_lkh(lkh_exe, par_path, timeout=params.get("TIME_LIMIT", 300) + 30)
    except FileNotFoundError as e:
        print(f"\n[✗] {e}")
        print("\n提示：请确保 LKH.exe 位于脚本同级目录，或修改 LKH_EXE 变量。")
        print("下载地址：http://akira.ruc.dk/~keld/research/LKH-3/")
        return None
    except subprocess.TimeoutExpired:
        print("\n[✗] 求解超时。请尝试增大 TIME_LIMIT 或减少 RUNS。")
        return None
    except RuntimeError as e:
        print(f"\n[✗] {e}")
        return None
    # 解析结果文件
    if not tour_path.exists():
        print(f"[✗] Tour 文件未找到: {tour_path}")
        # 尝试在当前目录查找
        alt_tour = Path(f"{PROBLEM_NAME}.tour")
        if alt_tour.exists():
            tour_path = alt_tour
            print(f"      使用备选路径: {tour_path}")
        else:
            print("      请检查 LKH 输出日志中的错误信息。")
            return None
    tour, _, lkh_cost = parse_tour_file(tour_path)
    tour = [int(i - 1) for i in tour]
    # 固定起始点为 start_idx
    if 0 <= start_idx < len(tour):
        idx = tour.index(start_idx)
        tour = tour[idx:] + tour[:idx]
    # 删除所有文件
    shutil.rmtree(PARAM_DIR)
    return tour, lkh_cost
