/*
 * ecat_drone_host - PC tuning tool for the ecat_drone_powertrain EtherCAT slave
 *
 * Crc16.cs - CRC16 variants used by the board protocol and the AM32 4-way bridge
 *
 * The board <-> PC protocol uses CRC16-CCITT-FALSE:
 *   polynomial 0x1021, init 0xFFFF, no input/output reflection, no final xor.
 * The AM32 / BLHeli 4-way "container" frames use CRC16-XMODEM:
 *   polynomial 0x1021, init 0x0000, no reflection, no final xor.
 * The BLHeli boot-loader wire frames additionally use CRC16-ARC (a.k.a. IBM,
 *   polynomial 0x8005 reflected -> 0xA001, init 0x0000) which the board side
 *   normally handles; it is available here for diagnostics.
 */

using System;

namespace EcatDroneHost.Protocol
{
    /// <summary>CRC16 helpers. All functions are pure and thread safe.</summary>
    public static class Crc16
    {
        /// <summary>CRC16-CCITT-FALSE (poly 0x1021, init 0xFFFF, no reflect).</summary>
        public static ushort CcittFalse(byte[] data, int offset, int count)
        {
            return CcittFalseUpdate(0xFFFF, data, offset, count);
        }

        /// <summary>CRC16-CCITT-FALSE over a whole buffer.</summary>
        public static ushort CcittFalse(byte[] data)
        {
            if (data == null)
            {
                return 0xFFFF;
            }
            return CcittFalse(data, 0, data.Length);
        }

        /// <summary>Continue a CRC16-CCITT-FALSE calculation.</summary>
        public static ushort CcittFalseUpdate(ushort crc, byte[] data, int offset, int count)
        {
            if (data == null || count <= 0)
            {
                return crc;
            }
            CheckRange(data, offset, count);
            for (int i = offset; i < offset + count; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((crc & 0x8000U) != 0U)
                    {
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    }
                    else
                    {
                        crc = (ushort)(crc << 1);
                    }
                }
            }
            return crc;
        }

        /// <summary>CRC16-XMODEM (poly 0x1021, init 0x0000, no reflect, no xor).</summary>
        public static ushort Xmodem(byte[] data, int offset, int count)
        {
            if (data == null)
            {
                return 0x0000;
            }
            CheckRange(data, offset, count);
            ushort crc = 0x0000;
            for (int i = offset; i < offset + count; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((crc & 0x8000U) != 0U)
                    {
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    }
                    else
                    {
                        crc = (ushort)(crc << 1);
                    }
                }
            }
            return crc;
        }

        /// <summary>CRC16-ARC / IBM, reflected poly 0xA001, init 0x0000 (BLHeli wire frames).</summary>
        public static ushort Arc(byte[] data, int offset, int count)
        {
            if (data == null)
            {
                return 0x0000;
            }
            CheckRange(data, offset, count);
            ushort crc = 0x0000;
            for (int i = offset; i < offset + count; i++)
            {
                crc ^= data[i];
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((crc & 0x0001U) != 0U)
                    {
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    }
                    else
                    {
                        crc = (ushort)(crc >> 1);
                    }
                }
            }
            return crc;
        }

        private static void CheckRange(byte[] data, int offset, int count)
        {
            if (offset < 0 || count < 0 || offset + count > data.Length)
            {
                throw new ArgumentOutOfRangeException("offset");
            }
        }

        /// <summary>Format as "0xABCD".</summary>
        public static string ToHex(ushort value)
        {
            return "0x" + value.ToString("X4");
        }
    }
}
