﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CtfPlayback;
using CtfPlayback.FieldValues;
using LTTngCds.CookerData;
using Microsoft.Performance.SDK;

namespace LTTngDataExtensions.DataOutputTypes
{
    public class EventKind
    {
        private class Key
        {
            private string domain;
            private uint id;

            public Key(string domain, uint id)
            {
                this.domain = domain ?? string.Empty;
                this.id = id;
            }

            public override bool Equals(object obj)
            {
                if (obj is Key key)
                {
                    return domain.Equals(key.domain) && id.Equals(key.id);
                }
                else
                {
                    return false;
                }
            }

            public override int GetHashCode()
            {
                int h1 = domain.GetHashCode();
                int h2 = id.GetHashCode();
                return ((h1 << 5) + h1) ^ h2;
            }
        }

        public string Domain { get; }
        public uint Id { get; }
        public string ProviderName { get; }
        public string EventName { get; }
        public readonly List<string> FieldNames;
        public EventKind(LTTngContext context, uint id, string name, IReadOnlyList<CtfFieldValue> fields)
        {
            this.Domain = context.Domain;
            this.Id = id;
            Match traceLoggingMatch = TraceLoggingEventRegex.Match(name);
            if (traceLoggingMatch.Success)
            {
                this.ProviderName = context.Intern(traceLoggingMatch.Groups["ProviderName"].Value);
                this.EventName = context.Intern(traceLoggingMatch.Groups["EventName"].Value);
            }
            else
            {
                this.ProviderName = "Unknown";
                this.EventName = context.Intern(name);
            }
            this.FieldNames = new List<string>(fields.Count);
            foreach (var field in fields)
            {
                this.FieldNames.Add(field.FieldName);
            }
        }

        private static readonly Dictionary<Key, EventKind> RegisteredKinds = new Dictionary<Key, EventKind>();
        private static readonly Regex TraceLoggingEventRegex = new Regex("^(?<ProviderName>[a-zA-Z_.0-9]+):(?<EventName>[a-zA-Z_.0-9]+);(?<Unknown>.+);$");

        public static bool TryGetRegisteredKind(string domain, uint id, out EventKind kind)
        {
            return RegisteredKinds.TryGetValue(new Key(domain, id), out kind);
        }

        public static EventKind RegisterKind(LTTngContext context, uint id, string name, IReadOnlyList<CtfFieldValue> fields)
        {
            EventKind kind = new EventKind(context, id, name, fields);
            RegisteredKinds.Add(new Key(context.Domain, id), kind);
            return kind;
        }
    }

    public struct LTTngGenericEvent
    {
        private readonly EventKind kind;

        public LTTngGenericEvent(LTTngEvent data, LTTngContext context)
        {
      
            if (!(data.Payload is CtfStructValue payload))
            {
                throw new CtfPlaybackException("Event data is corrupt.");
            }

            this.Timestamp = data.Timestamp;
            this.CpuId = context.CurrentCpu;
            this.DiscardedEvents = data.DiscardedEvents;

            if (!EventKind.TryGetRegisteredKind(context.Domain, data.Id, out this.kind))
            {
                this.kind = EventKind.RegisterKind(context, data.Id, data.Name, payload.Fields);
            }

            // As this is being written, all columns are of type 'T', so all rows are the same. For generic events,
            // where columns have different types for different rows, this means everything becomes a string.
            //
            // We don't want to keep around each event in memory, that would use too much memory. So for now convert
            // each field value to a string, which would happen anyways.
            //
            // If the consumer is smarter in the future and allows for multi-type columns, we can re-evaluate this
            // approach. We could probably generate a type from each event descriptor, and convert to that type.
            //

            var fieldCount = payload.Fields.Count;
            if (data.StreamDefinedEventContext != null)
            {
                fieldCount += data.StreamDefinedEventContext.Fields.Count;
            }

            this.FieldNames = new List<string>(fieldCount);
            this.FieldValues = new List<string>(fieldCount);

            if (data.StreamDefinedEventContext != null)
            {
                foreach (var field in data.StreamDefinedEventContext.Fields)
                {
                    this.FieldNames.Add(field.FieldName.ToString());
                    this.FieldValues.Add(field.GetValueAsString());
                }
            }
            foreach (var field in payload.Fields)
            {
                this.FieldNames.Add(field.FieldName.ToString());
                this.FieldValues.Add(field.GetValueAsString());
            }
        }

        public string ProviderName => this.kind.ProviderName;

        public string EventName => this.kind.EventName;

        public Timestamp Timestamp { get; }

        public uint Id => this.kind.Id;

        public uint CpuId { get; }

        public uint DiscardedEvents { get; }

        public readonly List<string> FieldValues;
        public readonly List<string> FieldNames;
    }
}