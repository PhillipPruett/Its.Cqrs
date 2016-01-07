// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using Pocket;

namespace Microsoft.Its.Domain
{
    internal static class PocketContainerExtensions
    {
        public static PocketContainer UseImmediateCommandScheduling(this PocketContainer container)
        {
            return container.AddStrategy(type =>
            {
                if (type.IsInterface)
                {
                    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICommandScheduler<>))
                    {
                        var aggregateType = type.GetGenericArguments().First();
                        var schedulerType = typeof(CommandScheduler<>).MakeGenericType(aggregateType);

                        return c => c.Resolve(schedulerType);
                    }
                    if (type == typeof(ICommandScheduler))
                    {
                        return c => c.Resolve(typeof(CommandSchedulerUtilities));
                    }
                }

                return null;
            });
        }
    }
}
