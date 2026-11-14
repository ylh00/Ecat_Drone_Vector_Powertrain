/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * LogBuffer.cs - timestamped protocol / operation log with optional hex dump.
 *
 * Entries are appended from the UI thread and from the background receiver
 * thread; Changed is raised on the appending thread, the UI marshals it.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EcatDroneHost.Protocol;

namespace EcatDroneHost.App
{
    public enum LogLevel
    {
        Trace = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    public sealed class LogEntry
    {
        public DateTime Time;
        public LogLevel Level;
        public string Category = string.Empty;
        public string Text = string.Empty;
        public byte[] Raw;

        public LogEntry(DateTime time, LogLevel level, string category, string text, byte[] raw)
        {
            Time = time;
            Level = level;
            Category = category;
            Text = text;
            Raw = raw;
        }

        public string Format(bool hexMode)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Time.ToString("HH:mm:ss.fff"));
            sb.Append(" [").Append(Category).Append("] ");
            switch (Level)
            {
                case LogLevel.Warning:
                    sb.Append("警告: ");
                    break;
                case LogLevel.Error:
                    sb.Append("错误: ");
                    break;
                default:
                    break;
            }
            sb.Append(Text);
            if (hexMode && Raw != null && Raw.Length > 0)
            {
                sb.AppendLine();
                sb.Append("    ").Append(Bytes.ToHexString(Raw));
            }
            return sb.ToString();
        }
    }

    /// <summary>Thread safe bounded log ring.</summary>
    public sealed class LogBuffer
    {
        private readonly object _lock = new object();
        private readonly List<LogEntry> _entries = new List<LogEntry>();
        private int _capacity = 20000;

        public event EventHandler Changed;

        /// <summary>Show raw bytes below each entry.</summary>
        public bool HexMode { get; set; }

        /// <summary>Maximum number of retained entries.</summary>
        public int Capacity
        {
            get { return _capacity; }
            set { _capacity = value < 100 ? 100 : value; }
        }

        public bool Paused { get; set; }

        public int Count
        {
            get { lock (_lock) { return _entries.Count; } }
        }

        public void Add(LogLevel level, string category, string text)
        {
            AddEntry(new LogEntry(DateTime.Now, level, category, text, null));
        }

        public void Info(string category, string text)
        {
            Add(LogLevel.Info, category, text);
        }

        public void Warning(string category, string text)
        {
            Add(LogLevel.Warning, category, text);
        }

        public void Error(string category, string text)
        {
            Add(LogLevel.Error, category, text);
        }

        /// <summary>Add a protocol trace entry (direction is encoded into the text).</summary>
        public void AddTrace(string category, TraceDirection direction, byte[] data, string text)
        {
            LogLevel level = direction == TraceDirection.Tx ? LogLevel.Trace : LogLevel.Info;
            AddEntry(new LogEntry(DateTime.Now, level, category, text, data));
        }

        public void AddEntry(LogEntry entry)
        {
            if (Paused)
            {
                return;
            }
            lock (_lock)
            {
                _entries.Add(entry);
                if (_entries.Count > _capacity)
                {
                    _entries.RemoveRange(0, _entries.Count - _capacity);
                }
            }
            EventHandler handler = Changed;
            if (handler != null)
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception)
                {
                    /* ignore */
                }
            }
        }

        public LogEntry[] Snapshot()
        {
            lock (_lock)
            {
                return _entries.ToArray();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
            EventHandler handler = Changed;
            if (handler != null)
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception)
                {
                    /* ignore */
                }
            }
        }

        /// <summary>Format the whole log as text.</summary>
        public string FormatAll(bool hexMode)
        {
            LogEntry[] entries = Snapshot();
            StringBuilder sb = new StringBuilder(entries.Length * 64);
            for (int i = 0; i < entries.Length; i++)
            {
                sb.AppendLine(entries[i].Format(hexMode));
            }
            return sb.ToString();
        }

        /// <summary>Save the log to a UTF-8 text file. Returns the number of lines written.</summary>
        public int SaveToFile(string path, bool hexMode)
        {
            string text = FormatAll(hexMode);
            File.WriteAllText(path, text, new UTF8Encoding(true));
            return Snapshot().Length;
        }
    }
}
