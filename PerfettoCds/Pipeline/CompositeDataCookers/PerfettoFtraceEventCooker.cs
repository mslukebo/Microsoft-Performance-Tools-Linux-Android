﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Performance.SDK;
using Microsoft.Performance.SDK.Extensibility;
using Microsoft.Performance.SDK.Extensibility.DataCooking;
using Microsoft.Performance.SDK.Processing;
using PerfettoCds.Pipeline.DataOutput;
using PerfettoCds.Pipeline.SourceDataCookers;
using PerfettoProcessor;
using Utilities;

namespace PerfettoCds.Pipeline.CompositeDataCookers
{
    /// <summary>
    /// Pulls data from multiple individual SQL tables and joins them to create an Ftrace Perfetto event
    /// </summary>
    public sealed class PerfettoFtraceEventCooker : CookedDataReflector, ICompositeDataCookerDescriptor
    {
        public static readonly DataCookerPath DataCookerPath = PerfettoPluginConstants.FtraceEventCookerPath;

        public string Description => "Ftrace Event composite cooker";

        public DataCookerPath Path => DataCookerPath;

        // Declare all of the cookers that are used by this CompositeCooker.
        public IReadOnlyCollection<DataCookerPath> RequiredDataCookers => new[]
        {
            PerfettoPluginConstants.RawCookerPath,
            PerfettoPluginConstants.ThreadCookerPath,
            PerfettoPluginConstants.ProcessRawCookerPath,
            PerfettoPluginConstants.ArgCookerPath
        };

        [DataOutput]
        public ProcessedEventData<PerfettoFtraceEvent> FtraceEvents { get; }

        /// <summary>
        /// The highest number of fields found in any single event.
        /// </summary>
        [DataOutput]
        public int MaximumEventFieldCount { get; private set; }

        public PerfettoFtraceEventCooker() : base(PerfettoPluginConstants.FtraceEventCookerPath)
        {
            this.FtraceEvents =
                new ProcessedEventData<PerfettoFtraceEvent>();
        }

        public void OnDataAvailable(IDataExtensionRetrieval requiredData)
        {
            // Gather the data from all the SQL tables
            var rawData = requiredData.QueryOutput<ProcessedEventData<PerfettoRawEvent>>(new DataOutputPath(PerfettoPluginConstants.RawCookerPath, nameof(PerfettoRawCooker.RawEvents)));
            var threadData = requiredData.QueryOutput<ProcessedEventData<PerfettoThreadEvent>>(new DataOutputPath(PerfettoPluginConstants.ThreadCookerPath, nameof(PerfettoThreadCooker.ThreadEvents)));
            var processData = requiredData.QueryOutput<ProcessedEventData<PerfettoProcessRawEvent>>(new DataOutputPath(PerfettoPluginConstants.ProcessRawCookerPath, nameof(PerfettoProcessRawCooker.ProcessEvents)));
            var argsData = requiredData.QueryOutput<ProcessedEventData<PerfettoArgEvent>>(new DataOutputPath(PerfettoPluginConstants.ArgCookerPath, nameof(PerfettoArgCooker.ArgEvents)));

            // Join them all together

            // Raw contains all the ftrace events, timestamps, and CPU
            // Arg contains a variable number of extra arguments for each event.
            // Thread and process info is contained in their respective tables
            var joined = from raw in rawData
                         join thread in threadData on raw.Utid equals thread.Utid
                         join threadProcess in processData on thread.Upid equals threadProcess.Upid into pd
                         from threadProcess in pd.DefaultIfEmpty()
                         join arg in argsData on raw.ArgSetId equals arg.ArgSetId into args
                         select new { raw, args, thread, threadProcess };

            // Create events out of the joined results
            foreach (var result in joined)
            {
                MaximumEventFieldCount = Math.Max(MaximumEventFieldCount, result.args.Count());
                var args = Args.ParseArgs(result.args);

                // An event can have a thread+process or just a process
                string processFormattedName = string.Empty;
                string threadFormattedName = $"{result.thread.Name} ({result.thread.Tid})";
                if (result.threadProcess != null)
                {
                    processFormattedName = $"{result.threadProcess.Name} ({result.threadProcess.Pid})";
                }

                PerfettoFtraceEvent ev = new PerfettoFtraceEvent
                (
                    new Timestamp(result.raw.RelativeTimestamp),
                    processFormattedName,
                    threadFormattedName,
                    result.thread.Name,
                    result.thread.Tid,
                    result.raw.Cpu,
                    result.raw.Name,
                    args
                );
                this.FtraceEvents.AddEvent(ev);
            }
            this.FtraceEvents.FinalizeData();
        }
    }
}
