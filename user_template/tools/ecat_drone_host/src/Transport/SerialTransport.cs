/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * SerialTransport.cs - USB CDC-ACM virtual COM port transport.
 *
 * The board enumerates as a USB CDC-ACM device (VID 0x0A4D / PID 0xE3A2), the
 * baud rate is irrelevant for the link (it is a virtual port) but is set to
 * 115200 for convenience.  Read() returns 0 on timeout instead of throwing so
 * that the background receiver thread can poll its shutdown flag.
 */

using System;
using System.IO.Ports;
using EcatDroneHost.Protocol;

namespace EcatDroneHost.Transport
{
    /// <summary>SerialPort based transport (System.IO.Ports lives in System.dll).</summary>
    public sealed class SerialTransport : IByteTransport
    {
        private readonly string _portName;
        private readonly int _baudRate;
        private SerialPort _port;
        private int _readTimeoutMs = 50;

        public SerialTransport(string portName, int baudRate)
        {
            if (string.IsNullOrEmpty(portName))
            {
                throw new ArgumentException("串口名称为空", "portName");
            }
            _portName = portName;
            _baudRate = baudRate <= 0 ? 115200 : baudRate;
        }

        public string PortName
        {
            get { return _portName; }
        }

        public string Description
        {
            get { return _portName + " @" + _baudRate; }
        }

        public bool IsOpen
        {
            get { return _port != null && _port.IsOpen; }
        }

        public int ReadTimeoutMs
        {
            get { return _readTimeoutMs; }
            set
            {
                _readTimeoutMs = value < 1 ? 1 : value;
                if (_port != null)
                {
                    _port.ReadTimeout = _readTimeoutMs;
                }
            }
        }

        /// <summary>Keep DTR/RTS asserted - some USB CDC firmwares gate TX on DTR.</summary>
        public bool DtrEnable { get; set; }

        public void Open()
        {
            if (IsOpen)
            {
                return;
            }
            SerialPort port = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One);
            port.Handshake = Handshake.None;
            port.ReadTimeout = _readTimeoutMs;
            port.WriteTimeout = 1000;
            port.ReadBufferSize = 8192;
            port.WriteBufferSize = 4096;
            port.DtrEnable = DtrEnable;
            port.RtsEnable = DtrEnable;
            port.Open();
            /* drop anything the device transmitted before the host was ready */
            try
            {
                port.DiscardInBuffer();
                port.DiscardOutBuffer();
            }
            catch (Exception)
            {
                /* some virtual ports do not implement Discard - ignore */
            }
            _port = port;
        }

        public void Close()
        {
            SerialPort port = _port;
            _port = null;
            if (port == null)
            {
                return;
            }
            try
            {
                port.Close();
            }
            catch (Exception)
            {
                /* already gone */
            }
            try
            {
                port.Dispose();
            }
            catch (Exception)
            {
                /* ignore */
            }
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            SerialPort port = _port;
            if (port == null || !port.IsOpen)
            {
                throw new HostException("串口未打开");
            }
            if (count <= 0)
            {
                return 0;
            }
            try
            {
                return port.Read(buffer, offset, count);
            }
            catch (TimeoutException)
            {
                return 0;
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }

        public void Write(byte[] data, int offset, int count)
        {
            SerialPort port = _port;
            if (port == null || !port.IsOpen)
            {
                throw new HostException("串口未打开");
            }
            if (data == null || count <= 0)
            {
                return;
            }
            try
            {
                port.Write(data, offset, count);
            }
            catch (TimeoutException ex)
            {
                throw new HostException("串口写入超时: " + ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                throw new HostException("串口写入失败: " + ex.Message);
            }
        }

        public void DiscardInput()
        {
            SerialPort port = _port;
            if (port == null || !port.IsOpen)
            {
                return;
            }
            try
            {
                port.DiscardInBuffer();
            }
            catch (Exception)
            {
                /* ignore */
            }
        }

        public void Dispose()
        {
            Close();
        }

        /// <summary>Available COM port names, sorted naturally.</summary>
        public static string[] GetPortNames()
        {
            string[] names;
            try
            {
                names = SerialPort.GetPortNames();
            }
            catch (Exception)
            {
                return new string[0];
            }
            Array.Sort(names, ComparePortNames);
            return names;
        }

        private static int ComparePortNames(string a, string b)
        {
            int numberA;
            int numberB;
            if (TryGetPortNumber(a, out numberA) && TryGetPortNumber(b, out numberB))
            {
                return numberA.CompareTo(numberB);
            }
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetPortNumber(string name, out int number)
        {
            number = 0;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            int i = name.Length;
            while (i > 0 && char.IsDigit(name[i - 1]))
            {
                i--;
            }
            if (i == name.Length)
            {
                return false;
            }
            return int.TryParse(name.Substring(i), out number);
        }
    }
}
