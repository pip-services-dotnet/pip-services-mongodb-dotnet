﻿using System;
using System.Threading.Tasks;
using MongoDB.Driver;
using PipServices.Commons.Config;
using PipServices.Commons.Errors;
using PipServices.Commons.Refer;
using PipServices.Commons.Run;
using PipServices.Components.Auth;
using PipServices.Components.Connect;
using PipServices.Components.Log;

namespace PipServices.MongoDb.Persistence
{
    /// <summary>
    /// Abstract persistence component that stores data in MongoDB
    /// and is based using Mongoose object relational mapping.
    /// 
    /// This is the most basic persistence component that is only
    /// able to store data items of any type.Specific CRUD operations 
    /// over the data items must be implemented in child classes by 
    /// accessing this._collection or this._model properties.
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
    /// <typeparam name="T">the class type</typeparam>
    /// <example>
    /// <code>
    /// class MyMongoDbPersistence: MongoDbPersistence<MyData> 
    /// {
    ///     public MyMongoDbPersistence()
    ///     {
    ///         base("mydata");
    ///     }
    ///     public MyData getByName(string correlationId, string name)
    ///     {
    ///         var builder = Builders<BeaconV1>.Filter;
    ///         var filter = builder.Eq(x => x.Name, name);
    ///         var result = await _collection.Find(filter).FirstOrDefaultAsync();
    ///         return result;
    ///     }
    ///     public MyData set(String correlatonId, MyData item)
    ///     {
    ///         var filter = Builders<T>.Filter.Eq(x => x.Id, item.Id);
    ///         var options = new FindOneAndReplaceOptions<T>
    ///         {
    ///             ReturnDocument = ReturnDocument.After,
    ///             IsUpsert = true
    ///         };
    ///         var result = await _collection.FindOneAndReplaceAsync(filter, item, options);
    ///         return result;
    ///     }
    /// }
    /// 
    /// var persistence = new MyMongoDbPersistence();
    /// persistence.Configure(ConfigParams.fromTuples(
    /// "host", "localhost",
    /// "port", 27017 ));
    /// 
    /// persitence.Open("123");
    /// var mydata = new MyData("ABC");
    /// persistence.Set("123", mydata);
    /// persistence.GetByName("123", "ABC");
    /// Console.Out.WriteLine(item);                   // Result: { name: "ABC" }
    /// </code>
    /// </example>
    public class MongoDbPersistence<T> : IReferenceable, IReconfigurable, IOpenable, ICleanable
    {
        private ConfigParams _defaultConfig = ConfigParams.FromTuples(
            //"connection.type", "mongodb",
            //"connection.database", "test",
            //"connection.host", "localhost",
            //"connection.port", 27017,

            "options.poll_size", 4,
            "options.keep_alive", 1,
            "options.connect_timeout", 5000,
            "options.auto_reconnect", true,
            "options.max_page_size", 100,
            "options.debug", true
        );

        /// <summary>
        /// The MongoDB colleciton name.
        /// </summary>
        protected string _collectionName;
        /// <summary>
        /// The connection resolver.
        /// </summary>
        protected ConnectionResolver _connectionResolver = new ConnectionResolver();
        /// <summary>
        /// The credential resolver.
        /// </summary>
        protected CredentialResolver _credentialResolver = new CredentialResolver();
        /// <summary>
        /// The configuration options.
        /// </summary>
        protected ConfigParams _options = new ConfigParams();
        
        /// <summary>
        /// The MongoDB connection object.
        /// </summary>
        protected MongoClient _connection;
        /// <summary>
        /// The MongoDB database.
        /// </summary>
        protected IMongoDatabase _database;
        /// <summary>
        /// The MongoDB colleciton object.
        /// </summary>
        protected IMongoCollection<T> _collection;

        /// <summary>
        /// The logger.
        /// </summary>
        protected CompositeLogger _logger = new CompositeLogger();

        /// <summary>
        /// Creates a new instance of the persistence component.
        /// </summary>
        /// <param name="collectionName">(optional) a collection name.</param>
        public MongoDbPersistence(string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
                throw new ArgumentNullException(nameof(collectionName));

            _collectionName = collectionName;
        }

