using System;
using System.Diagnostics;

namespace Microsoft.Its.Domain
{
    /// <summary>
    /// An event that indicates that a command was scheduled.
    /// </summary>
    /// <typeparam name="TAggregate">The type of the aggregate.</typeparam>
    [EventName("Scheduled")]
    [DebuggerDisplay("{ToString()}")]
    public class ScheduledCommand :
        IScheduledCommand
    {
        public ScheduledCommand()
        {
            ETag = Guid.NewGuid().ToString("N").ToETag();
        }

        public long SequenceNumber { get; set; }
        public string ETag { get; private set; }
        public DateTimeOffset? DueTime { get; set; }
        public CommandPrecondition DeliveryPrecondition { get; set; }
        public ScheduledCommandResult Result { get; set; }
        public ICommand Command { get; set; }

        //TODO: remove these after IEvent is removed from IScheduledCommand
        public Guid AggregateId { get; private set; }
        public DateTimeOffset Timestamp { get; private set; }
    }
}