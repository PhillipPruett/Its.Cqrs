// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Its.Domain.Serialization;
using Microsoft.Its.Recipes;
using Its.Validation;

namespace Microsoft.Its.Domain
{
    /// <summary>
    /// Provides additional functionality for event-sourced aggregates.
    /// </summary>
    public static class AggregateExtensions
    {
        /// <summary>
        ///     Applies a command to an aggregate.
        /// </summary>
        /// <typeparam name="TTarget">The type of the target.</typeparam>
        /// <param name="target">The target.</param>
        /// <param name="command">The command.</param>
        /// <returns>The same target with the command applied and any applicable updates performed.</returns>
        public static TTarget Apply<TTarget>(
            this TTarget target,
            ICommand<TTarget> command)
            where TTarget : class
        {
            command.ApplyTo(target);
            return target;
        }

        /// <summary>
        ///     Applies a command to an aggregate.
        /// </summary>
        /// <typeparam name="TTarget">The type of the target.</typeparam>
        /// <param name="target">The target.</param>
        /// <param name="command">The command.</param>
        /// <returns>The same target with the command applied and any applicable updates performed.</returns>
        public static async Task<TTarget> ApplyAsync<TTarget>(
            this TTarget target,
            ICommand<TTarget> command)
            where TTarget : class
        {
            await command.ApplyToAsync(target);
            return target;
        }

        /// <summary>
        /// Gets an event sequence containing both the event history and pending events for the specified aggregate.
        /// </summary>
        /// <param name="aggregate">The aggregate.</param>
        /// <returns></returns>
        public static IEnumerable<IEvent> Events(this EventSourcedAggregate aggregate)
        {
            if (aggregate == null)
            {
                throw new ArgumentNullException("aggregate");
            }

            if (aggregate.SourceSnapshot != null)
            {
                throw new InvalidOperationException("Aggregate was sourced from a snapshot, so event history is unavailable.");
            }

            return aggregate.EventHistory.Concat(aggregate.PendingEvents);
        }

        /// <summary>
        /// Determines whether an aggregate is valid for application of the specified command.
        /// </summary>
        /// <typeparam name="TAggregate">The type of the aggregate.</typeparam>
        /// <param name="aggregate">The aggregate.</param>
        /// <param name="command">The command.</param>
        /// <returns>
        ///   <c>true</c> if the command can be applied; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsValidTo<TAggregate>(this TAggregate aggregate, Command<TAggregate> command)
            where TAggregate : class
        {
            return !command.RunAllValidations(aggregate, false).HasFailures;
        }

        /// <summary>
        /// Creates a new instance of the aggregate in memory using its state as of the specified version.
        /// </summary>
        public static TAggregate AsOfVersion<TAggregate>(this TAggregate aggregate, long version) where TAggregate : EventSourcedAggregate
        {
            var snapshot = aggregate.SourceSnapshot;

            var eventsAsOfVersion = aggregate.EventHistory
                                             .Concat(aggregate.PendingEvents)
                                             .Where(e => e.SequenceNumber <= version);

            if (snapshot != null)
            {
                if (snapshot.Version > version)
                {
                    throw new InvalidOperationException("Snapshot version is later than specified version. Source the aggregate from an earlier snapshot or from events in order to use AsOfVersion.");
                }

                return AggregateType<TAggregate>.FromSnapshot.Invoke(
                    snapshot,
                    eventsAsOfVersion);
            }

            return AggregateType<TAggregate>.FromEventHistory.Invoke(
                aggregate.Id,
                eventsAsOfVersion);
        }

        /// <summary>
        /// Updates the specified aggregate with additional events.
        /// </summary>
        /// <exception cref="System.ArgumentNullException">events</exception>
        /// <exception cref="System.InvalidOperationException">Aggregates having pending events cannot be updated.</exception>
        /// <remarks>This method can be used when additional events have been appended to an event stream and you would like to bring an in-memory aggregate up to date with those events. If there are new pending events, the aggregate needs to be reset first, and any commands re-applied.</remarks>
        internal static void Update<TAggregate>(
            this TAggregate aggregate,
            IEnumerable<IEvent> events)
            where TAggregate : IEventSourced
        {
            if (events == null)
            {
                throw new ArgumentNullException("events");
            }

            if (aggregate.PendingEvents.Any())
            {
                throw new InvalidOperationException("Aggregates having pending events cannot be updated.");
            }

            var startingVersion = aggregate.Version;

            var pendingEvents = aggregate.PendingEvents
                                         .IfTypeIs<EventSequence>()
                                         .ElseDefault();

            foreach (var @event in events
                .OfType<IEvent<TAggregate>>()
                .Where(e => e.SequenceNumber > startingVersion)
                .Do(e =>
                {
                    if (e.SequenceNumber == 0)
                    {
                        throw new InvalidOperationException("Event has not been previously stored: " + e.ToJson());
                    }
                })
                .ToArray())
            {
                pendingEvents.Add(@event);
                @event.Update(aggregate);
            }

            aggregate.IfTypeIs<EventSourcedAggregate>()
                     .ThenDo(a => a.ConfirmSave());
        }

