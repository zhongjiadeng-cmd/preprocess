import numpy as np
import json
from matplotlib import pyplot as plt

data_path = "axis_precision_cam_0.json"

data = json.load(open(data_path, "r"))
out_data_arr = data["out_data_arr"]
for itm in out_data_arr:
    delta = itm['delta_pts']
    delta = np.array(delta)
    # 分别绘制 x(z) 轴和 y 轴的误差,
    plt.plot(delta[:, 0], label="x(z)")
    plt.plot(delta[:, 1], label="y")
    plt.legend()
    plt.title("Delta Data(No affine)")
    plt.grid()
    plt.show()
    if "aff_meas_pts" in itm and "theo_pts" in itm:
        aff_meas_pts = itm['aff_meas_pts']
        theo_pts = itm['theo_pts']
        aff_meas_pts = np.array(aff_meas_pts)
        aff_meas_pts = aff_meas_pts.T
        theo_pts = np.array(theo_pts)
        delta_aff = aff_meas_pts - theo_pts
        print("delta x(z):", delta_aff[:, 0].max() - delta_aff[:, 0].min())
        print("delta y:", delta_aff[:, 1].max() - delta_aff[:, 1].min())
        plt.plot(delta_aff[:, 0], label="x(z)")
        plt.plot(delta_aff[:, 1], label="y")
        plt.legend()
        plt.title("Delta Data(Affine)")
        plt.grid()
        plt.show()


repeat_data = data["repeat_data"]
repeat_data = np.array(repeat_data)
plt.plot(repeat_data[:, 0], label="x(z)")
plt.plot(repeat_data[:, 1], label="y")
plt.legend()
plt.title("Repeat Data")
plt.grid()
plt.show()