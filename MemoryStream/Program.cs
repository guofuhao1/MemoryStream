using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Dicom;
using Dicom.Network;
using System.Diagnostics;
using MemoryStream;

namespace DicomMultiThreadUploader
{
    class Program
    {
        // 配置常量
        private const int MaxConcurrentThreads = 2; // 最大并发线程数
        private const int MemoryWarningThresholdMB = 850; // 内存警告阈值(MB)
        private const int MemoryCriticalThresholdMB = 1000; // 内存临界阈值(MB)
        private const int NormalSleepInterval = 30000; // 正常休眠间隔(ms)
        private const int HighMemorySleepInterval = 60000; // 高内存休眠间隔(ms)

        static async Task Main(string[] args)
        {
            Console.WriteLine("DICOM多线程推送服务启动...");

            while (true)
            {
                try
                {
                    // 监控当前进程内存使用情况
                    var memoryUsage = GetMemoryUsage();
                    Console.WriteLine($"内存状态 - 私有内存: {memoryUsage.PrivateMB} MB, 工作集: {memoryUsage.WorkingSetMB} MB");

                    // 内存过高时休眠更长时间
                    if (memoryUsage.PrivateMB > MemoryCriticalThresholdMB)
                    {
                        Console.WriteLine($"内存占用过高，休眠{HighMemorySleepInterval / 1000}秒...");
                        await Task.Delay(HighMemorySleepInterval);
                        continue;
                    }
                    else if (memoryUsage.PrivateMB > MemoryWarningThresholdMB)
                    {
                        // 内存接近临界值时触发垃圾回收
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }

                    // 获取PACS数据
                    var pacsData = sqlData.PACS(Sql.pacsconfig, Sql.SelectPacs("1"));
                    if (pacsData.Rows.Count == 0)
                    {
                        Console.WriteLine("没有获取到PACS数据");
                    }
                    else
                    {
                        Console.WriteLine($"从PACS获取到{pacsData.Rows.Count}条数据");
                        await ComparisonAndProcessAsync(pacsData);
                    }

                    // 处理本地待上传数据
                    var localData = sqlData.Local(Sql.locale, Sql.SelectUploadLocal("7"));
                    if (localData.Rows.Count > 0)
                    {
                        Console.WriteLine($"开始处理{localData.Rows.Count}个本地任务");
                        await ProcessLocalDataAsync(localData);
                    }

                    Console.WriteLine($"本轮处理完成，休眠{NormalSleepInterval / 1000}秒...");
                    await Task.Delay(NormalSleepInterval);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"主循环发生错误: {ex.Message}");
                    await Task.Delay(NormalSleepInterval);
                }
                finally
                {
                    // 确保每轮循环后释放资源
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }
        }

        private static (long PrivateMB, long WorkingSetMB) GetMemoryUsage()
        {
            using (var currentProcess = Process.GetCurrentProcess())
            {
                return (
                    currentProcess.PrivateMemorySize64 / 1024 / 1024,
                    currentProcess.WorkingSet64 / 1024 / 1024
                );
            }
        }

        private static async Task ComparisonAndProcessAsync(DataTable pacsData)
        {
            try
            {
                // 获取本地已有数据的STYUID集合
                var localData = sqlData.Local(Sql.locale, Sql.SelectLocal("7"));
                var existingLocalUrls = new HashSet<string>(
                    localData.AsEnumerable().Select(r => r["StyUid"].ToString()));

                // 找出需要新增的数据
                var newData = pacsData.AsEnumerable()
                    .Where(row => !existingLocalUrls.Contains(row["STUDIESINSTUID"].ToString()))
                    .ToList();

                if (newData.Any())
                {
                    Console.WriteLine($"发现{newData.Count}条新数据需要处理");

                    // 分批处理新数据(每批50条)
                    foreach (var batch in newData.Batch(50))
                    {
                        await ProcessBatchAsync(batch);
                    }
                }
            }
            finally
            {
                pacsData?.Dispose();
            }
        }

        private static async Task ProcessBatchAsync(IEnumerable<DataRow> batch)
        {
            var tasks = batch.Select(row => Task.Run(async () =>
            {
                try
                {
                    sqlData.Insert(Sql.locale, Sql.InsertUpdateDcm(row));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"处理数据{row["STUDIESINSTUID"]}时出错: {ex.Message}");
                }
            }));

            await Task.WhenAll(tasks);
        }

