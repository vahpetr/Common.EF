using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Common.Dto;
using Common.Enums;
using Common.Extensions;
using Common.Filters;
using Common.Repositories.Contract;
using Common.Utilites;

namespace Common.EF.Repositories
{
    /// <summary>
    /// Базовое хранилище поддерживающее чтения данных
    /// </summary>
    /// <typeparam name="TEntity">Тип сущности</typeparam>
    /// <typeparam name="TFilter">Тип фильтра</typeparam>
    public class ReadRepository<TEntity, TFilter> : IReadRepository<TEntity, TFilter>
        where TEntity : class
        where TFilter : BaseFilter
    {
        /// <summary>
        /// Запрос к ленивому списку сущностей не отслеживаемый с подключёнными таблицами
        /// </summary>
        private readonly Lazy<IQueryable<TEntity>> _dbQueryNoTrackingWithInclude;

        /// <summary>
        /// Запрос к ленивому списку сущностей не отслеживаемый и без подключения таблиц
        /// </summary>
        private readonly Lazy<IQueryable<TEntity>> _dbQueryNoTrackingWithoutInclude;

        /// <summary>
        /// Запрос к ленивому списку сущностей отслеживаемый с подключёнными таблицами
        /// </summary>
        private readonly Lazy<IQueryable<TEntity>> _dbQueryTrackingWithInclude;

        /// <summary>
        /// Запрос к ленивому списку сущностей отслеживаемый и без подключения таблиц
        /// </summary>
        private readonly Lazy<IQueryable<TEntity>> _dbQueryTrackingWithoutInclude;

        /// <summary>
        /// Представление ленивого списка сущностей отслеживающего изменения
        /// </summary>
        private readonly Lazy<DbSet<TEntity>> _dbSet;

        /// <summary>
        /// Слой доступа к базе данных
        /// </summary>
        protected readonly DbContext dbContext;

        /// <summary>
        /// При получении сущности вначале искать в локале.
        /// В локаль сущности попадают если включено отслеживание сущностей
        /// </summary>
        protected bool Cache = false;

        /// <summary>
        /// Подключить связанные таблицы
        /// </summary>
        protected bool Include = true;

        /// <summary>
        /// Отслеживаие сущностей.
        /// Если включено значительно понижает производительность 
        /// </summary>
        protected bool Tracking = false;

        protected ReadRepository(DbContext dbContext)
        {
            dbContext.Configuration.LazyLoadingEnabled = false;
            dbContext.Configuration.ProxyCreationEnabled = false;
            dbContext.Configuration.AutoDetectChangesEnabled = false;

            this.dbContext = dbContext;
            _dbSet = new Lazy<DbSet<TEntity>>(dbContext.Set<TEntity>);

            _dbQueryNoTrackingWithoutInclude = new Lazy<IQueryable<TEntity>>(dbSet.AsNoTracking().AsQueryable);
            _dbQueryNoTrackingWithInclude =
                new Lazy<IQueryable<TEntity>>(() => ApplyInclude(dbQueryNoTrackingWithoutInclude));

            _dbQueryTrackingWithoutInclude = new Lazy<IQueryable<TEntity>>(dbSet.AsQueryable);
            _dbQueryTrackingWithInclude =
                new Lazy<IQueryable<TEntity>>(() => ApplyInclude(dbQueryTrackingWithoutInclude));
        }

        /// <summary>
        /// Представление списка сущностей отслеживающего изменения
        /// </summary>
        protected DbSet<TEntity> dbSet => _dbSet.Value;

        /// <summary>
        /// Запрос к списку сущностей не отслеживаемый и без подключения таблиц
        /// </summary>
        protected IQueryable<TEntity> dbQueryNoTrackingWithoutInclude => _dbQueryNoTrackingWithoutInclude.Value;

        /// <summary>
        /// Запрос к списку сущностей не отслеживаемый с подключёнными таблицами
        /// </summary>
        protected IQueryable<TEntity> dbQueryNoTrackingWithInclude => _dbQueryNoTrackingWithInclude.Value;

        /// <summary>
        /// Запрос к списку сущностей отслеживаемый и без подключения таблиц
        /// </summary>
        protected IQueryable<TEntity> dbQueryTrackingWithoutInclude => _dbQueryTrackingWithoutInclude.Value;

        /// <summary>
        /// Запрос к списку сущностей отслеживаемый с подключёнными таблицами
        /// </summary>
        protected IQueryable<TEntity> dbQueryTrackingWithInclude => _dbQueryTrackingWithInclude.Value;

        /// <summary>
        /// Запрос к списку сущностей
        /// </summary>
        protected IQueryable<TEntity> dbQuery
        {
            get
            {
                if (!Tracking)
                {
                    return Include ? dbQueryNoTrackingWithInclude : dbQueryNoTrackingWithoutInclude;
                }

                return Include ? dbQueryTrackingWithInclude : dbQueryTrackingWithoutInclude;
            }
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
                var cache = dbSet.Local.Find(key);
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
            var total = query.LongCount();
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
        /// Проверить существуют ли сущности
        /// </summary>
        /// <param name="key">Ключ</param>
        /// <returns>Логическое значение</returns>
        public virtual bool Exist(params object[] key)
        {
            var exist = dbQuery.Exist(key);
            return exist;
        }

        /// <summary>
        /// Асинхронно проверить существуют ли сущности
        /// </summary>
        /// <param name="key">Ключ</param>
        /// <returns>Логическое значение</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task<bool> ExistAsync(params object[] key)
        {
            return Task.FromResult(Exist(key));
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
                filter.SortBy.Split('.')
                    .Aggregate(
                        query.ElementType,
                        (type, name) => type?.GetProperty(name)?.PropertyType
                    ) != null;
            // отсортировать нужно в любом случае иначе дальше Skip/Take упадут
            if (!exist && query.ElementType.GetProperty(filter.SortBy) == null)
            {
                filter.SortBy = "Id";
            }

            // если Id нет берём первое попавшееся свойство
            if (!exist && query.ElementType.GetProperty(filter.SortBy) == null)
            {
                filter.SortBy = query.ElementType.GetProperties().First().Name;
            }

            var parameter = Expression.Parameter(query.ElementType, "entity");

            //поддержка глубоких селекторов model.innerModel
            var property = filter.SortBy.Split('.').Aggregate<string, Expression>(parameter, Expression.Property);
            var func = typeof (Func<,>);
            var genericFunc = func.MakeGenericType(query.ElementType, property.Type);
            var expression = Expression.Lambda(genericFunc, property, parameter);

            query = query.Provider.CreateQuery<TEntity>(
                Expression.Call(
                    typeof (Queryable),
                    filter.Order == Order.Asc ? "OrderBy" : "OrderByDescending",
                    new[] {query.ElementType, expression.Body.Type},
                    query.Expression,
                    Expression.Quote(expression)));

            var genericType = typeof (EntityUtilites<>).MakeGenericType(query.ElementType);
            var getKeyProps = genericType.GetMethod("Get");
            object[] args = {};
            var keyProps = (PropertyInfo[]) getKeyProps.Invoke(null, args);

            //исключаем свойство из дополнительной сортировке по ключу
            //обязательно нужна дополнительная сортировка по ключу если ключ составной
            foreach (var prop in keyProps.Where(p => p.Name != filter.SortBy))
            {
                var keyProperty = Expression.Property(parameter, prop.Name);
                var keyFunc = typeof (Func<,>);
                var keyGenericFunc = keyFunc.MakeGenericType(query.ElementType, keyProperty.Type);
                var keyEexpression = Expression.Lambda(keyGenericFunc, keyProperty, parameter);

                query = query.Provider.CreateQuery<TEntity>(
                    Expression.Call(
                        typeof (Queryable),
                        filter.Order == Order.Asc ? "ThenBy" : "ThenByDescending",
                        new[] {query.ElementType, keyEexpression.Body.Type},
                        query.Expression,
                        Expression.Quote(keyEexpression)));
            }

            return query;
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
    }
}
