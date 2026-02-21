using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniLab.Core.Telemetry
{
    public static class TelemetryService
    {
        public static event Action<string, IReadOnlyDictionary<string, object>> OnEventTracked;

        public static void Track(string eventName)
        {
            Track(eventName, null);
        }

        public static void Track(string eventName, IReadOnlyDictionary<string, object> payload)
        {
            Debug.Log($"[Telemetry] {eventName}");
            OnEventTracked?.Invoke(eventName, payload ?? EmptyPayload.Instance);
        }

        private sealed class EmptyPayload : Dictionary<string, object>
        {
            public static readonly EmptyPayload Instance = new EmptyPayload();
        }
    }
}
