using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using System.Data.Odbc;

namespace MemoryStream
{
    class sqlData
    {
        public static DataTable PACS(string constr, string sql)
        {
            DataTable dt = new DataTable();
            // string connectionString = "User Id=exam;Password=exam;Data Source=172.18.200.100:1521/orcl";

            using (OracleConnection connection = new OracleConnection(constr))
            {
                try
                {
                    // 打开数据库连接  
                    connection.Open();
                    Console.WriteLine("连接成功！！");
                    using (OracleDataAdapter command = new OracleDataAdapter(sql, connection))
                    {
                        command.Fill(dt);
                        return dt;
                    }
                }
                catch (OracleException ex)
                {
                    Console.WriteLine("Oracle 错误: " + ex.Message);
                    return dt;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("一般错误: " + ex.Message);
                    return dt;
                }
                finally
                {
                    // 确保连接被正确关闭  
                    if (connection.State == System.Data.ConnectionState.Open)
                    {
                        connection.Close();
                    }
                }
            }
        }
        public static DataTable Local(string constr, string sql)
        {
            DataTable dt = new DataTable();
            try
            {
                using (SqlConnection con = new SqlConnection(constr))
                {
                    SqlDataAdapter adapter = new SqlDataAdapter(sql, con);
                    adapter.Fill(dt);
                    return dt;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return dt;
            }
        }
        public static void Insert(string constr, String sql)
        {
            try
            {
                using (SqlConnection con = new SqlConnection(constr))
                {
                    con.Open();
                    SqlCommand cmd = new SqlCommand(sql, con);
                    cmd.ExecuteNonQuery();
                    con.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }


        }
    }
}
