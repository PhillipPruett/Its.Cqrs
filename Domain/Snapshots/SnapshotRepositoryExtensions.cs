// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Its.Domain
{
    public static class SnapshotRepositoryExtensions
    {
        /// <summary>
        /// Saves a snapshot of the aggregate.
        /// </summary>
        public static async Task SaveSnapshot<TAggregate>(
            this ISnapshotRepository repository,
            TAggregate aggregate)
            where TAggregate : class, IEventSourced
        {
            var snapshotCreator = Configuration.Current.Container.Resolve<ICreateSnapshot<TAggregate>>();

            var snapshot = snapshotCreator.CreateSnapshot(aggregate);

            aggregate.InitializeSnapshot(snapshot);

            await repository.SaveSnapshot(snapshot);
        }
    }
}