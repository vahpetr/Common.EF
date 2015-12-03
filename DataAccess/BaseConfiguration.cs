using System;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Migrations;
using System.Data.Entity.Validation;
using System.Diagnostics;
using System.Linq;

namespace Common.EF.DataAccess
{
    /// <summary>
    /// Базовый конфигуратор базы данных
    /// </summary>
    /// <typeparam name="TContext">Контекст базы данных</typeparam>
    public class BaseConfiguration<TContext> : DbMigrationsConfiguration<TContext>
        where TContext : DbContext
    {
        /// <summary>
        /// Конструктор базового конфигуратора базы данных
        /// </summary>
        public BaseConfiguration()
        {
            AutomaticMigrationsEnabled = false;
            SetSqlGenerator("System.Data.SqlClient", new CustomSqlServerMigrationSqlGenerator());
        }

        protected override void Seed(TContext context)
        {
            if (DataExist(context)) return;

            ChangeDatabaseStructure(context);

            using (var connection = ((IObjectContextAdapter)context).ObjectContext.Connection)
            {
                if (connection.State != ConnectionState.Open) connection.Open();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        AddDbObjects(context);
                        ExecuteRawQueries(context);
                        context.SaveChanges();
                        transaction.Commit();
                    }
                    catch (Exception e)
                    {
                        transaction.Rollback();

                        if (e.GetType() == typeof(DbEntityValidationException))
                        {
                            //открыть отладочный процесс
                            if (Debugger.IsAttached == false)
                            {
                                Debugger.Launch();
                            }

                            var results = ((DbEntityValidationException)e).EntityValidationErrors;
                            foreach (var error in results.SelectMany(p => p.ValidationErrors))
                            {
                                Debug.Write(error.PropertyName, error.ErrorMessage);
                            }
                        }

                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Шаг 0. Проверяем необходимо ли накатить на базу стартовую миграцию
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        protected virtual bool DataExist(TContext context)
        {
            return false;
        }

        /// <summary>
        /// Шаг 1. Модифицируем таблици, создаём тригеры, функции, процедуры
        /// </summary>
        /// <param name="context"></param>
        protected virtual void ChangeDatabaseStructure(TContext context)
        {
        }

        /// <summary>
        /// Шаг 2. Заполняем таблици стартовыми данными
        /// </summary>
        /// <param name="context"></param>
        protected virtual void AddDbObjects(TContext context)
        {
        }

        /// <summary>
        /// Шаг 3. Прочии действия
        /// </summary>
        /// <param name="context"></param>
        protected virtual void ExecuteRawQueries(TContext context)
        {
        }
    }
}