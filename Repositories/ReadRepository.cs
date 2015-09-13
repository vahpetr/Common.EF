using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Common.Extensions;
using Common.Models.Dto;
using Common.Models.Enums;
using Common.Models.Filters;
using Common.Repositories.Contract;

namespace Common.EF.Repositories
{
    /// <summary>
    /// Базовое хранилище поддерживающее чтения данных
    /// </summary>
    /// <typeparam name="TEntity">Сущность контекста</typeparam>
    /// <typeparam name="TFilter">Фильтр сущностей</typeparam>
    public class ReadRepository<TEntity, TFilter> : IReadRepository<TEntity, TFilter> 
        where TEntity : class
        where TFilter : BaseFilter
    {
        /// <summary>
        /// Отслеживаие сущностей.
        /// Если включено значительно понижает производительность 
        /// </summary>
        protected bool Tracking = false;

        /// <summary>
        /// При получении сущности вначале искать в локале.
        /// В локаль сущности попадают если включено отслеживание сущностей
        /// </summary>
        protected bool Cache = true;

        /// <summary>
        /// Подключить связанные таблицы
        /// </summary>
        protected bool Include = true;

        /// <summary>
        /// Слой доступа к базе данных
        /// </summary>
        protected readonly DbContext dbContext;

        /// <summary>
        /// Представление списка сущностей отслеживающего изменения
        /// </summary>
        protected readonly Lazy<DbSet<TEntity>> dbSet;

        /// <summary>
        /// Запрос к списку сущностей не отслеживаемый и без подключения таблиц
        /// </summary>
        protected readonly Lazy<IQueryable<TEntity>> dbQueryNoTrackingWithoutInclude;

        /// <summary>
        /// Запрос к списку сущностей не отслеживаемый с подключёнными таблицами
        /// </summary>
        protected readonly Lazy<IQueryable<TEntity>> dbQueryNoTrackingWithInclude;

        /// <summary>
        /// Запрос к списку сущностей отслеживаемый и без подключения таблиц
        /// </summary>
        protected readonly Lazy<IQueryable<TEntity>> dbQueryTrackingWithoutInclude;

        /// <summary>
        /// Запрос к списку сущностей отслеживаемый с подключёнными таблицами
        /// </summary>
        protected readonly Lazy<IQueryable<TEntity>> dbQueryTrackingWithInclude;

        /// <summary>
        /// Запрос к списку сущностей
        /// </summary>
        protected IQueryable<TEntity> dbQuery
        {
            get
            {
                if (!Tracking)
                {
                    if (Include)
                    {
                        return dbQueryNoTrackingWithInclude.Value;
                    }

                    return dbQueryNoTrackingWithoutInclude.Value;
                }

                if (Include)
                {
                    return dbQueryTrackingWithInclude.Value;
                }

                return dbQueryTrackingWithoutInclude.Value;
            }
        }

        protected ReadRepository(DbContext dbContext)
        {
            dbContext.Configuration.LazyLoadingEnabled = false;
            dbContext.Configuration.ProxyCreationEnabled = false;
            dbContext.Configuration.AutoDetectChangesEnabled = false;

            this.dbContext = dbContext;
            dbSet = new Lazy<DbSet<TEntity>>(dbContext.Set<TEntity>);

            dbQueryNoTrackingWithoutInclude = new Lazy<IQueryable<TEntity>>(dbSet.Value.AsNoTracking().AsQueryable);
            dbQueryNoTrackingWithInclude = new Lazy<IQueryable<TEntity>>(() => ApplyInclude(dbQueryNoTrackingWithoutInclude.Value));

            dbQueryTrackingWithoutInclude = new Lazy<IQueryable<TEntity>>(dbSet.Value.AsQueryable);
            dbQueryTrackingWithInclude = new Lazy<IQueryable<TEntity>>(() => ApplyInclude(dbQueryTrackingWithoutInclude.Value));
        }

        /// <summary>
        /// Подключает таблици к запросу
        /// </summary>
        /// <param name="query">Запрос</param>
        /// <returns>Запрос с подключёнными таблицами</returns>
        protected virtual IQueryable<TEntity> ApplyInclude(IQueryable<TEntity> query)
        {
            return query;
        }

        /// <summary>
        /// Применить фильтацию
        /// </summary>
        /// <param name="query">Запрос</param>
        /// <param name="filter">Фильтр</param>
        /// <returns>Отфильтрированны запрос</returns>
        protected virtual IQueryable<TEntity> ApplyFilter(IQueryable<TEntity> query, TFilter filter)
        {
            if (string.IsNullOrEmpty(filter.Q)) return query;

            var expressions = new List<MethodCallExpression>();
            //входной параметр(TEntity) для функтора 
            var entity = Expression.Parameter(query.ElementType, "p");
            var stringType = typeof (string);
            //ссылка на функцию Contains
            var containsMethod = stringType.GetMethod("Contains");
            //ссылка на функцию ToLower
            var toLowerMethod = stringType.GetMethod("ToLower", Type.EmptyTypes);
            var text = filter.Q.ToLower();

            var props = query.ElementType.GetProperties().Where(p => p.PropertyType == stringType);

            foreach (var prop in props)
            {
                //доступ к свойству field.Name объекта TEntity
                var property = Expression.Property(entity, prop.Name);
                //выражение вызова метода ToLower
                var toLower = Expression.Call(property, toLowerMethod);
                //выражение вызова метода Contains с параметром keyword
                var contains = Expression.Call(toLower, containsMethod, Expression.Constant(text));
                expressions.Add(contains);
            }

            if (!expressions.Any()) return query;

            //комбинируем все выражения оператором или
            var body = expressions.Aggregate<Expression>(Expression.OrElse);
            //создаём выражение Func<TEntity, bool>
            var searchExpression = Expression.Lambda<Func<TEntity, bool>>(body, entity);

            return query.Where(searchExpression);
        }

        /// <summary>
        /// Применить сортировку
        /// </summary>
        /// <param name="query">Запрос</param>
        /// <param name="filter">Фильтр</param>
        /// <returns>Отсортированный запрос</returns>
        protected virtual IQueryable<TEntity> ApplySort(IQueryable<TEntity> query, TFilter filter)
        {
            var exist =
                filter.Sort.Split('.')
                .Aggregate(query.ElementType, (type, name) =>
                {
                    if (type == null) return null;

                    var propertyInfo = type.GetProperty(name);

                    if(propertyInfo == null) return null;

                    return propertyInfo.PropertyType;
                }) != null;
            // отсортировать нужно в любом случае иначе дальше Skip/Take упадут
            if (!exist && query.ElementType.GetProperty(filter.Sort) == null)
            {
                filter.Sort = "Id";
            }

            // если Id нет берём первое попавшееся свойство
            if (!exist && query.ElementType.GetProperty(filter.Sort) == null)
            {
                filter.Sort = query.ElementType.GetProperties().First().Name;
            }

            var parameter = Expression.Parameter(query.ElementType, "entity");

            //поддержка глубоких селекторов model.innerModel
            var property = filter.Sort.Split('.').Aggregate<string, Expression>(parameter, Expression.Property);
            var func = typeof (Func<,>);
            var genericFunc = func.MakeGenericType(query.ElementType, property.Type);
            var expression = Expression.Lambda(genericFunc, property, parameter);

            return query.Provider.CreateQuery<TEntity>(
                Expression.Call(
                    typeof (Queryable),
                    filter.Order == Order.Asc ? "OrderBy" : "OrderByDescending",
                    new[] {query.ElementType, expression.Body.Type},
                    query.Expression,
                    Expression.Quote(expression)
                    )
                );
        }

        /// <summary>
        /// Произвести дополнительную обработку над элементами результирующей последовательности
        /// </summary>
        /// <param name="items">Список сущностей</param>
        /// <returns>Список сущностей</returns>
        public virtual IEnumerable<TEntity> ApplyMapping(IEnumerable<TEntity> items)
        {
            return items;
        } 

        /// <summary>
        /// Получить сущность
        /// </summary>
        /// <param name="key">Ключ</param>
        /// <returns>Сущность</returns>
        public virtual TEntity Get(params object[] key)
        {
            if (Cache)
            {
                var cache = dbSet.Value.Local.Find(key);
                if (cache != null) return cache;
            }         

            var item = dbQuery.Find(key);
            if (item == null) return null;

            var items = new[] {item};
            var mapping = ApplyMapping(items);
            var data = mapping.First();
            return data;
        }

        /// <summary>
        /// Асинхронно получить сущность
        /// </summary>
        /// <param name="key">Ключ</param>
        /// <returns>Сущность</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task<TEntity> GetAsync(params object[] key)
        {
            return Task.FromResult(Get(key));
        }

        /// <summary>
        /// Получить страницу сущностей
        /// </summary>
        /// <param name="filter">Фильтр</param>
        /// <returns>Страница сущностей</returns>
        public virtual ResultDto<TEntity> Get(TFilter filter)
        {
            var query = ApplyFilter(dbQuery, filter);
            var sort = ApplySort(query, filter);
            var range = sort.Skip(filter.Skip).Take(filter.Take);
            var total = dbQuery.LongCount();
            var items = range.ToArray();
            var mapping = ApplyMapping(items);
            return new ResultDto<TEntity>
            {
                Total = total,
                Data = mapping
            };
        }

        /// <summary>
        /// Асинхронно получить страницу сущностей
        /// </summary>
        /// <param name="filter">Фильтр</param>
        /// <returns>Страница сущностей</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task<ResultDto<TEntity>> GetAsync(TFilter filter)
        {
            return Task.FromResult(Get(filter));
        }

        /// <summary>
        /// Получить все сущности
        /// </summary>
        /// <param name="filter">Фильтр</param>
        /// <returns>Все сущности</returns>
        public virtual IEnumerable<TEntity> GetAll(TFilter filter)
        {
            var query = ApplyFilter(dbQuery, filter);
            var sort = ApplySort(query, filter);
            var items = sort.ToArray();
            var mapping = ApplyMapping(items);
            return mapping;
        }

        /// <summary>
        /// Асинхронно получить все сущности
        /// </summary>
        /// <param name="filter">Фильтр</param>
        /// <returns>Все сущности</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task<IEnumerable<TEntity>> GetAllAsync(TFilter filter)
        {
            return Task.FromResult(GetAll(filter));
        }

        /// <summary>
        /// Проверить существуют ли сущности
        /// </summary>
        /// <param name="filter">Фильтр</param>
        /// <returns>Логическое значение</returns>
        public virtual bool Exist(TFilter filter)
        {
            var query = ApplyFilter(dbQuery, filter);
            var exist = query.Any();
            return exist;
        }

        /// <summary>
        /// Асинхронно проверить существуют ли сущности
        /// </summary>
        /// <param name="filter">Фильтр</param>
        /// <returns>Логическое значение</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task<bool> ExistAsync(TFilter filter)
        {
            return Task.FromResult(Exist(filter));
        }

        /// <summary>
        /// Получить количество сущностей
        /// </summary>
        /// <param name="filter">Фильтр</param>
        /// <returns>Количество сущностей</returns>
        public virtual long Count(TFilter filter)
        {
            var query = ApplyFilter(dbQuery, filter);
            var count = query.LongCount();
            return count;
        }

        /// <summary>
        /// Асинхронно получить количество сущностей
        /// </summary>
        /// <param name="filter">Фильтр</param>
        /// <returns>Количество сущностей</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task<long> CountAsync(TFilter filter)
        {
            return Task.FromResult(Count(filter));
        }
    }
}