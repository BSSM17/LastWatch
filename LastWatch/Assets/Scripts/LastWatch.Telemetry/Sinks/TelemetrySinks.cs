using System;
using System.Collections.Generic;

namespace LastWatch.Telemetry.Sinks
{
    public interface ITelemetrySink
    {
        void Record(TelemetryEventEnvelope value);
    }

    public sealed class NullTelemetrySink : ITelemetrySink
    {
        public void Record(TelemetryEventEnvelope value) { }
    }

    public sealed class InMemoryTelemetrySink : ITelemetrySink
    {
        private readonly List<TelemetryEventEnvelope> events;
        public int Capacity { get; }
        public long DroppedEventCount { get; private set; }
        public IReadOnlyList<TelemetryEventEnvelope> Events => events;

        public InMemoryTelemetrySink(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
            events = new List<TelemetryEventEnvelope>(capacity);
        }

        public void Record(TelemetryEventEnvelope value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (events.Count > 0 && value.EventSequence < events[events.Count - 1].EventSequence)
                throw new InvalidOperationException("Gameplay EventSequence decreased.");
            if (events.Count == Capacity)
            {
                events.RemoveAt(0);
                DroppedEventCount++;
            }
            events.Add(value);
        }
    }

    public sealed class FailureInjectionTelemetrySink : ITelemetrySink
    {
        public int AttemptCount { get; private set; }
        public void Record(TelemetryEventEnvelope value)
        {
            AttemptCount++;
            throw new InvalidOperationException("Injected telemetry sink failure.");
        }
    }

    public enum TelemetryDispatchStatus { Recorded, ValidationRejected, SinkFailed }

    public readonly struct TelemetryDispatchResult
    {
        public TelemetryDispatchStatus Status { get; }
        public string DetailCode { get; }
        public TelemetryDispatchResult(TelemetryDispatchStatus status, string detailCode)
        { Status = status; DetailCode = detailCode; }
    }

    public sealed class SafeTelemetryDispatcher
    {
        private readonly ITelemetrySink sink;
        private readonly TelemetrySchemaValidator validator;
        public long ValidationRejectedCount { get; private set; }
        public long SinkFailureCount { get; private set; }

        public SafeTelemetryDispatcher(ITelemetrySink sink, TelemetrySchemaValidator validator = null)
        {
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            this.validator = validator ?? new TelemetrySchemaValidator();
        }

        public TelemetryDispatchResult Dispatch(TelemetryEventEnvelope value)
        {
            TelemetryValidationResult validation = validator.Validate(value);
            if (!validation.IsValid)
            {
                ValidationRejectedCount++;
                return new TelemetryDispatchResult(TelemetryDispatchStatus.ValidationRejected, validation.ErrorCode);
            }
            try
            {
                sink.Record(value);
                return new TelemetryDispatchResult(TelemetryDispatchStatus.Recorded, null);
            }
            catch (Exception exception)
            {
                SinkFailureCount++;
                return new TelemetryDispatchResult(TelemetryDispatchStatus.SinkFailed, exception.GetType().Name);
            }
        }
    }
}
