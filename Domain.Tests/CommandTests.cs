using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Its.Domain.Testing;
using Microsoft.Its.Recipes;
using NUnit.Framework;

namespace Microsoft.Its.Domain.Tests
{
    [TestFixture]
    public class CommandTests
    {
        private ICommandScheduler commandScheduler;

        [Test]
        public async Task a_command_can_be_applied_without_specifying_an_aggregate()
        {
            var command = new DoTheNeedful();
            commandScheduler = Configuration.Current.CommandScheduler();
            await commandScheduler.Schedule(command);

            throw new NotImplementedException("Test Not Finished");
        }

    }

    public class DoTheNeedful : Command
    {
        public override string CommandName
        {
            get { return "DoTheNeedful"; }
        }
    }

    public class DoTheNeedfulCommandHandler : ICommandHandler<DoTheNeedful>
    {
        public async Task EnactCommand(DoTheNeedful command)
        {
        }

        public async Task HandleScheduledCommandException(CommandFailed<DoTheNeedful> command)
        {
        }
    }
}