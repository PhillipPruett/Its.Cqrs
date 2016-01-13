using Microsoft.Its.Domain.Sql;

namespace Microsoft.Its.Domain.Api.Tests
{
    public static class TestSetUp
    {
        private static bool eventStoreInitialized;

        public static void EnsureEventStoreIsInitialized()
        {
            if (!eventStoreInitialized)
            {
                EventStoreDbContext.NameOrConnectionString =
                    @"Data Source=(localdb)\MSSQLLocalDB; Integrated Security=True; MultipleActiveResultSets=False; Initial Catalog=ItsCqrsTestsEventStore";

                using (var eventStore = new EventStoreDbContext())
                {
                    new EventStoreDatabaseInitializer<EventStoreDbContext>().InitializeDatabase(eventStore);
                }
            }

            eventStoreInitialized = true;
        }
    }
}