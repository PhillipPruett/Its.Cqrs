// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Its.Domain.Serialization;

namespace Microsoft.Its.Domain.Sql
{
    public static class DatabaseExtensions
    {
        [Obsolete]
        public static void Unique<TProjection>(
            this DbContext context,
            Expression<Func<TProjection, object>> member,
            string schema = "dbo")
            where TProjection : class
        {
            context.Database.ExecuteSqlCommand(AddUniqueIndex(context, member, schema));
        }

        internal static string AddUniqueIndex<TProjection>(
            this DbContext context,
            Expression<Func<TProjection, object>> member,
            string schema = "dbo")
            where TProjection : class =>
                string.Format("CREATE UNIQUE INDEX IX_{0}_{1} ON {2}.{0} ({1})",
                              context.TableNameFor<TProjection>(),
                              member.MemberName(),
                              schema);

        [Obsolete]
        public static void Unique<TProjection>(
            this DbContext context,
            Expression<Func<TProjection, object>> member1,
            Expression<Func<TProjection, object>> member2,
            string schema = "dbo") where TProjection : class =>
                context.Database.ExecuteSqlCommand(
                    string.Format("CREATE UNIQUE INDEX IX_{0}_{1}_{2} ON {3}.{0} ({1}, {2})",
                                  context.TableNameFor<TProjection>(),
                                  member1.MemberName(),
                                  member2.MemberName(),
                                  schema));

        /// <summary>
        /// Seeds an event store using JSON-serialized events stored in a file.
        /// </summary>
        public static void SeedFromFile(this EventStoreDbContext context, FileInfo file)
        {
            using (var stream = file.OpenRead())
            using (var reader = new StreamReader(stream))
            {
                var json = reader.ReadToEnd();
                var events = Serializer.FromJsonToEvents(json).ToArray();

                foreach (var e in events)
                {
                    context.Events.Add(e.ToStorableEvent());

                    // it's necessary to save at every event to preserve ordering, otherwise EF will reorder them
                    context.SaveChanges();
                }
            }
        }

        private static string TableNameFor<T>(this DbContext context) where T : class
        {
            var objectContext = ((IObjectContextAdapter) context).ObjectContext;

            var sql = objectContext.CreateObjectSet<T>().ToTraceString();

            var match = new Regex(@"FROM ([\[\]a-z0-9]*\.)?(?<table>.*) AS", RegexOptions.IgnoreCase)
                .Match(sql);

            var matched = match.Groups["table"].Value;

            return Regex.Replace(matched, @"[\[\]\.]", "");
        }

        /// <summary>
        /// Opens the connection.
        /// </summary>
        internal static IDbConnection OpenConnection(this DbContext context)
        {
            var connection = context.Database.Connection;
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }
            return connection;
        }

        /// <summary>
        /// Determines whether the specified DbContext is configured to use an Azure SQL Database.
        /// </summary>
        /// <param name="context">The database context.</param>
        public static bool IsAzureDatabase(this DbContext context)
        {
            return context.Database.Connection.IsAzureDatabase();
        }

        /// <summary>
        /// Determines whether the specified connection is configured to use an Azure SQL Database.
        /// </summary>
        public static bool IsAzureDatabase(this IDbConnection connection)
        {
            return connection.ConnectionString.Contains("database.windows.net");
        }

        /// <summary>
        /// Creates an Azure database.
        /// </summary>
        /// <param name="context">The DbContext</param>
        /// <param name="dbSizeInGB">Size of database in GB</param>
        /// <param name="edition">Edition of database</param>
        /// <param name="serviceObjective">Service objective of database</param>
        /// <param name="connectionString">Optional connection string to use instead of taking it off of <paramref name="context"/></param>
        /// <exception cref="System.ArgumentException">Not Azure database based on ConnectionString</exception>
        /// <remarks>
        /// See https://msdn.microsoft.com/en-us/library/dn268335.aspx for more info.
        /// MAXSIZE = ( [ 100 MB | 500 MB ] | [ { 1 | 5 | 10 | 20 | 30 � 150�500 } GB  ] )
        /// | EDITION = { 'web' | 'business' | 'basic' | 'standard' | 'premium' }
        /// | SERVICE_OBJECTIVE = { 'shared' | 'basic' | 'S0' | 'S1' | 'S2' | 'P1' | 'P2' | 'P3' }
        /// </remarks>
        internal static void CreateAzureDatabase(
            this DbContext context, 
            int dbSizeInGB = 2, 
            string edition = "standard", 
            string serviceObjective = "S0",
            string connectionString = null)
        {
            if (!context.IsAzureDatabase())
            {
                throw new ArgumentException("Not Azure database based on ConnectionString");
            }

            var connstrBldr = new SqlConnectionStringBuilder(connectionString ?? context.Database.Connection.ConnectionString)
            {
                InitialCatalog = "master"
            };

            var databaseName = context.Database.Connection.Database;
            var dbCreationCmd = $"CREATE DATABASE [{databaseName}] (MAXSIZE={dbSizeInGB}GB, EDITION='{edition}', SERVICE_OBJECTIVE='{serviceObjective}')";

            // With Azure SQL db V12, database creation TSQL became a sync process. 
            // So we need a 10 minutes command timeout
            ExecuteNonQuery(connstrBldr.ConnectionString, dbCreationCmd, commandTimeout: 600);
            context.WaitUntilDatabaseIsCreated(false);
        }