        private static async Task ProcessLocalDataAsync(DataTable localData)
        {
            try
            {
                var semaphore = new SemaphoreSlim(MaxConcurrentThreads);
                var tasks = new List<Task>();
                var exceptions = new ConcurrentQueue<Exception>();

                foreach (DataRow row in localData.Rows)
                {
                    await semaphore.WaitAsync();

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            string ftpUrl = row["FtpUrl"].ToString();
                            string styuid = row["StyUid"].ToString();

                            Console.WriteLine($"开始处理STYUID: {styuid}");
                            var fileUrls = await GetFtpFileListAsync(ftpUrl, "zl", "zlpacs");
                            
                            if (fileUrls.Any())
                            {
                                await UploadDicomFilesAsync(fileUrls, "zl", "zlpacs", styuid);
                                sqlData.Local(Sql.locale, Sql.UpdateUploadLocal(styuid));
                                Console.WriteLine($"成功处理STYUID: {styuid}, 文件数: {fileUrls.Count}");
                            }
                        }
                        catch (Exception ex)
                        {
                            exceptions.Enqueue(ex);
                            Console.WriteLine($"处理STYUID {row["StyUid"]}时出错: {ex.Message}");
                            sqlData.Insert(Sql.locale, Sql.ErrorUpdateUploadLocal(row["StyUid"].ToString()));
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks);

                if (exceptions.Any())
                {
                    Console.WriteLine($"本轮处理完成，共{exceptions.Count}个错误");
                }
            }
            finally
            {
                localData?.Dispose();
            }
        }

