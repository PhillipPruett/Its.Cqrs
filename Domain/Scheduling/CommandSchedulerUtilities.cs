// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Its.Recipes;

namespace Microsoft.Its.Domain
{
    public static class CommandSchedulerUtilities
    {
        internal static ICommandScheduler<TAggregate> Wrap<TAggregate>(
            this ICommandScheduler<TAggregate> scheduler,
            ScheduledCommandInterceptor<TAggregate> schedule = null,
            ScheduledCommandInterceptor<TAggregate> deliver = null)
            where TAggregate : IEventSourced
        {
            schedule = schedule ?? (async (c, next) => await next(c));
            deliver = deliver ?? (async (c, next) => await next(c));

            return Create<TAggregate>(
                async command => await schedule(command, async c => await scheduler.Schedule(c)),
                async command => await deliver(command, async c => await scheduler.Deliver(c)));
        }

        private static ICommandScheduler<TAggregate> Create<TAggregate>(
            Func<IScheduledCommand<TAggregate>, Task> schedule,
            Func<IScheduledCommand<TAggregate>, Task> deliver)
            where TAggregate : IEventSourced
        {
            return new AnonymousCommandScheduler<TAggregate>(
                schedule,
                deliver);
        }

        internal static ScheduledCommandInterceptor<TAggregate> Compose<TAggregate>(
            this IEnumerable<ScheduledCommandInterceptor<TAggregate>> pipeline)
            where TAggregate : IEventSourced
        {
            var delegates = pipeline.OrEmpty().ToArray();

            if (!delegates.Any())
            {
                return null;
            }

            return delegates.Aggregate(
                (first, second) =>
                    (async (command, next) =>
                    await first(command,
                                async c => await second(c,
                                                        async cc => await next(cc)))));
        }

        internal static async Task DeliverImmediatelyOnConfiguredScheduler<TAggregate>(
            IScheduledCommand<TAggregate> command,
            Configuration configuration)
            where TAggregate : class, IEventSourced
        {
            var scheduler = configuration.CommandScheduler<TAggregate>();
            await scheduler.Deliver(command);
        }

        internal static void DeliverIfPreconditionIsSatisfiedSoon<TAggregate>(
            IScheduledCommand<TAggregate> scheduledCommand,
            Configuration configuration,
            int timeoutInMilliseconds = 10000)
            where TAggregate : class, IEventSourced
        {
            var eventBus = configuration.EventBus;

            var timeout = TimeSpan.FromMilliseconds(timeoutInMilliseconds);

            eventBus.Events<IEvent>()
                    .Where(
                        e => e.AggregateId == scheduledCommand.DeliveryPrecondition.AggregateId &&
                             e.ETag == scheduledCommand.DeliveryPrecondition.ETag)
                    .Take(1)
                    .Timeout(timeout)
                    .Subscribe(
                        e =>
                        {
                            Task.Run(() => DeliverImmediatelyOnConfiguredScheduler(scheduledCommand, configuration)).Wait();
                        },
                        onError: ex =>
                        {
                            eventBus.PublishErrorAsync(new EventHandlingError(ex));
                        });
        }

        internal static IScheduledCommand<TAggregate> CreateScheduledCommand<TCommand, TAggregate>(
            Guid aggregateId,
            TCommand command,
            DateTimeOffset? dueTime,
            IEvent deliveryDependsOn = null)
            where TCommand : ICommand<TAggregate> where TAggregate : IEventSourced
        {
            CommandPrecondition precondition = null;

            if (deliveryDependsOn != null)
            {
                if (deliveryDependsOn.AggregateId == Guid.Empty)
                {
                    throw new ArgumentException("An AggregateId must be set on the event on which the scheduled command depends.");
                }

                if (String.IsNullOrWhiteSpace(deliveryDependsOn.ETag))
                {
                    deliveryDependsOn.IfTypeIs<Event>()
                                     .ThenDo(e => e.ETag = Guid.NewGuid().ToString("N"))
                                     .ElseDo(() =>
                                     {
                                         throw new ArgumentException("An ETag must be set on the event on which the scheduled command depends.");
                                     });
                }

                precondition = new CommandPrecondition
                {
                    AggregateId = deliveryDependsOn.AggregateId,
                    ETag = deliveryDependsOn.ETag
                };
            }

            if (String.IsNullOrEmpty(command.ETag))
            {
                command.IfTypeIs<Command>()
                       .ThenDo(c => c.ETag = CommandContext.Current
                                                           .IfNotNull()
                                                           .Then(ctx => ctx.NextETag(aggregateId.ToString("N")))
                                                           .Else(() => Guid.NewGuid().ToString("N")));
            }

            var scheduledCommand = new CommandScheduled<TAggregate>
            {
                Command = command,
                DueTime = dueTime,
                AggregateId = aggregateId,
                SequenceNumber = -DateTimeOffset.UtcNow.Ticks,
                DeliveryPrecondition = precondition
            };
            return scheduledCommand;
        }

