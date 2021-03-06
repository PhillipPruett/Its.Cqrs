// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Dynamic;

namespace Microsoft.Its.Domain.Testing
{
    public class InMemoryStoredEvent : IStoredEvent, IHaveExtensibleMetada
    {
        private dynamic metadata;

        public InMemoryStoredEvent()
        {
            Timestamp = Clock.Now();
        }

        public string Body { get; set; }

        public string ETag { get; set; }

        public string AggregateId { get; set; }

        public DateTimeOffset Timestamp { get; set; }

        public string StreamName { get; set; }

        public string Type { get; set; }

        public long SequenceNumber { get; set; }

        public dynamic Metadata => metadata ?? (metadata = new ExpandoObject());
    }
}