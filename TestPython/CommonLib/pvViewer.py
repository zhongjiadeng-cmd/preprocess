import pyvista as pv
import numpy as np
from CommonLib.Transform3D import get_transformed_pts, get_transformed_vec
import open3d as o3d

def mesh_o3d_to_pv(mesh_o3d):
    """
    将 open3d.geometry.TriangleMesh 或 open3d.t.geometry.TriangleMesh 对象转换为 pyvista.PolyData 对象
    """
    if isinstance(mesh_o3d, o3d.geometry.TriangleMesh):
        triangles = np.asarray(mesh_o3d.triangles)
        vertex = np.asarray(mesh_o3d.vertices)
    elif isinstance(mesh_o3d, o3d.t.geometry.TriangleMesh):
        vertex = np.asarray(mesh_o3d.vertex.positions.numpy())
        triangles = np.asarray(mesh_o3d.triangle.indices.numpy())
    else:
        raise TypeError("Input mesh should be open3d.geometry.TriangleMesh or open3d.t.geometry.TriangleMesh")
    triangles = np.hstack((np.ones((triangles.shape[0], 1)) * 3, triangles))
    triangles = triangles.flatten().astype(np.int64)
    return pv.PolyData(vertex, triangles)

def draw_o3d_mesh_pv(pl, mesh, color=[1.0, 0.8, 0.6], opacity=1.0):
    """
    在 pyvista.Plotter 对象中绘制 open3d.geometry.TriangleMesh 对象
    """
    mesh = mesh_o3d_to_pv(mesh)
    pl.add_mesh(mesh, color=color, opacity=opacity)

def draw_pv_axes(pl, size=1.0, trans_mat=np.eye(4)):
    """
    在 pyvista.Plotter 对象中绘制坐标轴
    :param pl: pyvista.Plotter, plotter object
    :param size: float, size of the axes
    :param trans_mat: numpy array, transformation matrix
    :return: pyvista.PolyData, axes as a PolyData object
    """
    # 1. 点和方向变换
    org_pt = np.array([0.0,0.0,0.0])
    xyz_dir = np.array([[1.0,0.0,0.0],
                        [0.0,1.0,0.0],
                        [0.0,0.0,1.0]])
    org_pt = get_transformed_pts(trans_mat, org_pt)
    xyz_dir = get_transformed_vec(trans_mat, xyz_dir)
    # 2. 绘制坐标轴
    x_arrow = pv.Arrow(start=org_pt, direction=xyz_dir[0], scale=size)
    y_arrow = pv.Arrow(start=org_pt, direction=xyz_dir[1], scale=size)
    z_arrow = pv.Arrow(start=org_pt, direction=xyz_dir[2], scale=size)
    pl.add_mesh(x_arrow, color='r')
    pl.add_mesh(y_arrow, color='g')
    pl.add_mesh(z_arrow, color='b')

def draw_pv_segments(pl: pv.Plotter, segments, color=[1.0, 0.0, 0.0]):
    """
    绘制线段
    :param pl: pyvista.Plotter, plotter object
    :param segments: numpy array, nx6, [[x1, y1, z1, x2, y2, z2], ...] 或 nx4, [[x1, y1, x2, y2], ...]
    :param color: str, color of the segments
    """
    segments = np.array(segments)
    if (len(segments)) == 0:
        return
    if segments.shape[1] == 4:
        new_segments = np.zeros((segments.shape[0], 6))
        new_segments[:, :2] = segments[:, :2]
        new_segments[:, 3:5] = segments[:, 2:]
        segments = new_segments
    seg_pts = np.vstack((segments[:, :3], segments[:, 3:]))
    lines = np.hstack((np.ones((segments.shape[0], 1)) * 2, 
                       np.array(range(segments.shape[0])).reshape(-1, 1), 
                       np.array(range(segments.shape[0])).reshape(-1, 1) + segments.shape[0]))
    lines = lines.flatten().astype(np.int64)
    pl.add_mesh(pv.PolyData(seg_pts, lines=lines), color=color)

def draw_pv_points(pl, points, color=[0.0, 1.0, 0.0], size=1.0):
    """
    绘制点云
    :param pl: pyvista.Plotter, plotter object
    :param points: numpy array, nx3, [[x1, y1, z1], ...]
    :param color: str, color of the points
    :param size: float, size of the point s
    """
    points = np.array(points)
    # pl.add_mesh(pv.PolyData(points), point_size=size)
    pl.add_mesh(pv.PolyData(points), color=color, point_size=size)
    
def draw_pv_polylines(pl, polylines, color=[1.0, 0.0, 0.0]):
    """
    绘制多边形线
    :param pl: pyvista.Plotter, plotter object
    :param polylines: numpy array, [[p0, p1, p2, ...], ...], p_i = [x, y, z]
    """
    if len(polylines) == 0:
        return
    if not isinstance(polylines[0], np.ndarray):
        polylines = [np.array(polyline) for polyline in polylines]
    if (polylines[0].shape[1] == 2):  # 2D 转 3D
        polylines = [np.hstack((polyline, np.zeros((polyline.shape[0], 1)))) for polyline in polylines]
    pts_count = [len(polyline) for polyline in polylines]
    all_pts = [pt for polyline in polylines for pt in polyline]
    lines = []
    tmp_idx = 0
    for count in pts_count:
        lines.append(count)
        lines += list(range(tmp_idx, tmp_idx + count))
        tmp_idx += count
    lines = np.array(lines).reshape(-1, 1)
    pl.add_mesh(pv.PolyData(all_pts, lines=lines), color=color)

    