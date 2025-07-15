
using Dicom;
using Dicom.Network;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;


public class DicomSender
{
    public static void SendDicomFile(string ip, int port, string callingAe, string calledAe, System.IO.MemoryStream memory)
    {
        var client = new DicomClient();


        try
        {
            var dicomFile = DicomFile.Open(memory);
            var request = new DicomCStoreRequest(dicomFile);
            client.AddRequest(request);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
        }

        try
        {

            // 发送请求  
            client.SendAsync(ip, port, false, callingAe, calledAe);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"发送 DICOM 文件时发生错误: {ex.Message}");
        }
        finally
        {
            // 清理资源  
            //client.Dispose(); // 确保释放资源  
        }
    }
}