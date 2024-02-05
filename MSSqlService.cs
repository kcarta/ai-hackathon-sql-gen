using System.Data;
using System.Data.SqlClient;

namespace DataAgent
{

    internal class MSSqlService()
    {
        public static DataTable Search(string query, string connectionString)
        {
            var dataTable = new DataTable();
            using(var connection = new SqlConnection(connectionString))
            {
                try {
                    connection.Open();
                    using var command = new SqlCommand(query, connection);
                    using var adapter = new SqlDataAdapter(command);
                    adapter.Fill(dataTable);
                }
                catch(Exception e){
                    Console.WriteLine($"Error: {e.Message}");
                    #if DEBUG
                    Console.WriteLine($"Based on query: \n{query}");
                    #endif
                }
            }
            return dataTable;
        }
    }
}