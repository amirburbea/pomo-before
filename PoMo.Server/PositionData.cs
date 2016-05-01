using System;
using System.Data;
using System.Linq;
using System.Reflection;

namespace PoMo.Server
{
    internal sealed class PositionData
    {
        public static readonly PropertyInfo[] Properties = typeof(Position).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod.GetParameters().Length == 0)
            .ToArray();

        public static readonly DataTable SchemaTable = PositionData.CreateDataSchemaTable();

        private static DataTable CreateDataSchemaTable()
        {
            DataTable dataTable = new DataTable();
            foreach (PropertyInfo property in PositionData.Properties)
            {
                dataTable.Columns.Add(new DataColumn(property.Name, Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType));
            }
            dataTable.PrimaryKey = new[] { dataTable.Columns[nameof(Position.Ticker)] };
            return dataTable;
        }
    }
}