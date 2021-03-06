// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using Microsoft.Its.Recipes;

namespace Microsoft.Its.Domain
{
    public static class ScheduledCommandExtensions
    {
        public static bool IsDue(
            this IScheduledCommand command,
            IClock clock = null)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            clock = clock ??
                    command.Clock ??
                    command.Result
                           .IfTypeIs<CommandScheduled>()
                           .Then(scheduled => scheduled.Clock)
                           .ElseDefault() ??
                    Clock.Current;

            return (command.DueTime == null ||
                    command.DueTime <= clock.Now())
                   && !(command.Result is CommandDelivered);
        }

        public static async Task<bool> IsPreconditionSatisfied(
            this IETagChecker preconditionChecker,
            IScheduledCommand scheduledCommand)
        {
            if (preconditionChecker == null)
            {
                throw new ArgumentNullException(nameof(preconditionChecker));
            }

            var precondition = scheduledCommand.DeliveryPrecondition;

            if (precondition == null)
            {
                return true;
            }

            return await preconditionChecker.HasBeenRecorded(
                precondition.Scope,
                precondition.ETag);
        }

        internal static void EnsureCommandHasETag<TTarget>(this IScheduledCommand<TTarget> scheduledCommand)
        {
            var command = scheduledCommand.Command;

            if (String.IsNullOrEmpty(command.ETag))
            {
                command.IfTypeIs<Command>()
                       .ThenDo(c => c.ETag = CommandContext.Current
                                                           .IfNotNull()
                                                           .Then(ctx => ctx.NextETag(scheduledCommand.TargetId))
                                                           .Else(() => Guid.NewGuid().ToString("N").ToETag()));
            }
        }

        internal static void ThrowIfNotAllowedToChangeTo(
            this ScheduledCommandResult @from,
            ScheduledCommandResult to)
        {
            if (to == null)
            {
                throw new ArgumentNullException(nameof(to), "Result cannot be set to null.");
            }

            if (@from is CommandDelivered)
            {
                if (to is CommandScheduled)
                {
                    throw new ArgumentException("Command cannot be scheduled again when it has already been delivered.");
                }
            }
        }
    }
}