        public static void CreateReadonlyUser(this DbContext context, DbReadonlyUser readonlyUser)
        {
            var createUserCmd = $"CREATE USER [{readonlyUser.UserName}] FOR LOGIN [{readonlyUser.LoginName}]";
            ExecuteNonQuery(context.Database.Connection.ConnectionString, createUserCmd);

            var addRoleToUserCmd = $"EXEC sp_addrolemember N'db_datareader', N'{readonlyUser.UserName}'";
            ExecuteNonQuery(context.Database.Connection.ConnectionString, addRoleToUserCmd);
        }

        internal static void WaitUntilDatabaseIsCreated(this DbContext context, bool forceInitialize)
        {
            // wait up to 60 seconds
            var retryCount = 30;

            while (retryCount-- > 0)
            {
                try
                {
                    if (context.Database.Exists())
                    {
                        if (forceInitialize)
                        {
                            context.Database.Initialize(force: true);
                        }
                        return;
                    }
                }
                catch
                {
                    if (retryCount <= 0)
                    {
                        throw;
                    }
                }
                if (retryCount <= 0)
                {
                    throw new TimeoutException("Database is not ONLINE after 60 seconds");
                }

                Thread.Sleep(TimeSpan.FromSeconds(2));
            }
        }

        public static IEnumerable<IEnumerable<dynamic>> QueryDynamic(
            this DbContext context,
            string sql,
            IDictionary<string, object> parameters = null) =>
                context.OpenConnection().QueryDynamic(sql, parameters).ToArray();

        private static void ExecuteNonQuery(string connString, string commandText, int commandTimeout = 60)
        {
            using (var conn = new SqlConnection(connString))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandTimeout = commandTimeout;
                cmd.CommandText = commandText;
                cmd.ExecuteNonQuery();
            }
        }

        public static IEnumerable<IEnumerable<dynamic>> QueryDynamic(
            this IDbConnection connection,
            string sql,
            IDictionary<string, object> parameters = null)
        {
            using (var command = connection.PrepareCommand(sql, parameters))
            {
                return command.ExecuteQueriesToDynamic();
            }
        }

        public static void Execute(
            this IDbConnection connection,
            string sql,
            IDictionary<string, object> parameters = null)
        {
            using (var command = connection.PrepareCommand(sql, parameters))
            {
                command.ExecuteNonQuery();
            }
        }

        public static IDbCommand PrepareCommand(
            this IDbConnection connection,
            string sql,
            IDictionary<string, object> parameters = null)
        {
            var command = connection.CreateCommand();
            {
                command.CommandType = CommandType.Text;
                command.CommandText = sql;

                if (parameters != null)
                {
                    foreach (var pair in parameters)
                    {
                        var parameter = command.CreateParameter();
                        parameter.ParameterName = pair.Key;
                        parameter.Value = pair.Value ?? DBNull.Value;
                        command.Parameters.Add(parameter);
                    }
                }

                return command;
            }
        }

        public static IEnumerable<IEnumerable<dynamic>> ExecuteQueriesToDynamic(
            this IDbCommand command)
        {
            using (var reader = command.ExecuteReader())
            {
                do
                {
                    var values = new object[reader.FieldCount];
                    var names = Enumerable.Range(0, reader.FieldCount)
                                          .Select(reader.GetName)
                                          .ToArray();

                    var currentResult = new List<ExpandoObject>();

                    while (reader.Read())
                    {
                        reader.GetValues(values);
                        var expando = new ExpandoObject();
                        for (var i = 0; i < values.Length; i++)
                        {
                            expando.TryAdd(names[i], values[i]);
                        }
                        currentResult.Add(expando);
                    }

                    yield return currentResult;
                } while (reader.NextResult());
            }
        }
    }
}