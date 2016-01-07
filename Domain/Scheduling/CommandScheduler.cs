using System;
using System.Threading.Tasks;

namespace Microsoft.Its.Domain
{
    public class CommandScheduler: ICommandScheduler
    {
        private readonly ICommandPreconditionVerifier preconditionVerifier;

        public CommandScheduler(ICommandPreconditionVerifier preconditionVerifier = null)
        {
            this.preconditionVerifier = preconditionVerifier ??
                                        Configuration.Current.CommandPreconditionVerifier();
        }

         /// <summary>
        /// Schedules the specified command.
        /// </summary>
        /// <param name="scheduledCommand">The scheduled command.</param>
        /// <returns>
        /// A task that is complete when the command has been successfully scheduled.
        /// </returns>
        /// <exception cref="System.NotSupportedException">Non-immediate scheduling is not supported.</exception>
        public virtual async Task Schedule(IScheduledCommand scheduledCommand)
        {
            if (scheduledCommand.Command.CanBeDeliveredDuringScheduling() && scheduledCommand.IsDue())
            {
                if (!await VerifyPrecondition(scheduledCommand))
                {
                    CommandSchedulerUtilities.DeliverIfPreconditionIsSatisfiedSoon(scheduledCommand,Configuration.Current);
                }
                else
                {
                    // resolve the command scheduler so that delivery goes through the whole pipeline
                    await Configuration.Current.CommandScheduler().Deliver(scheduledCommand);
                    return;
                }
            }

            if (scheduledCommand.Result == null)
            {
                throw new NotSupportedException("Deferred scheduling is not supported.");
            }
        }

        public virtual async Task Deliver(IScheduledCommand scheduledCommand)
        {
            await repository.ApplyScheduledCommand(scheduledCommand, preconditionVerifier);
        }

        protected async Task<bool> VerifyPrecondition(IScheduledCommand scheduledCommand)
        {
            return await preconditionVerifier.IsPreconditionSatisfied(scheduledCommand);
        }
    }
}