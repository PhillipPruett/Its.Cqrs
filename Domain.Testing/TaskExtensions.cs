// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.Its.Domain.Testing
{
    public static class TaskExtensions
    {
        /// <summary>
        /// Throws a TimeoutException if the specified task does not complete within the given timespan.
        /// </summary>
        /// <param name="task">The task.</param>
        /// <param name="timespan">The timespan.</param>
        /// <exception cref="System.TimeoutException"></exception>
        public static async Task TimeoutAfter(
            this Task task,
            TimeSpan timespan)
        {
            if (task.IsCompleted)
            {
                return;
            }

            if (task == await Task.WhenAny(task, Task.Delay(timespan)))
            {
                await task;
            }
            else
            {
                throw new TimeoutException();
            }
        }
        
        public static async Task<T> TimeoutAfter<T>(
            this Task<T> task,
            TimeSpan timespan)
        {
            if (task.IsCompleted)
            {
                return task.Result;
            }

            if (task == await Task.WhenAny(task, Task.Delay(timespan)))
            {
                return await task;
            }
            
            throw new TimeoutException();
        }

        /// <summary>
        /// Provides a way to specify the intention to fire and forget a task and suppress the compiler warning that results from unawaited tasks.
        /// </summary>
        internal static void DontAwait(this Task task)
        {
        }
    }
}