using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MemoryStream
{
    public static class Sql
    {
        /// <summary>
        /// 查询pacs数据
        /// </summary>
        /// <param name="day">天数</param>
        /// <returns></returns>
        public static string SelectPacs(string day)
        {
            return $"SELECT * FROM ReportForDicom where STUDIESDONEDATE>trunc(sysdate - {day}) and STUDIESDONEDATE <SYSDATE - INTERVAL '10' MINUTE ";
        }
        /// <summary>
        /// 查询本地数据
        /// </summary>
        /// <param name="day">天数</param>
        /// <returns></returns>
        public static string SelectLocal(string day)
        {
            return $"select * from [JPSPV20].[dbo].[UpdateDcm] where CreaDate >GETDATE()-{day}";
        }
        /// <summary>
        /// 插入本地数据
        /// </summary>
        /// <param name="ROW"></param>
        /// <returns></returns>
        public static string InsertUpdateDcm(DataRow ROW)
        {
            return $"INSERT INTO [JPSPV20].[dbo].[UpdateDcm] VALUES(NEWID(),'{ROW["PATIENTSID"]}','{ROW["ACCESSIONNUMBER"]}','{ROW["STUDIESINSTUID"]}','{DateTime.Now}','{ROW["IMAGESFILENAME"]}','1','1')";

        }
        /// <summary>
        /// 查询本地待上传数据
        /// </summary>
        /// <param name="day">天数</param>
        /// <returns></returns>
        public static string SelectUploadLocal(string day)
        {
            return $"select top 10 * from [JPSPV20].[dbo].[UpdateDcm] where CreaDate >GETDATE()-{day} and [UpdateStase]='1' order by [ImageId] desc";
        }
        /// <summary>
        /// 更新本地数据
        /// </summary>
        /// <param name="stuid"></param>
        /// <returns></returns>
        public static string UpdateUploadLocal(string stuid)
        {
            return $"UPDATE [JPSPV20].[dbo].[UpdateDcm] SET [UpdateStase]=2  where [StyUid]='{stuid}'";
        }
        /// <summary>
        /// 获取推送错误更新数据库
        /// </summary>
        /// <param name="stuid"></param>
        /// <returns></returns>
        public static string ErrorUpdateUploadLocal(string stuid)
        {
            return $"UPDATE [JPSPV20].[dbo].[UpdateDcm] SET [UpdateStase]=3  where [StyUid]='{stuid}'";
        }
        /// <summary>
        /// pacs链接字符串
        /// </summary>
        public static string pacsconfig = "User Id=exam;Password=exam;Data Source=172.18.200.100:1521/orcl";
        /// <summary>
        /// 本地链接字符串
        /// </summary>
        public static string locale = "server=127.0.0.1;database=JPSPV20;uid=sa;pwd=Jp123!@#";
    }
}

