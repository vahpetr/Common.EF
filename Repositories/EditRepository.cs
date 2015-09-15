using System;
using System.Data.Entity;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Common.Extensions;
using Common.Repositories.Contract;

namespace Common.EF.Repositories
{
    /// <summary>
    /// Базовое хранилище поддерживающее редактирования данных
    /// </summary>
    /// <typeparam name="TEntity">Тип сущности</typeparam>
    public class EditRepository<TEntity> : IEditRepository<TEntity>
        where TEntity : class
    {
        /// <summary>
        /// Слой доступа к базе данных
        /// </summary>
        protected readonly DbContext dbContext;

        /// <summary>
        /// Представление списка сущностей отслеживающего изменения
        /// </summary>
        protected readonly Lazy<DbSet<TEntity>> dbSet;

        protected EditRepository(DbContext dbContext)
        {
            this.dbContext = dbContext;
            dbSet = new Lazy<DbSet<TEntity>>(dbContext.Set<TEntity>);
        }

        /// <summary>
        /// Добавить граф сущности в хранилище сущностей и пометить его как добавленный
        /// </summary>
        /// <param name="entity">Новаый граф сущности</param>
        public virtual void Add(TEntity entity)
        {
            var dbEntityEntry = dbContext.Entry(entity);
            if (dbEntityEntry.State == EntityState.Detached)
            {
                dbSet.Value.Add(entity);
            }
            dbEntityEntry.State = EntityState.Added;
        }

        /// <summary>
        /// Добавить граф сущности в контекст
        /// </summary>
        /// <param name="entity">Граф сущности</param>
        private void Attach(TEntity entity)
        {
            if (dbContext.Entry(entity).State != EntityState.Detached) return;
            dbSet.Value.Attach(entity);
        }

        /// <summary>
        /// Пометить свойство сущности как изменённое
        /// </summary>
        /// <param name="entity">Сущность</param>
        /// <param name="expressions">Свойства</param>
        public virtual void Modified(TEntity entity, params Expression<Func<TEntity, object>>[] expressions)
        {
            Attach(entity);
            foreach (var expression in expressions)
            {
                dbContext.Entry(entity).Property(expression).IsModified = true;
            }

            //dbContext.Entry(entity).State = EntityState.Detached;
            //var copy = entity.Convert(new TEntity());
            //Attach(copy);
            //foreach (var expression in expressions)
            //{
            //    dbContext.Entry(copy).Property(expression).IsModified = true;
            //}
        }

        /// <summary>
        /// Добавить граф сущности в хранилище сущностей и пометить его как изменённый
        /// </summary>
        /// <param name="entity">Обновляемый граф сущности</param>
        public virtual void Update(TEntity entity)
        {
            Attach(entity);
            dbContext.Entry(entity).State = EntityState.Modified;
        }

        /// <summary>
        /// Удалить сущность
        /// </summary>
        /// <param name="entity">Сущность</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public virtual void Remove(TEntity entity)
        {
            var identity = entity.Identity();
            dbContext.Entry(identity).State = EntityState.Deleted;
        }

        /// <summary>
        /// Сохранить все изменения
        /// </summary>
        /// <returns>Количество изменённых строк в базе</returns>
        public virtual int SaveChanges()
        {
            return dbContext.SaveChanges();
        }

        /// <summary>
        /// Асинхронно сохранить все изменения
        /// </summary>
        /// <returns>Количество изменённых строк в базе</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task<int> SaveChangesAsync()
        {
            return Task.FromResult(SaveChanges());
        }
    }
}