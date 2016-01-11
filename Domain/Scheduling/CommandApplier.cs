using System;
using System.Threading.Tasks;

namespace Microsoft.Its.Domain
{
    internal class CommandApplier<TTarget> : ICommandApplier<TTarget> where TTarget : class
    {
        private ICommandPreconditionVerifier preconditionVerifier;

        public CommandApplier(ICommandPreconditionVerifier preconditionVerifier)
        {
            if (preconditionVerifier == null)
            {
                throw new ArgumentNullException("preconditionVerifier");
            }

            this.preconditionVerifier = preconditionVerifier;
        }

        public async Task ApplyScheduledCommand(IScheduledCommand<TTarget> scheduledCommand)
        {
            throw new System.NotImplementedException();
        }
    }
}