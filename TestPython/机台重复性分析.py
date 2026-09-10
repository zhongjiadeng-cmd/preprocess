import json
from matplotlib import pyplot as plt
import numpy as np

data_path = 'cycle_test.json'
data = json.load(open(data_path, 'r'))
data = np.array(data)

# 筛选出首列小于0.5的数据和大于0.5的数据
data1 = data[data[:, 0] < 0.5]
data2 = data[data[:, 0] > 0.5]
data1 = data1[:, 1:]
data2 = data2[:, 1:]
# 按列去中心化
data1 = data1 - np.mean(data1, axis=0)
data2 = data2 - np.mean(data2, axis=0)


plt.plot(data1[:, 0], label='galvo3: z error', color='blue', linestyle='--')
plt.plot(-data1[:, 1], label='galvo3: y error', color='red', linestyle='--')
plt.plot(data2[:, 0], label='galvo1: z error', color='blue')
plt.plot(-data2[:, 1], label='galvo1: y error', color='red')
plt.legend()
plt.grid()
plt.xlabel('cycle')
plt.ylabel('error')
plt.show()
