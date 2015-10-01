using System;
using System.Collections.Generic;
using System.Text;
using System.Data.SqlClient;
using System.Data;

namespace Chaos.WebSpider.Tester
{
    public class DBHelper
    {
        public const string connStr = "Data Source=(local)\\SQLEXPRESS;Initial Catalog=Chaos;User ID=sa;Password=123456;";

        public static int DoSql(string sql)
        {
            using (SqlConnection sqlConn = new SqlConnection(connStr))
            {
                sqlConn.Open();
                SqlCommand cmd = new SqlCommand();
                cmd.CommandText = sql;
                cmd.Connection = sqlConn;
                return cmd.ExecuteNonQuery();
            }
        }

        public static int CheckThing(int sql)
        {
            using (SqlConnection sqlConn = new SqlConnection(connStr))
            {
                sqlConn.Open();
                SqlCommand cmd = new SqlCommand();
                cmd.CommandText = "select Id from Thing with(nolock) where id = " + sql;
                cmd.Connection = sqlConn;
                object id = cmd.ExecuteScalar();
                if (id != null)
                {
                    return int.Parse(id.ToString());
                }
                else
                {
                    return 0;
                }
            }
        }

        public static List<int> GetIDs(int beginId, int endId)
        {
            List<int> ids = new List<int>();
            using (SqlConnection sqlConn = new SqlConnection(connStr))
            {
                sqlConn.Open();
                SqlCommand cmd = new SqlCommand();
                cmd.CommandText = string.Format(@"select (select max(id)+1 
from Chaos.dbo.Thing 
where id<a.id) as beginId,
(id-1) as endId
from Chaos.dbo.Thing a
where a.ID > {0} and a.Id < {1} and
    a.id>(select max(id)+1 from Chaos.dbo.Thing where id<a.id)", beginId, endId);
                cmd.Connection = sqlConn;
                SqlDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    int start = int.Parse(reader["beginId"].ToString());
                    int end = int.Parse(reader["endId"].ToString());
                    for (; start <= end; start++)
                    {
                        ids.Add(start);
                    }
                }
            }
            return ids;
        }
    }
}
