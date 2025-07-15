using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Data;
using MemoryStream;
using Dicom.Network;
using Dicom;
using System.Threading;
using System.Diagnostics;
namespace MemoryStream1
{
    class Program
    {
        [Obsolete]
        static void Main(string[] args)
        {
            while (true)
            {
                Process currentProcess = Process.GetCurrentProcess();

                // 获取私有内存（占用的私有字节数）
                
                long privateMemorySize = currentProcess.PrivateMemorySize64;

                // 获取工作集（进程当前使用的物理内存）
                long workingSet = currentProcess.WorkingSet64;

                Console.WriteLine($"私有内存：{privateMemorySize / 1024 / 1024} MB");
                Console.WriteLine($"工作集（物理内存使用）：{workingSet / 1024 / 1024} MB");
                if ((privateMemorySize / 1024 / 1024)>500)
                {
                    Thread.Sleep(60000);
                    Console.WriteLine("内存占用过高，休眠60秒");
                    continue; // 跳过本次循环，重新开始
                }
                DataTable pacs = new DataTable();//pacs数据表
                DataTable BenDi = new DataTable();//本地待上传数据表
                pacs = sqlData.PACS(Sql.pacsconfig, Sql.SelectPacs("1"));//查询pacs数据
                if (pacs.Rows.Count == 0)
                {
                    Console.WriteLine("没有数据");

                }
                else
                {
                    Console.WriteLine("PACS获取：" + pacs.Rows.Count);
                    //过滤
                    Comparison(pacs);
                }
                BenDi = sqlData.Local(Sql.locale, Sql.SelectUploadLocal("7"));//查询本地待上传数据


                for (int i = 0; i < BenDi.Rows.Count; i++)
                {
                    string ftpUrl = BenDi.Rows[i]["FtpUrl"].ToString();
                    FtpWebRequest request = (FtpWebRequest)WebRequest.Create(ftpUrl);
                    request.Method = WebRequestMethods.Ftp.ListDirectory;

                    // 认证  
                    request.Credentials = new NetworkCredential("zl", "zlpacs");
                    try
                    {
                        using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
                        using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                        {
                            string fileName;
                            List<string> FTPUILs = new List<string>();
                            while ((fileName = reader.ReadLine()) != null)
                            {
                                string fileUrl = ftpUrl + fileName;
                                FTPUILs.Add(fileUrl);
                            }
                            //下载文件到内存流，并推送到Aibox
                            NewMethod(FTPUILs, "zl", "zlpacs");
                        }
                        sqlData.Local(Sql.locale, Sql.UpdateUploadLocal(BenDi.Rows[i]["StyUid"].ToString()));//更新本地数据
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }
                Console.WriteLine($"本轮结束休眠30秒");
                Thread.Sleep(30000);
            }   
        }

        private static void NewMethod(List<string> FtpUrls, string FtpName, string FtpPwd)
        {
            //var client = new DicomClient();
            
            var client = new Dicom.Network.Client.DicomClient("127.0.0.1", 5051, false, "JPAI", "JPAI");         
            foreach (var url in FtpUrls)
            {
                try
                {
                    var request = (FtpWebRequest)WebRequest.Create(url);
                    request.Method = WebRequestMethods.Ftp.DownloadFile;
                    request.Credentials = new NetworkCredential(FtpName, FtpPwd);

                    using (var response = (FtpWebResponse)request.GetResponse())
                    using (var stream = response.GetResponseStream())
                    {
                        var memoryStream = new System.IO.MemoryStream();
                        stream.CopyTo(memoryStream);
                        memoryStream.Position = 0; // 重置流的位置
                        var dicomFile = DicomFile.Open(memoryStream);
                        var dicomRequest = new DicomCStoreRequest(dicomFile);
                        client.AddRequestAsync(dicomRequest).Wait(); // 等待请求完成
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"处理 {url} 时发生错误: {ex.Message}");
                }
            }

            try
            {
                // 发送请求  
                client.SendAsync().GetAwaiter().GetResult();
                //client.SendAsync("127.0.0.1", 5051, false, "JPAI", "JPAI");
                Console.WriteLine("推送完成。");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"发送 DICOM 文件时发生错误：{ex.Message}");
            }
            finally 
            {
                GC.Collect();        // Replace the following line:  
            }
        }



        //private static void NewMethod(List<string> Ftpuil, string FtpName, string FtpPWD, System.IO.MemoryStream memoryStream)
        //{
        //    var client = new DicomClient();
        //    long count = 0;
        //    for (int i = 0; i < Ftpuil.Count; i++)
        //    {
        //        FtpWebRequest request = (FtpWebRequest)WebRequest.Create(Ftpuil[i].ToString());
        //        request.Method = WebRequestMethods.Ftp.DownloadFile;
        //        request.Credentials = new NetworkCredential(FtpName, FtpPWD);
        //        using (FtpWebResponse response = (FtpWebResponse)request.GetResponse())
        //        using (Stream stream = response.GetResponseStream())
        //        {
        //            count = memoryStream.Length;
        //            stream.CopyTo(memoryStream);
        //        }   
        //        try
        //        {
        //            memoryStream.Position = count;
        //            var dicomFile = DicomFile.Open(memoryStream);
        //            var requests = new DicomCStoreRequest(dicomFile);
        //            client.AddRequest(requests);
        //        }
        //        catch (Exception ex)
        //        {
        //            Console.WriteLine(ex);
        //        }
        //    }
        //    try
        //    {
        //        // 发送请求  
        //        client.SendAsync("127.0.0.1", 5051, false, "JPAI", "JPAI");
        //        Console.WriteLine("推送完成");
        //        memoryStream.SetLength(0); // 清空内存 
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.Error.WriteLine($"发送 DICOM 文件时发生错误: {ex.Message}");
        //    }

        //}
        public static void Comparison(DataTable PACSData)
        {
            HashSet<string> existingLocalUrls = new HashSet<string>();
            DataTable BenDidata = new DataTable();
            BenDidata = sqlData.Local(Sql.locale, Sql.SelectLocal("1"));//查询本地数据
            foreach (DataRow row in BenDidata.Rows)
            {
                existingLocalUrls.Add(row["StyUid"].ToString());
            }
            for (int i = 0; i < PACSData.Rows.Count; i++)
            {
                if (!existingLocalUrls.Contains(PACSData.Rows[i]["STUDIESINSTUID"]))
                {
                    sqlData.Insert(Sql.locale, Sql.InsertUpdateDcm(PACSData.Rows[i]));                  
                }
            }

        }
    }
}