        internal static IScheduledCommand CreateScheduledCommand<TCommand>(
            TCommand command,
            DateTimeOffset? dueTime,
            IEvent deliveryDependsOn = null)
            where TCommand : ICommand
        {
            CommandPrecondition precondition = null;

            if (deliveryDependsOn != null)
            {
                if (deliveryDependsOn.AggregateId == Guid.Empty)
                {
                    throw new ArgumentException("An AggregateId must be set on the event on which the scheduled command depends.");
                }

                if (String.IsNullOrWhiteSpace(deliveryDependsOn.ETag))
                {
                    deliveryDependsOn.IfTypeIs<Event>()
                                     .ThenDo(e => e.ETag = Guid.NewGuid().ToString("N"))
                                     .ElseDo(() =>
                                             {
                                                 throw new ArgumentException("An ETag must be set on the event on which the scheduled command depends.");
                                             });
                }

                precondition = new CommandPrecondition
                               {
                                   AggregateId = deliveryDependsOn.AggregateId,
                                   ETag = deliveryDependsOn.ETag
                               };
            }

            if (String.IsNullOrEmpty(command.ETag))
            {
                command.IfTypeIs<Command>()
                       .ThenDo(c => c.ETag = CommandContext.Current
                                                           .IfNotNull()
                                                           .Then(ctx => ctx.NextETag(""))
                                                           .Else(() => Guid.NewGuid().ToString("N")));
            }

            var scheduledCommand = new ScheduledCommand
                                   {
                                       Command = command,
                                       DueTime = dueTime,
                                       SequenceNumber = -DateTimeOffset.UtcNow.Ticks,
                                       DeliveryPrecondition = precondition
                                   };
            return scheduledCommand;
        }

        public static async Task<IScheduledCommand<TAggregate>> Schedule<TCommand, TAggregate>(
            this ICommandScheduler<TAggregate> scheduler,
            Guid aggregateId,
            TCommand command,
            DateTimeOffset? dueTime = null,
            IEvent deliveryDependsOn = null)
            where TCommand : ICommand<TAggregate>
            where TAggregate : IEventSourced
        {
            if (aggregateId == Guid.Empty)
            {
                throw new ArgumentException("Parameter aggregateId cannot be an empty Guid.");
            }

            var scheduledCommand = CreateScheduledCommand<TCommand, TAggregate>(
                aggregateId,
                command,
                dueTime,
                deliveryDependsOn);

            await scheduler.Schedule(scheduledCommand);

            return scheduledCommand;
        }

        public static async Task<IScheduledCommand> Schedule<TCommand>(
            this ICommandScheduler scheduler,
            TCommand command,
            DateTimeOffset? dueTime = null,
            IEvent deliveryDependsOn = null)
            where TCommand : ICommand
        {
            var scheduledCommand = CreateScheduledCommand<TCommand>(
                command,
                dueTime,
                deliveryDependsOn);

            await scheduler.Schedule(scheduledCommand);

            return scheduledCommand;
        }

        internal static void DeliverIfPreconditionIsSatisfiedWithin<TAggregate>(
            this ICommandScheduler<TAggregate> scheduler,
            TimeSpan timespan,
            IScheduledCommand<TAggregate> scheduledCommand,
            IEventBus eventBus) where TAggregate : IEventSourced
        {
            eventBus.Events<IEvent>()
                    .Where(
                        e => e.AggregateId == scheduledCommand.DeliveryPrecondition.AggregateId &&
                             e.ETag == scheduledCommand.DeliveryPrecondition.ETag)
                    .Take(1)
                    .Timeout(timespan)
                    .Subscribe(
                        e => { Task.Run(() => scheduler.Deliver(scheduledCommand)).Wait(); },
                        onError: ex => { eventBus.PublishErrorAsync(new EventHandlingError(ex, scheduler)); });
        }
    }
}