/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * Events.cs - small event argument types shared by the protocol/application layer.
 */

using System;

namespace EcatDroneHost.Protocol
{
    /// <summary>Direction of a raw protocol frame.</summary>
    public enum TraceDirection
    {
        Tx,
        Rx
    }

    /// <summary>A raw frame / byte block trace record (used by the log window).</summary>
    public sealed class TraceEventArgs : EventArgs
    {
        public TraceDirection Direction { get; private set; }
        /// <summary>"协议", "AM32", ...</summary>
        public string Channel { get; private set; }
        public byte[] Data { get; private set; }
        /// <summary>Human readable summary (already formatted).</summary>
        public string Text { get; private set; }
        public DateTime Timestamp { get; private set; }

        public TraceEventArgs(TraceDirection direction, string channel, byte[] data, string text)
        {
            Direction = direction;
            Channel = channel;
            Data = data;
            Text = text;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>A plain text notification.</summary>
    public sealed class TextEventArgs : EventArgs
    {
        public string Text { get; private set; }

        public TextEventArgs(string text)
        {
            Text = text;
        }
    }
}
