import numpy as np
import json
from matplotlib import pyplot as plt

# sh_path = r"z-width.json"
sh_path = "E:/0-yxm/EVision/APP/Calibration5Axis/x64/Debug/cache/z-sharpness.json"
sh_data = json.load(open(sh_path, "r"))
sh_data = np.array(sh_data)

x = sh_data[0, :]
y = sh_data[1, :]
y = (y - y.min()) / (y.max() - y.min())
plt.plot(x, y, marker="o")

plt.title("Sharpness Curve")
# xy等比例
plt.grid()
plt.show()


