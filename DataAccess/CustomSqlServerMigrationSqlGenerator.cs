using System.Collections.Generic;
using System.Data.Entity.Migrations.Model;
using System.Data.Entity.SqlServer;

namespace Common.EF.DataAccess
{
    /// <summary>
    /// Генератор запросов из миграций
    /// </summary>
    public class CustomSqlServerMigrationSqlGenerator : SqlServerMigrationSqlGenerator
    {
        protected override void Generate(AddColumnOperation addColumnOperation)
        {
            SetDefaultValueSql(addColumnOperation.Column);

            base.Generate(addColumnOperation);
        }

        protected override void Generate(CreateTableOperation createTableOperation)
        {
            SetDefaultValueSql(createTableOperation.Columns);

            base.Generate(createTableOperation);
        }

        private static void SetDefaultValueSql(IEnumerable<ColumnModel> columns)
        {
            foreach (var columnModel in columns)
            {
                SetDefaultValueSql(columnModel);
            }
        }

        private static void SetDefaultValueSql(PropertyModel column)
        {
            switch (column.Name)
            {
                case "Position":
                    {
                        column.DefaultValueSql = "0";
                        break;
                    }
                case "Available":
                    {
                        column.DefaultValueSql = "1";
                        break;
                    }
                case "Secret":
                    {
                        column.DefaultValueSql = "NEWID()";
                        break;
                    }
                case "Created":
                case "Updated":
                case "Publish":
                    {
                        column.DefaultValueSql = "GETDATE()";
                        break;
                    }
            }
        }
    }
}