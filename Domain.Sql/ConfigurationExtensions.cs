// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Microsoft.Its.Domain.Sql.CommandScheduler;
using Microsoft.Its.Recipes;
using Pocket;

namespace Microsoft.Its.Domain.Sql
{
    /// <summary>
    /// Provides methods for preparing the domain configuration. 
    /// </summary>
    public static class ConfigurationExtensions
    {
        public static SqlCommandScheduler SqlCommandScheduler(this Configuration configuration)
        {
            if (configuration.IsUsingCommandSchedulerPipeline())
            {
                throw new InvalidOperationException("You must first call UseSqlCommandScheduling to enable the use of the legacy SqlCommandScheduler.");
            }
            return configuration.Container.Resolve<SqlCommandScheduler>();
        }

        public static ISchedulerClockRepository SchedulerClockRepository(this Configuration configuration)
        {
            return configuration.Container.Resolve<ISchedulerClockRepository>();
        }

        public static ISchedulerClockTrigger SchedulerClockTrigger(this Configuration configuration)
        {
            return configuration.Container.Resolve<ISchedulerClockTrigger>();
        }

        /// <summary>
        /// Configures the system to use a SQL-based event store.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <param name="createEventStoreDbContext">A function that returns an <see cref="EventStoreDbContext" />, if something different from the default is desired.</param>
        /// <returns>The updated configuration.</returns>
        public static Configuration UseSqlEventStore(
            this Configuration configuration,
            Func<EventStoreDbContext> createEventStoreDbContext = null)
        {
            configuration.Container.AddStrategy(SqlEventSourcedRepositoryStrategy);
            configuration.IsUsingSqlEventStore(true);

            createEventStoreDbContext = createEventStoreDbContext ??
                                        (() => new EventStoreDbContext());

            configuration.Container
                         .Register(c => createEventStoreDbContext())
                         .Register<ICommandPreconditionVerifier>(c => c.Resolve<CommandPreconditionVerifier>());

            return configuration;
        }

        /// <summary>
        /// Configures the system to use SQL-backed command scheduling.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <returns>The updated configuration.</returns>
        [Obsolete("Please move to UseSqlStorageForScheduledCommands.")]
        public static Configuration UseSqlCommandScheduling(
            this Configuration configuration,
            Action<ReadModelCatchup<CommandSchedulerDbContext>> configureCatchup = null)
        {
            var container = configuration.Container;

            container.RegisterDefaultClockName();

            var scheduler = new SqlCommandScheduler(
                configuration,
                container.Resolve<Func<CommandSchedulerDbContext>>(),
                container.Resolve<GetClockName>());

            if (container.All(r => r.Key != typeof (SqlCommandScheduler)))
            {
                container.Register(c => scheduler)
                         .Register<ISchedulerClockTrigger>(c => scheduler)
                         .Register<ISchedulerClockRepository>(c => scheduler);
            }

            var subscription = container.Resolve<IEventBus>().Subscribe(scheduler);
            configuration.RegisterForDisposal(subscription);
            container.RegisterSingle(c => scheduler);

            if (configureCatchup != null)
            {
                var catchup = new ReadModelCatchup<CommandSchedulerDbContext>(scheduler)
                {
                    CreateReadModelDbContext = scheduler.CreateCommandSchedulerDbContext
                };
                configureCatchup(catchup);
                catchup.PollEventStore();
                container.RegisterSingle(c => catchup);
                configuration.RegisterForDisposal(catchup);
            }

            configuration.IsUsingCommandSchedulerPipeline(false);

            return configuration;
        }

        public static Configuration UseSqlStorageForScheduledCommands(
            this Configuration configuration)
        {
            var container = configuration.Container;

            container.RegisterDefaultClockName()
                     .Register<ISchedulerClockRepository>(
                         c => c.Resolve<SchedulerClockRepository>())
                     .Register<ICommandPreconditionVerifier>(
                         c => c.Resolve<CommandPreconditionVerifier>())
                     .Register<ISchedulerClockTrigger>(
                         c => c.Resolve<SchedulerClockTrigger>());

            configuration.Container
                         .Resolve<SqlCommandSchedulerPipelineInitializer>()
                         .Initialize(configuration);

            var commandSchedulerResolver = new CommandSchedulerResolver(container);

            container
                .Register(
                    c => new SchedulerClockTrigger(
                        c.Resolve<CommandSchedulerDbContext>,
                        async (serializedCommand, result, db) =>
                        {
                            await DeserializeAndDeliver(commandSchedulerResolver, serializedCommand, db);

                            result.Add(serializedCommand.Result);
                        }))
                .Register<ISchedulerClockTrigger>(c => c.Resolve<SchedulerClockTrigger>());

            return configuration;
        }

        private static async Task DeserializeAndDeliver(
            CommandSchedulerResolver schedulerResolver,
            CommandScheduler.ScheduledCommand serializedCommand,
            CommandSchedulerDbContext db)
        {
            dynamic scheduler = schedulerResolver.ResolveSchedulerForAggregateTypeNamed(serializedCommand.AggregateType);

            await Storage.DeserializeAndDeliverScheduledCommand(
                serializedCommand,
                scheduler);

            serializedCommand.Attempts++;

            await db.SaveChangesAsync();
        }
        
        public static async Task DeserializeAndDeliver(
            this Configuration configuration,
            CommandScheduler.ScheduledCommand serializedCommand,
            CommandSchedulerDbContext db)
        {
            var resolver = configuration.Container.Resolve<CommandSchedulerResolver>();
            await DeserializeAndDeliver(resolver, serializedCommand, db);
        }

        internal static void IsUsingSqlEventStore(this Configuration configuration, bool value)
        {
            configuration.Properties["IsUsingSqlEventStore"] = value;
        }

        internal static bool IsUsingSqlEventStore(this Configuration configuration)
        {
            return configuration.Properties
                                .IfContains("IsUsingSqlEventStore")
                                .And()
                                .IfTypeIs<bool>()
                                .ElseDefault();
        }

        internal static Func<PocketContainer, object> SqlEventSourcedRepositoryStrategy(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof (IEventSourcedRepository<>))
            {
                var aggregateType = type.GenericTypeArguments.Single();
                var genericType = typeof (SqlEventSourcedRepository<>).MakeGenericType(aggregateType);
                return c => c.Resolve(genericType);
            }
            return null;
        }

        internal static ICommandSchedulerDispatcher[] InitializeSchedulersPerAggregateType(
            PocketContainer container,
            GetClockName getClockName,
            ISubject<ICommandSchedulerActivity> subject)
        {
            var binders = AggregateType.KnownTypes
                                       .Select(aggregateType =>
                                       {
                                           var initializerType =
                                               typeof (SchedulerInitializer<>).MakeGenericType(aggregateType);

                                           dynamic initializer = container.Resolve(initializerType);

                                           return (ICommandSchedulerDispatcher) initializer.InitializeScheduler(
                                               subject,
                                               container,
                                               getClockName);
                                       })
                                       .ToArray();
            return binders;
        }

        internal static PocketContainer RegisterDefaultClockName(this PocketContainer container)
        {
            return container.AddStrategy(t =>
            {
                if (t == typeof (GetClockName))
                {
                    return c => new GetClockName(e => CommandScheduler.SqlCommandScheduler.DefaultClockName);
                }

                return null;
            });
        }
    }
}