using Microsoft.Its.Domain.Sql;

namespace Microsoft.Its.Domain.Api.Tests
{
    public static class TestSetUp
    {
        public static void InitializeEventStore()
        {
            EventStoreDbContext.NameOrConnectionString =
                @"Data Source=(localdb)\MSSQLLocalDB; Integrated Security=True; MultipleActiveResultSets=False; Initial Catalog=ItsCqrsTestsCommandScheduler";

            using (var eventStore = new EventStoreDbContext())
            {
                new EventStoreDatabaseInitializer<EventStoreDbContext>().InitializeDatabase(eventStore);
            }
        }
    }
}