        /// <summary>
        /// Validates the command against the specified aggregate.
        /// </summary>
        /// <typeparam name="TAggregate">The type of the aggregate.</typeparam>
        /// <param name="aggregate">The aggregate.</param>
        /// <param name="command">The command.</param>
        /// <returns>A <see cref="ValidationReport" /> detailing any validation errors.</returns>
        public static ValidationReport Validate<TAggregate>(this TAggregate aggregate, Command<TAggregate> command)
            where TAggregate : class
        {
            return command.RunAllValidations(aggregate, false);
        }

        /// <summary>
        /// Returns the version number of the aggregate, which is equal to it's latest event's sequence id.
        /// </summary>
        /// <typeparam name="TAggregate">The type of the aggregate.</typeparam>
        /// <param name="aggregate">The aggregate.</param>
        /// <returns>The aggregate's version.</returns>
        public static long Version<TAggregate>(this TAggregate aggregate)
            where TAggregate : EventSourcedAggregate
        {
            if (aggregate == null)
            {
                throw new ArgumentNullException("aggregate");
            }

            return Math.Max(
                ((EventSequence) aggregate.EventHistory).Version,
                ((EventSequence) aggregate.PendingEvents).Version);
        }

        /// <summary>
        /// Initializes the interface properties of a snapshot.
        /// </summary>
        /// <typeparam name="TAggregate">The type of the aggregate.</typeparam>
        /// <param name="aggregate">The aggregate.</param>
        /// <param name="snapshot">The snapshot.</param>
        /// <exception cref="System.ArgumentNullException">
        /// aggregate
        /// or
        /// snapshot
        /// </exception>
        public static void InitializeSnapshot<TAggregate>(this TAggregate aggregate, ISnapshot snapshot)
            where TAggregate : class, IEventSourced
        {
            if (aggregate == null)
            {
                throw new ArgumentNullException("aggregate");
            }
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            if (aggregate.PendingEvents.Any())
            {
                throw new InvalidOperationException("A snapshot can only be created from an aggregate having no pending events. Save the aggregate before creating a snapshot.");
            }

            snapshot.AggregateId = aggregate.Id;
            snapshot.AggregateTypeName = AggregateType<TAggregate>.EventStreamName;
            snapshot.LastUpdated = Clock.Now();
            snapshot.Version = aggregate.Version;
            snapshot.ETags = aggregate.CreateETagBloomFilter();
        }

        internal static BloomFilter CreateETagBloomFilter<TAggregate>(this TAggregate aggregate)
            where TAggregate : class, IEventSourced
        {
            return aggregate.IfTypeIs<EventSourcedAggregate>()
                            .Then(a =>
                            {
                                if (a.WasSourcedFromSnapshot)
                                {
                                    return a.SourceSnapshot.ETags;
                                }

                                var bloomFilter = new BloomFilter();
                                a.EventHistory
                                 .Select(e => e.ETag)
                                 .Where(etag => !string.IsNullOrWhiteSpace(etag))
                                 .ForEach(bloomFilter.Add);
                                return bloomFilter;
                            })
                            .ElseDefault();
        }

        /// <summary>
        /// Determines whether the specified ETag already exists in the aggregate's event stream.
        /// </summary>
        /// <typeparam name="TAggregate">The type of the aggregate.</typeparam>
        /// <param name="aggregate">The aggregate.</param>
        /// <param name="etag">The etag.</param>
        public static bool HasETag<TAggregate>(this TAggregate aggregate, string etag)
            where TAggregate : class, IEventSourced
        {
            if (string.IsNullOrWhiteSpace(etag))
            {
                return false;
            }

            var eventSourcedAggregate = aggregate as EventSourcedAggregate;
            if (eventSourcedAggregate != null)
            {
                var answer = eventSourcedAggregate.HasETag(etag);

                if (answer == ProbabilisticAnswer.Yes)
                {
                    return true;
                }

                if (answer == ProbabilisticAnswer.No)
                {
                    return false;
                }

                // maybe... which means we need to do a lookup
                var preconditionVerifier = Configuration.Current.CommandPreconditionVerifier();

                return Task.Run(() => preconditionVerifier.HasBeenApplied(
                    aggregate.Id,
                    etag)).Result;
            }

            return false;
        }
    }
}