        private static async Task<List<string>> GetFtpFileListAsync(string ftpUrl, string username, string password)
        {
            var fileUrls = new List<string>();

            var request = (FtpWebRequest)WebRequest.Create(ftpUrl);
            request.Method = WebRequestMethods.Ftp.ListDirectory;
            request.Credentials = new NetworkCredential(username, password);

            using (var response = (FtpWebResponse)await request.GetResponseAsync())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream))
            {
                string fileName;
                while ((fileName = await reader.ReadLineAsync()) != null)
                {
                    fileUrls.Add(ftpUrl + fileName);
                }
            }

            return fileUrls;
        }

        private static async Task UploadDicomFilesAsync(List<string> fileUrls, string username, string password, string styuid)
        {
            // 每个STYUID使用单独的DICOM客户端
            var client = new Dicom.Network.Client.DicomClient("127.0.0.1", 5051, false, "JPAI", "JPAI");

            var semaphore = new SemaphoreSlim(MaxConcurrentThreads);
            var tasks = new List<Task>();
            var exceptions = new ConcurrentQueue<Exception>();

            // 初始批量大小
            int batchSize = 300;
            int processedCount = 0;

            while (processedCount < fileUrls.Count)
            {
                // 检查内存使用情况
                var memoryUsage = GetMemoryUsage();

                // 根据内存使用情况调整批量大小
                if (memoryUsage.PrivateMB > MemoryCriticalThresholdMB)
                {
                    batchSize = Math.Max(50, batchSize / 2); // 减半批量大小，最小50
                    Console.WriteLine($"内存过高({memoryUsage.PrivateMB}MB)，减少批量大小至{batchSize}");
                    await Task.Delay(5000); // 等待5秒让内存释放
                }
                else if (memoryUsage.PrivateMB > MemoryWarningThresholdMB)
                {
                    batchSize = Math.Max(100, (int)(batchSize * 0.8)); // 减少20%，最小100
                    Console.WriteLine($"内存警告({memoryUsage.PrivateMB}MB)，调整批量大小至{batchSize}");
                }
                else if (memoryUsage.PrivateMB < MemoryWarningThresholdMB / 2)
                {
                    batchSize = Math.Min(500, (int)(batchSize * 1.2)); // 增加20%，最大500
                }

                // 计算当前批次
                int currentBatchSize = Math.Min(batchSize, fileUrls.Count - processedCount);
                var currentBatch = fileUrls.Skip(processedCount).Take(currentBatchSize);

                // 处理当前批次
                foreach (var url in currentBatch)
                {
                    await semaphore.WaitAsync();

                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var request = (FtpWebRequest)WebRequest.Create(url);
                            request.Method = WebRequestMethods.Ftp.DownloadFile;
                            request.Credentials = new NetworkCredential(username, password);

                            using (var response = (FtpWebResponse)await request.GetResponseAsync())
                            using (var stream = response.GetResponseStream())                        
                            {
                                var memoryStream = new System.IO.MemoryStream();
                                await stream.CopyToAsync(memoryStream);
                                memoryStream.Position = 0;

                                var dicomFile = await DicomFile.OpenAsync(memoryStream);
                                var dicomRequest = new DicomCStoreRequest(dicomFile);
                                await client.AddRequestAsync(dicomRequest);
                            }
                        }
                        catch (Exception ex)
                        {
                            exceptions.Enqueue(ex);
                            Console.WriteLine($"处理文件{url}时出错: {ex.Message}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));

                    processedCount++;
                }

                // 等待当前批次完成
                await Task.WhenAll(tasks);
                tasks.Clear();

                // 发送当前批次的DICOM请求
                try
                {
                    Console.WriteLine($"发送批次: {processedCount}/{fileUrls.Count} (批量大小: {currentBatchSize})");
                    await client.SendAsync();

                    // 发送后强制GC释放内存
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"发送DICOM文件时出错: {ex.Message}");
                    throw;
                }

                // 显示进度
                Console.WriteLine($"已处理: {processedCount}/{fileUrls.Count} (内存使用: {memoryUsage.PrivateMB}MB)");
            }

            if (exceptions.Any())
            {
                Console.WriteLine($"STYUID {styuid}有{exceptions.Count}个文件处理失败");
            }
        }
        //private static async Task UploadDicomFilesAsync(List<string> fileUrls, string username, string password, string styuid)
        //{
        //    // 每个STYUID使用单独的DICOM客户端
        //    var client = new Dicom.Network.Client.DicomClient("127.0.0.1", 5051, false, "JPAI", "JPAI");

        //        var semaphore = new SemaphoreSlim(MaxConcurrentThreads);
        //        var tasks = new List<Task>();
        //        var exceptions = new ConcurrentQueue<Exception>();
        //        foreach (var url in fileUrls)
        //        {
        //            await semaphore.WaitAsync();


        //            tasks.Add(Task.Run(async () =>
        //            {
        //                try
        //                {
        //                       var request = (FtpWebRequest)WebRequest.Create(url);

        //                        request.Method = WebRequestMethods.Ftp.DownloadFile;
        //                        request.Credentials = new NetworkCredential(username, password);

        //                        using (var response = (FtpWebResponse)await request.GetResponseAsync())
        //                        using (var stream = response.GetResponseStream())
        //                        {
        //                        var memoryStream = new System.IO.MemoryStream();
        //                            await stream.CopyToAsync(memoryStream);
        //                            memoryStream.Position = 0;

        //                            var dicomFile = await DicomFile.OpenAsync(memoryStream);
        //                            var dicomRequest = new DicomCStoreRequest(dicomFile);

        //                            await client.AddRequestAsync(dicomRequest);

        //                        //if (fileUrls.IndexOf(url) % 300 == 0)
        //                        //{
        //                        //    Console.WriteLine($"打包序列{styuid} 300个文件");
        //                        //    await client.SendAsync();
        //                        //    memoryStream.Dispose();
        //                        //}
        //                    }

        //                }
        //                catch (Exception ex)
        //                {
        //                    exceptions.Enqueue(ex);
        //                    Console.WriteLine($"处理文件{url}时出错: {ex.Message}");
        //                }
        //                finally
        //                {
        //                    semaphore.Release();
        //                }
        //            }));
        //        }

        //        await Task.WhenAll(tasks);

        //        if (exceptions.Any())
        //        {
        //            Console.WriteLine($"STYUID {styuid}有{exceptions.Count}个文件处理失败");
        //        }

        //        // 所有文件添加完成后统一发送
        //        try
        //        {
        //            await client.SendAsync();
        //        }
        //        catch (Exception ex)
        //        {
        //            Console.WriteLine($"发送DICOM文件时出错: {ex.Message}");
        //            throw;
        //        }

        //}
    }

    // 扩展方法: 集合分批
    public static class EnumerableExtensions
    {
        public static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> source, int size)
        {
            T[] bucket = null;
            var count = 0;

            foreach (var item in source)
            {
                if (bucket == null)
                    bucket = new T[size];

                bucket[count++] = item;
                if (count != size)
                    continue;

                yield return bucket;

                bucket = null;
                count = 0;
            }

            if (bucket != null && count > 0)
                yield return bucket.Take(count);
        }
    }
}