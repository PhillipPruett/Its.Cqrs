using System;
using System.Threading.Tasks;
using FluentAssertions;
using Its.Validation;
using Its.Validation.Configuration;
using Microsoft.Its.Recipes;
using NUnit.Framework;

namespace Microsoft.Its.Domain.Tests
{
    [TestFixture]
    public class NonAggregateCommandTests
    {
        internal static int CallCount;

        [SetUp]
        public void Setup()
        {
            Command<Target>.AuthorizeDefault = (account, command) => true;
            CallCount = 0;
        }

        [Test]
        public async Task the_command_scheduler_can_be_used_to_deliver_commands()
        {
            var scheduler = Configuration.Current.CommandScheduler<Target>();

            //await scheduler.Schedule(new CommandScheduled<Target>
            //{
            //    Command = new CommandOnTarget(),
            //    DueTime = Clock.Now().AddHours(1)
            //});
            throw new NotImplementedException("Test Not Finished");
        }

        [Test]
        public async Task when_a_command_is_applied_directly_the_command_is_executed()
        {
            await new CommandOnTarget { }.ApplyToAsync(new Target());
            CallCount.Should().Be(1);
        }

        [Test]
        public async Task when_a_command_is_applied_directly_with_an_etag_the_command_is_executed()
        {
            await new CommandOnTarget { ETag = Any.Guid().ToString() }.ApplyToAsync(new Target());
            CallCount.Should().Be(1);
        }

        [Test]
        public async Task commands_are_idempotent()
        {
            var eTag = Any.Guid().ToString();
            await new CommandOnTarget { ETag = eTag }.ApplyToAsync(new Target());
            await new CommandOnTarget { ETag = eTag }.ApplyToAsync(new Target());
            CallCount.Should().Be(1);

            throw new NotImplementedException("Test Not Finished");
        }

        [Test]
        public void command_validations_are_checked()
        {
            Action applyCommand = () => (new CommandOnTarget { FailCommandValidation = true }.ApplyToAsync(new Target())).Wait();

            applyCommand.ShouldThrow<CommandValidationException>();
        }

        [Test]
        public void target_validations_are_checked()
        {
            Action applyCommand = () => (new CommandOnTarget { }.ApplyToAsync(new Target(){ FailCommandApplications = true})).Wait();

            applyCommand.ShouldThrow<CommandValidationException>();
        }

        [Test]
        public void command_delivery_precondition_requirements_are_required_to_be_met_for_delivery()
        {
            throw new NotImplementedException("Test Not Finished");
        }

    }

    public class Target
    {
        public bool FailCommandApplications { get; set; }
    }

    public class CommandOnTargetCommandHandler : ICommandHandler<Target, CommandOnTarget>
    {
        public async Task EnactCommand(Target aggregate, CommandOnTarget command)
        {
            NonAggregateCommandTests.CallCount++;
        }

        public async Task HandleScheduledCommandException(Target aggregate, CommandFailed<CommandOnTarget> command)
        {
        }
    }

    public class CommandOnTarget : Command<Target>
    {
        public bool FailCommandValidation { get; set; }

        public override IValidationRule<Target> Validator
        {
            get
            {
                return Validate.That<Target>(t => !t.FailCommandApplications).WithMessage("Target failed command validation");
            }
        }

        public override IValidationRule CommandValidator
        {
            get
            {
                return Validate.That<CommandOnTarget>(t => !t.FailCommandValidation).WithMessage("Command failed validation");
            }
        }
    }
}