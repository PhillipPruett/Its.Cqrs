using System.Threading.Tasks;

namespace Microsoft.Its.Domain
{
    /// <summary>
    /// Handles commands.
    /// </summary>
    /// <typeparam name="TCommand">The type of the command.</typeparam>
    public interface ICommandHandler<TCommand>
        where TCommand : class, ICommand
    {
        /// <summary>
        /// Called when a command has passed validation and authorization checks.
        /// </summary>
        Task EnactCommand(TCommand command);

        /// <summary>
        /// Handles any exception that occurs during delivery of a scheduled command.
        /// </summary>
        /// <remarks>This method can control retry and cancelation of the command.</remarks>
        Task HandleScheduledCommandException(CommandFailed<TCommand> command);
    }
}