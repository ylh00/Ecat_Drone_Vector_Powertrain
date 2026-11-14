/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * ShellEvents.cs - event payloads raised by the main window.
 */

using System;
using EcatDroneHost.Protocol;

namespace EcatDroneHost.App
{
    /// <summary>New telemetry snapshot (raised on the poller thread).</summary>
    public sealed class TelemetryEventArgs : EventArgs
    {
        public TelemetryData Telemetry { get; private set; }

        public TelemetryEventArgs(TelemetryData telemetry)
        {
            Telemetry = telemetry;
        }
    }

    /// <summary>New EtherCAT status snapshot (raised on the poller thread).</summary>
    public sealed class EcatStatusEventArgs : EventArgs
    {
        public EcatStatus Status { get; private set; }

        public EcatStatusEventArgs(EcatStatus status)
        {
            Status = status;
        }
    }
}
