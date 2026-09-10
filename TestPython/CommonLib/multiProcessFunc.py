from multiprocessing import Pool
import math


def func_by_pool(func, input_args, split_input_idx, num_processes=10):
   """
   通过进程池来并行执行函数, 提升运行效率
   :param func: 待执行的函数
   :param input_args: 输入参数列表
   :param split_input_idx: 需要拆分的参数的索引
   :param num_processes: 进程数
   """
   all_count = len(input_args[split_input_idx[0]])
   each_count = math.ceil(all_count / num_processes)
   pool = Pool(processes=num_processes)
   results = []
   for i in range(num_processes):
       tmp_args = []
       for j in range(len(input_args)):
           start_idx = i * each_count
           end_idx = (i + 1) * each_count if (i + 1) * each_count < all_count else all_count
           if j in split_input_idx:
               tmp_args.append(input_args[j][start_idx:end_idx])
           else:
               tmp_args.append(input_args[j])
       results.append(pool.apply_async(func, args=tuple(tmp_args)))
   pool.close()
   pool.join()
   output = []
   for res in results:
       output += res.get()
   return output
#
# from concurrent.futures import ThreadPoolExecutor
# import math
#
# def func_by_pool(func, input_args, split_input_idx, num_workers=10):
#     """
#     通过线程池来并行执行函数, 提升运行效率
#     :param func: 待执行的函数
#     :param input_args: 输入参数列表
#     :param split_input_idx: 需要拆分的参数的索引
#     :param num_workers: 工作线程数
#     """
#     all_count = len(input_args[split_input_idx[0]])
#     each_count = math.ceil(all_count / num_workers)
#     results = []
#
#     with ThreadPoolExecutor(max_workers=num_workers) as executor:
#         # 提交任务到线程池
#         future_tasks = []
#         for i in range(num_workers):
#             tmp_args = []
#             for j in range(len(input_args)):
#                 start_idx = i * each_count
#                 end_idx = (i + 1) * each_count if (i + 1) * each_count < all_count else all_count
#                 if j in split_input_idx:
#                     tmp_args.append(input_args[j][start_idx:end_idx])
#                 else:
#                     tmp_args.append(input_args[j])
#             future_tasks.append(executor.submit(func, *tmp_args))
#
#         # 获取所有任务结果
#         for future in future_tasks:
#             results += future.result()
#
#     return results