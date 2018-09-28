﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using MongoDB.Bson.Serialization;
using MongoDB.Driver;

using PipServices.Commons.Config;
using PipServices.Commons.Convert;
using PipServices.Commons.Data;
using PipServices.Commons.Reflect;
using PipServices.Data;

namespace PipServices.MongoDb.Persistence
{
    /// <summary>
    /// Abstract persistence component that stores data in MongoDB
    /// and implements a number of CRUD operations over data items with unique ids.
    /// The data items must implement IIdentifiable interface.
    /// 
    /// In basic scenarios child classes shall only override getPageByFilter(),
    /// getListByFilter() or deleteByFilter() operations with specific filter function.
    /// All other operations can be used out of the box.
    /// 
    /// In complex scenarios child classes can implement additional operations by
    /// accessing this._collection and this._model properties.
    /// 
    /// ### Configuration parameters ###
    /// 
    /// collection:                  (optional) MongoDB collection name
    /// connection(s):    
    /// discovery_key:             (optional) a key to retrieve the connection from IDiscovery
    /// host:                      host name or IP address
    /// port:                      port number (default: 27017)
    /// uri:                       resource URI or connection string with all parameters in it
    /// credential(s):    
    /// store_key:                 (optional) a key to retrieve the credentials from ICredentialStore
    /// username:                  (optional) user name
    /// password:                  (optional) user password
    /// options:
    /// max_pool_size:             (optional) maximum connection pool size (default: 2)
    /// keep_alive:                (optional) enable connection keep alive (default: true)
    /// connect_timeout:           (optional) connection timeout in milliseconds (default: 5 sec)
    /// auto_reconnect:            (optional) enable auto reconnection (default: true)
    /// max_page_size:             (optional) maximum page size (default: 100)
    /// debug:                     (optional) enable debug output (default: false).
    /// 
    /// ### References ###
    /// 
    /// - *:logger:*:*:1.0           (optional) ILogger components to pass log messages
    /// - *:discovery:*:*:1.0        (optional) IDiscovery services
    /// - *:credential-store:*:*:1.0 (optional) Credential stores to resolve credentials
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="K"></typeparam>
    /// <example>
    /// <code>
    /// class MyMongoDbPersistence: MongoDbPersistence<MyData, string> 
    /// {
    ///     public constructor()
    ///     {
    ///         base("mydata", MyData.class);
    ///     }
    /// 
    ///     private FilterDefinition<MyData> ComposeFilter(FilterParams filter)
    ///     {
    ///         filterParams = filterParams ?? new FilterParams();
    ///         var builder = Builders<BeaconV1>.Filter;
    ///         var filter = builder.Empty;
    ///         String name = filter.getAsNullableString('name');
    ///         if (name != null)
    ///             filter &= builder.Eq(b => b.Name, name);
    ///         return filter;
    ///     }
    ///     
    ///     public GetPageByFilter(String correlationId, FilterParams filter, PagingParams paging)
    ///     {
    ///         base.GetPageByFilter(correlationId, this.ComposeFilter(filter), paging, null, null);
    ///     }
    /// }
    /// 
    /// var persistence = new MyMongoDbPersistence();
    /// persistence.Configure(ConfigParams.fromTuples(
    /// "host", "localhost",
    /// "port", 27017 ));
    /// 
    /// persitence.Open("123");
    /// 
    /// persistence.Create("123", new MyData("1", "ABC"));
    /// var mydata = persistence.GetPageByFilter(
    /// "123",
    /// FilterParams.FromTuples("name", "ABC"),
    /// Console.Out.WriteLine(mydata.Data);          // Result: { id: "1", name: "ABC" }
    /// 
    /// persistence.DeleteById("123", "1");
    /// ...
    /// </code>
    /// </example>
    public class IdentifiableMongoDbPersistence<T, K> : MongoDbPersistence<T>, IWriter<T, K>, IGetter<T, K>, ISetter<T>
        where T : IIdentifiable<K>
        where K : class
    {
        protected int _maxPageSize = 100;

        protected const string InternalIdFieldName = "_id";

        /// <summary>
        /// Creates a new instance of the persistence component.
        /// </summary>
        /// <param name="collectionName">(optional) a collection name.</param>
        public IdentifiableMongoDbPersistence(string collectionName)
            : base(collectionName)
        { }

        /// <summary>
        /// Configures component by passing configuration parameters.
        /// </summary>
        /// <param name="config">configuration parameters to be set.</param>
        public override void Configure(ConfigParams config)
        {
            base.Configure(config);

            _maxPageSize = config.GetAsIntegerWithDefault("options.max_page_size", _maxPageSize);
        }

        /// <summary>
        /// Gets a page of data items retrieved by a given filter and sorted according to sort parameters.
        /// 
        /// This method shall be called by a public getPageByFilter method from child
        /// class that receives FilterParams and converts them into a filter function.
        /// </summary>
        /// <param name="correlationId">(optional) transaction id to trace execution through call chain.</param>
        /// <param name="filterDefinition">(optional) a filter JSON object</param>
        /// <param name="paging">(optional) paging parameters</param>
        /// <param name="sortDefinition">(optional) sorting JSON object</param>
        /// <returns>data page of results by filter.</returns>
        public virtual async Task<DataPage<T>> GetPageByFilterAsync(string correlationId, FilterDefinition<T> filterDefinition,
            PagingParams paging = null, SortDefinition<T> sortDefinition = null)
        {
            var documentSerializer = BsonSerializer.SerializerRegistry.GetSerializer<T>();
            var renderedFilter = filterDefinition.Render(documentSerializer, BsonSerializer.SerializerRegistry);

            var query = _collection.Find(renderedFilter);
            if (sortDefinition != null)
                query = query.Sort(sortDefinition);

            paging = paging ?? new PagingParams();
            var skip = paging.GetSkip(0);
            var take = paging.GetTake(_maxPageSize);

            var count = paging.Total ? (long?)await query.CountDocumentsAsync() : null;
            var items = await query.Skip((int)skip).Limit((int)take).ToListAsync();

            _logger.Trace(correlationId, $"Retrieved {items.Count} from {_collection}");

            return new DataPage<T>()
            {
                Data = items,
                Total = count
            };
        }

        /// <summary>
        /// Gets a page of data items retrieved by a given filter and sorted according to sort parameters.
        /// 
        /// This method shall be called by a public getPageByFilter method from child
        /// class that receives FilterParams and converts them into a filter function.
        /// </summary>
        /// <param name="correlationId">(optional) transaction id to trace execution through call chain.</param>
        /// <param name="filterDefinition">(optional) a filter JSON object</param>
        /// <param name="paging">(optional) paging parameters</param>
        /// <param name="sortDefinition">(optional) sorting JSON object</param>
        /// <param name="projection">(optional) projection parameters</param>
        /// <returns>data page of results by filter.</returns>
        public virtual async Task<DataPage<object>> GetPageByFilterAndProjectionAsync(string correlationId, FilterDefinition<T> filterDefinition,
            PagingParams paging = null, SortDefinition<T> sortDefinition = null, ProjectionParams projection = null)
        {
            var documentSerializer = BsonSerializer.SerializerRegistry.GetSerializer<T>();
            var renderedFilter = filterDefinition.Render(documentSerializer, BsonSerializer.SerializerRegistry);

            var query = _collection.Find(renderedFilter);
            if (sortDefinition != null)
            {
                query = query.Sort(sortDefinition);
            }

            var projectionBuilder = Builders<T>.Projection;
            var projectionDefinition = CreateProjectionDefinition(projection, projectionBuilder);

            paging = paging ?? new PagingParams();
            var skip = paging.GetSkip(0);
            var take = paging.GetTake(_maxPageSize);

            var count = paging.Total ? (long?)await query.CountDocumentsAsync() : null;
            var items = await query.Project(projectionDefinition).Skip((int)skip).Limit((int)take).ToListAsync();

            var result = new DataPage<object>()
            {
                Data = new List<object>(),
                Total = count
            };

            using (var cursor = await query.Project(projectionDefinition).Skip((int)skip).Limit((int)take).ToCursorAsync())
            {
                while (await cursor.MoveNextAsync())
                {
                    foreach (var doc in cursor.Current)
                    {
                        if (doc.ElementCount != 0)
                        {
                            result.Data.Add(BsonSerializer.Deserialize<object>(doc));
                        }
                    }
                }
            }

            _logger.Trace(correlationId, $"Retrieved {result.Total} from {_collection} with projection fields = '{StringConverter.ToString(projection)}'");

            return result;
        }

        /// <summary>
        /// Gets a list of data items retrieved by a given filter and sorted according to sort parameters.
        /// 
        /// This method shall be called by a public getListByFilter method from child
        /// class that receives FilterParams and converts them into a filter function.
        /// </summary>
        /// <param name="correlationId">(optional) transaction id to trace execution through call chain.</param>
        /// <param name="filterDefinition">(optional) a filter JSON object</param>
        /// <param name="sortDefinition">(optional) sorting JSON object</param>
        /// <returns></returns>
        public virtual async Task<List<T>> GetListByFilterAsync(string correlationId, FilterDefinition<T> filterDefinition,
            SortDefinition<T> sortDefinition = null)
        {
            var documentSerializer = BsonSerializer.SerializerRegistry.GetSerializer<T>();
            var renderedFilter = filterDefinition.Render(documentSerializer, BsonSerializer.SerializerRegistry);

            var query = _collection.Find(renderedFilter);
            if (sortDefinition != null)
                query = query.Sort(sortDefinition);

            var items = await query.ToListAsync();

            _logger.Trace(correlationId, $"Retrieved {items.Count} from {_collection}");

            return items;
        }

        /// <summary>
        /// Gets a list of data items retrieved by given unique ids.
        /// </summary>
        /// <param name="correlationId">(optional) transaction id to trace execution through call chain.</param>
        /// <param name="ids">ids of data items to be retrieved</param>
        /// <returns>a data list of results by ids.</returns>
        public virtual async Task<List<T>> GetListByIdsAsync(string correlationId, K[] ids)
        {
            
            var documentSerializer = BsonSerializer.SerializerRegistry.GetSerializer<T>();
            var builder = Builders<T>.Filter;
            var filterDefinition = builder.In(x => x.Id, ids);
            var renderedFilter = filterDefinition.Render(documentSerializer, BsonSerializer.SerializerRegistry);

            var query = _collection.Find(renderedFilter);
            var items = await query.ToListAsync();

            _logger.Trace(correlationId, $"Retrieved {items.Count} from {_collection}");

            return items;
        }

        /// <summary>
        /// Gets a data item by its unique id.
        /// </summary>
        /// <param name="correlationId">(optional) transaction id to trace execution through call chain.</param>
        /// <param name="id">an id of data item to be retrieved.</param>
        /// <returns>a data item by id.</returns>
        public virtual async Task<T> GetOneByIdAsync(string correlationId, K id)
        {
            var builder = Builders<T>.Filter;
            var filter = builder.Eq(x => x.Id, id);
            var result = await _collection.Find(filter).FirstOrDefaultAsync();

            if (result == null)
            {
                _logger.Trace(correlationId, "Nothing found from {0} with id = {1}", _collectionName, id);
                return default(T);
            }

            _logger.Trace(correlationId, "Retrieved from {0} with id = {1}", _collectionName, id);

            return result;
        }

        /// <summary>
        /// Gets a data item by its unique id.
        /// </summary>
        /// <param name="correlationId">(optional) transaction id to trace execution through call chain.</param>
        /// <param name="id">an id of data item to be retrieved.</param>
        /// <param name="projection">(optional) projection parameters.</param>
        /// <returns>a data item by id.</returns>
        public virtual async Task<object> GetOneByIdAsync(string correlationId, K id, ProjectionParams projection)
        {
            var builder = Builders<T>.Filter;
            var filter = builder.Eq(x => x.Id, id);

            var projectionBuilder = Builders<T>.Projection;
            var projectionDefinition = CreateProjectionDefinition(projection, projectionBuilder);

            var result = await _collection.Find(filter).Project(projectionDefinition).FirstOrDefaultAsync();

            if (result == null)
            {
                _logger.Trace(correlationId, "Nothing found from {0} with id = {1} and projection fields '{2}'", _collectionName, id, StringConverter.ToString(projection));
                return null;
            }

            if (result.ElementCount == 0)
            {
                _logger.Trace(correlationId, "Retrieved from {0} with id = {1}, but projection is not valid '{2}'", _collectionName, id, StringConverter.ToString(projection));
                return null;
            }

            _logger.Trace(correlationId, "Retrieved from {0} with id = {1} and projection fields '{2}'", _collectionName, id, StringConverter.ToString(projection));

            return BsonSerializer.Deserialize<object>(result);
        }

        /// <summary>
        /// Gets a random item from items that match to a given filter.
        /// 
        /// This method shall be called by a public getOneRandom method from child class
        /// that receives FilterParams and converts them into a filter function.
        /// </summary>
        /// <param name="correlationId">(optional) transaction id to trace execution through call chain.</param>
        /// <param name="filterDefinition">(optional) a filter JSON object</param>
        /// <returns>a random item by filter.</returns>
        public virtual async Task<T> GetOneRandomAsync(string correlationId, FilterDefinition<T> filterDefinition)
        {
            var documentSerializer = BsonSerializer.SerializerRegistry.GetSerializer<T>();
            var renderedFilter = filterDefinition.Render(documentSerializer, BsonSerializer.SerializerRegistry);

            var count = (int)_collection.CountDocuments(renderedFilter);

            if (count <= 0)
            {
                _logger.Trace(correlationId, "Nothing found for filter {0}", renderedFilter.ToString());
                return default(T);
            }

            var randomIndex = new Random().Next(0, count - 1);

            var result = await _collection.Find(filterDefinition).Skip(randomIndex).FirstOrDefaultAsync();

            _logger.Trace(correlationId, "Retrieved randomly from {0} with id = {1}", _collectionName, result.Id);

            return result;
        }

        /// <summary>
        /// Creates a data item.
        /// </summary>
        /// <param name="correlationId">(optional) transaction id to trace execution through call chain.</param>
        /// <param name="item">an item to be created.</param>
        /// <returns>created item.</returns>
        public virtual async Task<T> CreateAsync(string correlationId, T item)
        {
            var identifiable = item as IStringIdentifiable;
            if (identifiable != null && item.Id == null)
                ObjectWriter.SetProperty(item, nameof(item.Id), IdGenerator.NextLong());

            await _collection.InsertOneAsync(item, null);

            _logger.Trace(correlationId, "Created in {0} with id = {1}", _collectionName, item.Id);

            return item;
        }

        /// <summary>
        /// Sets a data item. If the data item exists it updates it, otherwise it create a new data item.
        /// </summary>
        /// <param name="correlationId">(optional) transaction id to trace execution through call chain.</param>
        /// <param name="item">an item to be set.</param>
        /// <returns>updated item.</returns>
        public virtual async Task<T> SetAsync(string correlationId, T item)
        {
            var identifiable = item as IIdentifiable<K>;
            if (identifiable == null || item.Id == null)
                return default(T);

            var filter = Builders<T>.Filter.Eq(x => x.Id, identifiable.Id);
            var options = new FindOneAndReplaceOptions<T>
            {
                ReturnDocument = ReturnDocument.After,
                IsUpsert = true
            };
            var result = await _collection.FindOneAndReplaceAsync(filter, item, options);

            _logger.Trace(correlationId, "Set in {0} with id = {1}", _collectionName, item.Id);

            return result;
        }

        /// <summary>
        /// Updates a data item.
        /// </summary>
        /// <param name="correlationId">(optional) transaction id to trace execution through call chain.</param>
        /// <param name="item">an item to be updated.</param>
        /// <returns>updated item.</returns>
        public virtual async Task<T> UpdateAsync(string correlationId, T item)
        {
            var identifiable = item as IIdentifiable<K>;
            if (identifiable == null || item.Id == null)
                return default(T);

            var filter = Builders<T>.Filter.Eq(x => x.Id, identifiable.Id);
            var options = new FindOneAndReplaceOptions<T>
            {
                ReturnDocument = ReturnDocument.After,
                IsUpsert = false
            };
            var result = await _collection.FindOneAndReplaceAsync(filter, item, options);

            _logger.Trace(correlationId, "Update in {0} with id = {1}", _collectionName, item.Id);

            return result;
        }

        public virtual async Task<T> ModifyAsync(string correlationId,
            FilterDefinition<T> filterDefinition, UpdateDefinition<T> updateDefinition)
        {
            if (filterDefinition == null || updateDefinition == null)
            {
                return default(T);
            }

            var options = new FindOneAndUpdateOptions<T>
            {
                ReturnDocument = ReturnDocument.After,
                IsUpsert = false
            };

            var result = await _collection.FindOneAndUpdateAsync(filterDefinition, updateDefinition, options);

            _logger.Trace(correlationId, "Modify in {0}", _collectionName);

            return result;
        }

        public virtual async Task<T> ModifyByIdAsync(string correlationId, K id, UpdateDefinition<T> updateDefinition)
        {
            if (id == null || updateDefinition == null)
            {
                return default(T);
            }

            var result = await ModifyAsync(correlationId, Builders<T>.Filter.Eq(x => x.Id, id), updateDefinition);

            _logger.Trace(correlationId, "Modify in {0} with id = {1}", _collectionName, id);

            return result;
        }

        /// <summary>
        /// Deleted a data item by it's unique id.
        /// </summary>
        /// <param name="correlationId">(optional) transaction id to trace execution through call chain.</param>
        /// <param name="id">an id of the item to be deleted</param>
        /// <returns>deleted item.</returns>
        public virtual async Task<T> DeleteByIdAsync(string correlationId, K id)
        {
            var filter = Builders<T>.Filter.Eq(x => x.Id, id);
            var options = new FindOneAndDeleteOptions<T>();
            var result = await _collection.FindOneAndDeleteAsync(filter, options);

            _logger.Trace(correlationId, "Deleted from {0} with id = {1}", _collectionName, id);

            return result;
        }

        /// <summary>
        /// Deletes data items that match to a given filter.
        /// 
        /// This method shall be called by a public deleteByFilter method from child
        /// class that receives FilterParams and converts them into a filter function.
        /// </summary>
        /// <param name="correlationId">(optional) transaction id to trace execution through call chain.</param>
        /// <param name="filterDefinition">(optional) a filter JSON object.</param>
        public virtual async Task DeleteByFilterAsync(string correlationId, FilterDefinition<T> filterDefinition)
        {
            var result = await _collection.DeleteManyAsync(filterDefinition);

            _logger.Trace(correlationId, $"Deleted {result.DeletedCount} from {_collection}");
        }

        /// <summary>
        /// Deletes multiple data items by their unique ids.
        /// </summary>
        /// <param name="correlationId">(optional) transaction id to trace execution through call chain.</param>
        /// <param name="ids">ids of data items to be deleted.</param>
        public virtual async Task DeleteByIdsAsync(string correlationId, K[] ids)
        {
            var filterDefinition = Builders<T>.Filter.In(x => x.Id, ids);

            var result = await _collection.DeleteManyAsync(filterDefinition);

            _logger.Trace(correlationId, $"Deleted {result.DeletedCount} from {_collection}");
        }

        #region Overridable Compose Methods

        protected virtual FilterDefinition<T> ComposeFilter(FilterParams filterParams)
        {
            filterParams = filterParams ?? new FilterParams();

            var builder = Builders<T>.Filter;
            var filter = builder.Empty;

            foreach (var filterKey in filterParams.Keys)
            {
                filter &= builder.Eq(filterKey, filterParams[filterKey]);
            }

            return filter;
        }

        protected virtual UpdateDefinition<T> ComposeUpdate(AnyValueMap updateMap)
        {
            updateMap = updateMap ?? new AnyValueMap();

            var builder = Builders<T>.Update;
            var updateDefinitions = new List<UpdateDefinition<T>>();

            foreach (var key in updateMap.Keys)
            {
                updateDefinitions.Add(builder.Set(key, updateMap[key]));
            }

            return builder.Combine(updateDefinitions);
        }

        protected virtual SortDefinition<T> ComposeSort(SortParams sortParams)
        {
            sortParams = sortParams ?? new SortParams();

            var builder = Builders<T>.Sort;

            return builder.Combine(sortParams.Select(field => field.Ascending ?
                builder.Ascending(field.Name) : builder.Descending(field.Name)));
        }

        protected virtual ProjectionDefinition<T> CreateProjectionDefinition(
            ProjectionParams projection, ProjectionDefinitionBuilder<T> projectionBuilder)
        {
            projection = projection ?? new ProjectionParams();

            return projectionBuilder.Combine(
                projection.Select(field => projectionBuilder.Include(field))
            ).Exclude(InternalIdFieldName);
        }

        #endregion

    }
}