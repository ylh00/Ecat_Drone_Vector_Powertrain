/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * Transport/IByteTransport.cs - raw byte transport abstraction.
 *
 * Two implementations exist:
 *   SerialTransport       - USB CDC-ACM virtual COM port (real hardware)
 *   FakeFirmwareTransport - in-process firmware simulator (selftest / demo mode)
 */

using System;

namespace EcatDroneHost.Transport
{
    /// <summary>Half duplex raw byte transport used by BoardClient.</summary>
    public interface IByteTransport : IDisposable
    {
        /// <summary>Human readable description (COM port name or "模拟设备").</summary>
        string Description { get; }

        bool IsOpen { get; }

        /// <summary>Timeout used by Read() when no byte is available.</summary>
        int ReadTimeoutMs { get; set; }

        void Open();
        void Close();

        /// <summary>
        /// Block until at least one byte is available (or the read timeout expires).
        /// Returns the number of bytes copied into the buffer, 0 on timeout.
        /// </summary>
        int Read(byte[] buffer, int offset, int count);

        /// <summary>Write all bytes (must not return before everything is queued).</summary>
        void Write(byte[] data, int offset, int count);

        /// <summary>Drop any buffered input (called after connect / resync).</summary>
        void DiscardInput();
    }
}