        /// <summary>
        /// Sets references to dependent components.
        /// </summary>
        /// <param name="references">references to locate the component dependencies.</param>
        public virtual void SetReferences(IReferences references)
        {
            _logger.SetReferences(references);
            _connectionResolver.SetReferences(references);
            _credentialResolver.SetReferences(references);
        }

        /// <summary>
        /// Configures component by passing configuration parameters.
        /// </summary>
        /// <param name="config">configuration parameters to be set.</param>
        public virtual void Configure(ConfigParams config)
        {
            config = config.SetDefaults(_defaultConfig);

            _connectionResolver.Configure(config, true);
            _credentialResolver.Configure(config, true);

            _collectionName = config.GetAsStringWithDefault("collection", _collectionName);

            _options = _options.Override(config.GetSection("options"));
        }

        /// <summary>
        /// Checks if the component is opened.
        /// </summary>
        /// <returns>true if the component has been opened and false otherwise.</returns>
        public virtual bool IsOpen()
        {
            return _collection != null;
        }

        /// <summary>
        /// Opens the component.
        /// </summary>
        /// <param name="correlationId">(optional) transaction id to trace execution through call chain.</param>
        public async virtual Task OpenAsync(string correlationId)
        {
            var connection = await _connectionResolver.ResolveAsync(correlationId);
            var credential = await _credentialResolver.LookupAsync(correlationId);
            await OpenAsync(correlationId, connection, credential);
        }

        /// <summary>
        /// Opens the component.
        /// </summary>
        /// <param name="correlationId">(optional) transaction id to trace execution through call chain.</param>
        /// <param name="connection">connection parameters.</param>
        /// <param name="credential">credential parameters.</param>
        /// <returns></returns>
        public async Task OpenAsync(string correlationId, ConnectionParams connection, CredentialParams credential)
        {
            if (connection == null)
                throw new ConfigException(correlationId, "NO_CONNECTION", "Database connection is not set");

            var uri = connection.Uri;
            var host = connection.Host;
            var port = connection.Port;
            var databaseName = connection.GetAsNullableString("database");

            if (uri != null)
            {
                databaseName = MongoUrl.Create(uri).DatabaseName;
            }
            else
            {
                if (host == null)
                    throw new ConfigException(correlationId, "NO_HOST", "Connection host is not set");

                if (port == 0)
                    throw new ConfigException(correlationId, "NO_PORT", "Connection port is not set");

                if (databaseName == null)
                    throw new ConfigException(correlationId, "NO_DATABASE", "Connection database is not set");
            }

            _logger.Trace(correlationId, "Connecting to mongodb database {0}, collection {1}", databaseName, _collectionName);

            try
            {
                if (uri != null)
                {
                    _connection = new MongoClient(uri);
                }
                else
                {
                    var settings = new MongoClientSettings
                    {
                        Server = new MongoServerAddress(host, port),
                        MaxConnectionPoolSize = _options.GetAsInteger("poll_size"),
                        ConnectTimeout = _options.GetAsTimeSpan("connect_timeout"),
                        //SocketTimeout =
                        //    new TimeSpan(options.GetInteger("server.socketOptions.socketTimeoutMS")*
                        //                 TimeSpan.TicksPerMillisecond)
                    };

                    if (credential.Username != null)
                    {
                        settings.Credential = MongoCredential.CreateCredential(databaseName, credential.Username, credential.Password);
                    }

                    _connection = new MongoClient(settings);
                }

                _database = _connection.GetDatabase(databaseName);
                _collection = _database.GetCollection<T>(_collectionName);

                _logger.Debug(correlationId, "Connected to mongodb database {0}, collection {1}", databaseName, _collectionName);
            }
            catch (Exception ex)
            {
                throw new ConnectionException(correlationId, "ConnectFailed", "Connection to mongodb failed", ex);
            }

            await Task.Delay(0);
        }

        /// <summary>
        /// Closes component and frees used resources.
        /// </summary>
        /// <param name="correlationId">(optional) transaction id to trace execution through call chain.</param>
        public virtual async Task CloseAsync(string correlationId)
        {
            await Task.Delay(0);
        }

        /// <summary>
        /// Clears component state.
        /// </summary>
        /// <param name="correlationId">(optional) transaction id to trace execution through call chain.</param>
        public virtual async Task ClearAsync(string correlationId)
        {
            await _database.DropCollectionAsync(_collectionName);
        }
    }
}