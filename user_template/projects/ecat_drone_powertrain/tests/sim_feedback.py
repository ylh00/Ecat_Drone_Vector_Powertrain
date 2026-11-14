#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ecat_drone_powertrain — 反馈报文模拟器 / Feedback frame simulator
=================================================================

生成与固件完全一致的“发动机（AM32 电调）+ 总线舵机 + EtherCAT/USB 遥测”
反馈报文，用于：

  * 没有实物板时测试上位机 / 主站解析
  * 对照示波器/逻辑分析仪的实测波形做逐字节比对
  * 回归测试（帧格式、CRC、字节序）

输出四种格式（--format 选择，默认 all）：

  dshot  双向 DShot 回传帧：12bit eRPM 载荷 + 4bit 反相 CRC = 16bit、
         以及 GCR+差分编码后的 21bit 线上电平序列
  servo  S288/J288 舵机 26 字节反馈帧（0xFC 0xEE ... CRC32），单总线 6Mbps
  pdo    EtherCAT TxPDO（48 字节，与 inc/ecat_pdo.h 一致）
  usb    USB CDC 主机协议 GET_TELEMETRY 响应帧（status + 54B 遥测 + CRC16）

用法:
  python tests/sim_feedback.py                     # 10 帧，可读摘要
  python tests/sim_feedback.py --frames 3 --format servo
  python tests/sim_feedback.py --hex-only          # 只打印十六进制报文
  python tests/sim_feedback.py --selftest          # 校验算法（必须 PASS）
  python tests/sim_feedback.py --json out.json     # 机器可读输出

仅使用 Python 标准库，无需任何依赖。
"""

import argparse
import json
import struct
import sys

# ---------------------------------------------------------------------------
# 1. 舵机协议：CRC32 + 26 字节反馈帧
# ---------------------------------------------------------------------------
# 手册 crc32_core(): poly 0x04C11DB7, init 0xFFFFFFFF, MSB first,
# 对 little-endian 32bit 字逐位处理（等价于“字节按 32bit 小端分组”）

PI = 3.141592653589793


def crc32_servo(data: bytes) -> int:
    """S288/J288 协议 CRC32（手册 §九 p.9-10 的 crc32_core 等价实现）。"""
    if len(data) % 4 != 0:
        raise ValueError("servo crc32 needs a multiple of 4 bytes")
    crc = 0xFFFFFFFF
    poly = 0x04C11DB7
    for i in range(0, len(data), 4):
        word = data[i] | (data[i + 1] << 8) | (data[i + 2] << 16) | (data[i + 3] << 24)
        for bit in range(31, -1, -1):
            if crc & 0x80000000:
                crc = ((crc << 1) & 0xFFFFFFFF) ^ poly
            else:
                crc = (crc << 1) & 0xFFFFFFFF
            if word & (1 << bit):
                crc ^= poly
    return crc & 0xFFFFFFFF


def s288_feedback_frame(servo_id=1, mode=1, timeout_flag=0,
                        temp_c=25, sensor_c=40, voltage_raw=48,
                        torque_raw=12800, speed_raw=1280, pos_raw=0x4000,
                        m_error=0x00040001, outpos=4096, exflag=1,
                        reserved=0) -> bytes:
    """构造 26 字节舵机反馈帧（线上字节，不含结构体 NoUse 填充）。

    线上布局（手册 p.8-9，'线上偏移 = 结构体偏移 - 2'）：
      0-1   head 0xFC 0xEE（不参与 CRC）
      2     id:4 | status:3 | timeout:1
      3     temp   int8  °C
      4     sensor uint8 绕组温度 °C
      5     vol    uint8 0.5V/LSB (255 = 127.5V)
      6-7   torque int16 (256000 = 1 Nm)
      8-9   speed  int16 (2.56/2π = 1 rad/s)
      10-13 pos    int32 (32768/2π = 1 rad, 转子)
      14-17 MError uint32
      18-19 OutPos:13 | ExFlag:3 (uint16 LE)
      20-21 res    uint16
      22-25 CRC32 (覆盖 2..21，共 20 字节)
    """
    mode_byte = ((servo_id & 0xF) | ((mode & 0x7) << 4) | ((timeout_flag & 0x1) << 7))
    outpos_exflag = ((outpos & 0x1FFF) | ((exflag & 0x7) << 13)) & 0xFFFF
    body = bytearray(26)
    body[0] = 0xFC
    body[1] = 0xEE
    body[2] = mode_byte & 0xFF
    body[3] = temp_c & 0xFF
    body[4] = sensor_c & 0xFF
    body[5] = voltage_raw & 0xFF
    body[6:8] = struct.pack('<h', torque_raw)
    body[8:10] = struct.pack('<h', speed_raw)
    body[10:14] = struct.pack('<i', pos_raw)
    body[14:18] = struct.pack('<I', m_error)
    body[18:20] = struct.pack('<H', outpos_exflag)
    body[20:22] = struct.pack('<H', reserved)
    body[22:26] = struct.pack('<I', crc32_servo(bytes(body[2:22])))
    return bytes(body)


def decode_s288_feedback(frame: bytes) -> dict:
    """解析 26 字节反馈帧（用于自检/展示）。"""
    if len(frame) != 26:
        raise ValueError("feedback frame must be 26 bytes")
    mode = frame[2]
    outpos_exflag = struct.unpack('<H', frame[18:20])[0]
    outpos = outpos_exflag & 0x1FFF
    crc_rx = struct.unpack('<I', frame[22:26])[0]
    crc_ok = crc_rx == crc32_servo(bytes(frame[2:22]))
    return {
        'head': ' '.join('%02X' % b for b in frame[0:2]),
        'id': mode & 0xF,
        'mode': (mode >> 4) & 0x7,
        'timeout_flag': (mode >> 7) & 1,
        'temp_c': struct.unpack('<b', frame[3:4])[0],
        'sensor_c': frame[4],
        'voltage_v': frame[5] * 0.5,
        'torque_Nm': struct.unpack('<h', frame[6:8])[0] / 256000.0,
        'speed_rad_s': struct.unpack('<h', frame[8:10])[0] * (2 * PI / 2.56),
        'pos_rad_rotor': struct.unpack('<i', frame[10:14])[0] * (2 * PI / 32768.0),
        'm_error': struct.unpack('<I', frame[14:18])[0],
        'outpos': outpos,
        'outpos_deg': outpos * 360.0 / 8192.0,
        'exflag': (outpos_exflag >> 13) & 0x7,
        'reserved': struct.unpack('<H', frame[20:22])[0],
        'crc32': crc_rx,
        'crc_ok': crc_ok,
    }


# ---------------------------------------------------------------------------
# 2. 双向 DShot 遥测帧
# ---------------------------------------------------------------------------
GCR_MAP = [0x19, 0x1B, 0x12, 0x13, 0x1D, 0x15, 0x16, 0x17,
           0x1A, 0x09, 0x0A, 0x0B, 0x1E, 0x0D, 0x0E, 0x0F]


def dshot_crc_bidir(data12: int) -> int:
    """双向 DShot 取反 CRC：~(d ^ d>>4 ^ d>>8) & 0xF（命令帧/回传帧通用）。"""
    return (~(data12 ^ (data12 >> 4) ^ (data12 >> 8))) & 0xF


def dshot_telemetry_encode(erpm: int) -> tuple:
    """把 eRPM 编码为 12bit 遥测载荷（eee mmmmmmmmm, period_us = m<<e）。

    返回 (telemetry12, period_us)。eRPM=0 时返回“停转”0x0FFF。
    """
    if erpm <= 0:
        return 0x0FFF, 0
    period_us = int(round(60_000_000.0 / erpm))
    if period_us <= 0:
        return 0x0FFF, 0
    best = None
    for e in range(8):
        for m in range(1, 512):
            p = m << e
            err = abs(p - period_us)
            if best is None or err < best[0]:
                best = (err, e, m, p)
    _, e, m, p = best
    return ((e << 9) | m) & 0x0FFF, p


def dshot_telemetry_value(erpm: int) -> tuple:
    """返回 (16bit 线上值, 12bit 载荷, period_us)。"""
    tele, period = dshot_telemetry_encode(erpm)
    value = ((tele << 4) | dshot_crc_bidir(tele)) & 0xFFFF
    return value, tele, period


def dshot_decode_value(value16: int) -> tuple:
    """解码 16bit 回传帧 -> (erpm, telemetry12, period_us, crc_ok)。"""
    tele = (value16 >> 4) & 0x0FFF
    crc_rx = value16 & 0xF
    crc_ok = dshot_crc_bidir(tele) == crc_rx
    if not crc_ok:
        return 0, tele, 0, False
    if tele == 0x0FFF:
        return 0, tele, 0, True
    e = (tele >> 9) & 0x7
    m = tele & 0x1FF
    period = m << e
    erpm = int(60_000_000 / period) if period else 0
    return erpm, tele, period, True


def dshot_wire_bits(value16: int) -> str:
    """16bit 值 -> 21bit 线上差分电平序列（GCR 20bit + 起始位 1）。

    返回 '0'/'1' 组成的 21 字符串（线上电平，从 idle 高开始：
    逻辑 1 = 该位槽翻转，逻辑 0 = 保持）。
    """
    gcr = 0
    for shift in (12, 8, 4, 0):
        nib = (value16 >> shift) & 0xF
        gcr = (gcr << 5) | GCR_MAP[nib]
    logic = [1] + [(gcr >> (19 - i)) & 1 for i in range(20)]
    level = 1
    levels = []
    for b in logic:
        if b:
            level ^= 1
        levels.append(level)
    return ''.join(str(x) for x in levels)


def dshot_frame_line(erpm: int) -> str:
    """格式化一行 DShot 回传帧描述。"""
    value, tele, period = dshot_telemetry_value(erpm)
    wires = dshot_wire_bits(value)
    return ('eRPM=%6d  tele=0x%03X  period=%4d us  frame=0x%04X  wire(21bit)=%s'
            % (erpm, tele, period, value, wires))


# ---------------------------------------------------------------------------
# 3. EtherCAT TxPDO（48 字节，inc/ecat_pdo.h）
# ---------------------------------------------------------------------------
def build_txpdo(status_word, fault_code, esc_erpm, esc_tlm_period,
                servo_pos_d10, servo_speed_rpm, servo_current_ma,
                servo_temp_c, servo_error, sys_voltage_dv, sys_current_ca,
                esc_current_ma, loop_counter, led_status, ecat_link_status,
                reserved0=0) -> bytes:
    out = bytearray()
    out += struct.pack('<H', status_word & 0xFFFF)
    out += struct.pack('<H', fault_code & 0xFFFF)
    out += struct.pack('<II', esc_erpm[0] & 0xFFFFFFFF, esc_erpm[1] & 0xFFFFFFFF)
    out += struct.pack('<HH', esc_tlm_period[0] & 0xFFFF, esc_tlm_period[1] & 0xFFFF)
    out += struct.pack('<hh', servo_pos_d10[0], servo_pos_d10[1])
    out += struct.pack('<hh', servo_speed_rpm[0], servo_speed_rpm[1])
    out += struct.pack('<hh', servo_current_ma[0], servo_current_ma[1])
    out += struct.pack('<BB', servo_temp_c[0] & 0xFF, servo_temp_c[1] & 0xFF)
    out += struct.pack('<BB', servo_error[0] & 0xFF, servo_error[1] & 0xFF)
    out += struct.pack('<H', sys_voltage_dv & 0xFFFF)
    out += struct.pack('<h', sys_current_ca)
    out += struct.pack('<HH', esc_current_ma[0] & 0xFFFF, esc_current_ma[1] & 0xFFFF)
    out += struct.pack('<H', loop_counter & 0xFFFF)
    out += struct.pack('<H', led_status & 0xFFFF)
    out += struct.pack('<H', ecat_link_status & 0xFFFF)
    out += struct.pack('<H', reserved0 & 0xFFFF)
    assert len(out) == 48, len(out)
    return bytes(out)


# ---------------------------------------------------------------------------
# 4. USB 主机协议（inc/host_proto.h）：CRC16 + GET_TELEMETRY 响应帧
# ---------------------------------------------------------------------------
def crc16_ccitt_false(data: bytes) -> int:
    crc = 0xFFFF
    for b in data:
        crc ^= b << 8
        for _ in range(8):
            if crc & 0x8000:
                crc = ((crc << 1) ^ 0x1021) & 0xFFFF
            else:
                crc = (crc << 1) & 0xFFFF
    return crc


def build_host_telemetry(status_word, fault_code, esc_erpm, esc_tlm_period,
                         servo_pos_d10, servo_speed_rpm, servo_current_ma,
                         servo_temp_c, servo_error, sys_voltage_dv,
                         sys_current_ca, esc_current_ma, loop_counter,
                         led_status, ecat_link_status, host_rx_frames,
                         host_crc_errors) -> bytes:
    out = bytearray()
    out += struct.pack('<H', status_word & 0xFFFF)
    out += struct.pack('<H', fault_code & 0xFFFF)
    out += struct.pack('<II', esc_erpm[0], esc_erpm[1])
    out += struct.pack('<HH', esc_tlm_period[0], esc_tlm_period[1])
    out += struct.pack('<hh', servo_pos_d10[0], servo_pos_d10[1])
    out += struct.pack('<hh', servo_speed_rpm[0], servo_speed_rpm[1])
    out += struct.pack('<hh', servo_current_ma[0], servo_current_ma[1])
    out += struct.pack('<BB', servo_temp_c[0], servo_temp_c[1])
    out += struct.pack('<BB', servo_error[0], servo_error[1])
    out += struct.pack('<H', sys_voltage_dv & 0xFFFF)
    out += struct.pack('<h', sys_current_ca)
    out += struct.pack('<HH', esc_current_ma[0], esc_current_ma[1])
    out += struct.pack('<H', loop_counter & 0xFFFF)
    out += struct.pack('<H', led_status & 0xFFFF)
    out += struct.pack('<H', ecat_link_status & 0xFFFF)
    out += struct.pack('<II', host_rx_frames, host_crc_errors)
    assert len(out) == 54, len(out)
    return bytes(out)


def build_host_frame(cmd: int, seq: int, payload: bytes) -> bytes:
    """A5 5A | CMD | SEQ | LEN(L,H) | PAYLOAD | CRC16(L,H)（CRC 覆盖 CMD..PAYLOAD）。"""
    body = bytes([cmd & 0xFF, seq & 0xFF]) + struct.pack('<H', len(payload)) + payload
    crc = crc16_ccitt_false(body)
    return b'\xA5\x5A' + body + struct.pack('<H', crc)


def hexs(data: bytes) -> str:
    return ' '.join('%02X' % b for b in data)


# ---------------------------------------------------------------------------
# 5. 时间序列（模拟“当前”运行状态的 10 帧反馈，10 ms 一帧 = 100 Hz）
# ---------------------------------------------------------------------------
def simulate_state(frame_index: int, total: int) -> dict:
    """生成第 frame_index 帧的运行状态（电机爬升 + 舵机转动 + ADC 波动）。"""
    t = frame_index / float(max(total - 1, 1))
    if frame_index == 0:
        erpm = (0, 0)
    else:
        erpm = (int(35000 * t), int(28000 * t))
    out_deg = 90.0 * t
    outpos = int(round(out_deg / 360.0 * 8192)) & 0x1FFF
    pos_d10 = int(round(outpos * 3600.0 / 8192))
    rotor_pos_raw = int(round(out_deg / 360.0 * 288.35 * 32768.0))
    servo_speed_rad_s = 30.0 * (2 * PI / 60.0)      # 输出轴 30 rpm
    rotor_rad_s = servo_speed_rad_s * 288.35
    speed_raw = int(round(rotor_rad_s * 2.56 / (2 * PI)))
    torque_raw = int(round(0.02 * 256000))          # 0.02 Nm
    return {
        'erpm': erpm,
        'servo': {
            'outpos': outpos,
            'out_deg': out_deg,
            'pos_d10': pos_d10,
            'rotor_pos_raw': rotor_pos_raw,
            'speed_raw': speed_raw,
            'torque_raw': torque_raw,
            'temp_c': 30 + int(t * 10),
            'sensor_c': 35 + int(t * 5),
            'voltage_raw': int(round(48.0 + t * 2)),   # 24.0 V -> 25.0 V
        },
        'sys_voltage_dv': int(240 + t * 6),             # 24.0 V -> 24.6 V
        'sys_current_ca': int(1200 + t * 3500),         # 12 A -> 47 A
        'esc_current_ma': (int(5000 + t * 9000), int(4500 + t * 7500)),
        'loop_counter': 1000 + frame_index,
    }


def build_all_frames(total=10) -> list:
    frames = []
    for i in range(total):
        st = simulate_state(i, total)
        erpm = st['erpm']
        tlm = [dshot_telemetry_encode(erpm[0]), dshot_telemetry_encode(erpm[1])]
        dshot_frames = [dshot_telemetry_value(erpm[0]), dshot_telemetry_value(erpm[1])]
        sv = st['servo']
        servo1 = s288_feedback_frame(servo_id=1, mode=1, timeout_flag=1,
                                     temp_c=sv['temp_c'], sensor_c=sv['sensor_c'],
                                     voltage_raw=sv['voltage_raw'],
                                     torque_raw=sv['torque_raw'],
                                     speed_raw=sv['speed_raw'],
                                     pos_raw=sv['rotor_pos_raw'],
                                     m_error=0, outpos=sv['outpos'], exflag=0)
        servo2 = s288_feedback_frame(servo_id=2, mode=1, timeout_flag=1,
                                     temp_c=sv['temp_c'] + 2, sensor_c=sv['sensor_c'] + 1,
                                     voltage_raw=sv['voltage_raw'],
                                     torque_raw=sv['torque_raw'] // 2,
                                     speed_raw=sv['speed_raw'],
                                     pos_raw=int(sv['rotor_pos_raw'] / 2),
                                     m_error=0, outpos=(sv['outpos'] + 1024) & 0x1FFF, exflag=0)
        status_word = (0x01 | 0x02 | 0x08 | 0x10 | 0x20 | 0x40) if i > 0 else 0x01
        txpdo = build_txpdo(
            status_word=status_word,
            fault_code=0,
            esc_erpm=(erpm[0], erpm[1]),
            esc_tlm_period=(tlm[0][1], tlm[1][1]),
            servo_pos_d10=(sv['pos_d10'], sv['pos_d10']),
            servo_speed_rpm=(30, 30),
            servo_current_ma=(200, 120),
            servo_temp_c=(sv['temp_c'], sv['temp_c'] + 2),
            servo_error=(0, 0),
            sys_voltage_dv=st['sys_voltage_dv'],
            sys_current_ca=st['sys_current_ca'],
            esc_current_ma=st['esc_current_ma'],
            loop_counter=st['loop_counter'],
            led_status=0x0787,
            ecat_link_status=0x0063,
        )
        usb_body = build_host_telemetry(
            status_word=status_word, fault_code=0,
            esc_erpm=(erpm[0], erpm[1]),
            esc_tlm_period=(tlm[0][1], tlm[1][1]),
            servo_pos_d10=(sv['pos_d10'], sv['pos_d10']),
            servo_speed_rpm=(30, 30),
            servo_current_ma=(200, 120),
            servo_temp_c=(sv['temp_c'], sv['temp_c'] + 2),
            servo_error=(0, 0),
            sys_voltage_dv=st['sys_voltage_dv'],
            sys_current_ca=st['sys_current_ca'],
            esc_current_ma=st['esc_current_ma'],
            loop_counter=st['loop_counter'],
            led_status=0x0787,
            ecat_link_status=0x0063,
            host_rx_frames=1234 + i,
            host_crc_errors=0,
        )
        usb_frame = build_host_frame(0x03, i, b'\x00' + usb_body)
        frames.append({
            'index': i,
            'state': st,
            'dshot_value': [dshot_frames[0][0], dshot_frames[1][0]],
            'dshot_tele': [dshot_frames[0][1], dshot_frames[1][1]],
            'dshot_period': [dshot_frames[0][2], dshot_frames[1][2]],
            'servo1': servo1,
            'servo2': servo2,
            'txpdo': txpdo,
            'usb': usb_frame,
        })
    return frames


# ---------------------------------------------------------------------------
# 6. 输出 / 自检
# ---------------------------------------------------------------------------
def print_frames(frames, fmt='all', hex_only=False):
    for f in frames:
        st = f['state']
        print('=' * 100)
        print('frame %d  (t=%d ms)  ESC1 eRPM=%d  ESC2 eRPM=%d  servo out=%.1f deg'
              % (f['index'], f['index'] * 10, st['erpm'][0], st['erpm'][1],
                 st['servo']['out_deg']))
        if fmt in ('all', 'dshot'):
            if hex_only:
                print('DShot  : ESC1 %s   ESC2 %s'
                      % (hexs(struct.pack('<H', f['dshot_value'][0])),
                         hexs(struct.pack('<H', f['dshot_value'][1]))))
            else:
                print('DShot  : ESC1 ' + dshot_frame_line(st['erpm'][0]))
                print('         ESC2 ' + dshot_frame_line(st['erpm'][1]))
        if fmt in ('all', 'servo'):
            print('Servo1 : ' + hexs(f['servo1']))
            if not hex_only:
                d = decode_s288_feedback(f['servo1'])
                print('         id=%d mode=%d temp=%dC vol=%.1fV torque=%.3fNm '
                      'outpos=%.1fdeg crc_ok=%s'
                      % (d['id'], d['mode'], d['temp_c'], d['voltage_v'],
                         d['torque_Nm'], d['outpos_deg'], d['crc_ok']))
            print('Servo2 : ' + hexs(f['servo2']))
        if fmt in ('all', 'pdo'):
            print('TxPDO  : ' + hexs(f['txpdo']))
        if fmt in ('all', 'usb'):
            print('USB rsp: ' + hexs(f['usb']))


def selftest() -> bool:
    ok = True

    def check(name, cond, detail=''):
        nonlocal ok
        print('[%s] %s %s' % ('PASS' if cond else 'FAIL', name, detail))
        if not cond:
            ok = False

    # 1) 舵机 CRC32 与固件文档中的示例帧一致（docs/SERVO_S288.md §3.6）
    worked = bytes.fromhex(
        'FC EE 11 19 28 30 00 32 00 05 00 40 00 00 01 00 04 00 00 30 00 00 27 65 98 1F')
    check('S288 CRC32 == 文档示例帧 CRC(0x1F986527)',
          crc32_servo(worked[2:22]) == 0x1F986527)
    d = decode_s288_feedback(worked)
    check('S288 示例帧解析 (id=%d outpos=%.1fdeg)' % (d['id'], d['outpos_deg']),
          d['id'] == 1 and abs(d['outpos_deg'] - 180.0) < 0.1 and d['crc_ok'])

    # 2) 自建舵机帧可被解析、CRC 正确
    f = s288_feedback_frame(servo_id=2, outpos=683, temp_c=31, voltage_raw=48)
    d = decode_s288_feedback(f)
    check('S288 自建帧 CRC 正确', d['crc_ok'])
    check('S288 自建帧 id/角度', d['id'] == 2 and abs(d['outpos_deg'] - 30.0) < 0.1)

    # 3) DShot eRPM 编解码往返 + 文档基准
    for erpm in (0, 35000, 69930, 123456):
        val, tele, period = dshot_telemetry_value(erpm)
        dec, tele2, period2, crc_ok = dshot_decode_value(val)
        expected = 0 if erpm == 0 else erpm
        tol = 0.0 if erpm == 0 else max(2.0, erpm * 0.02)
        check('DShot eRPM=%d 往返 (dec=%d, tele=0x%03X, frame=0x%04X)'
              % (erpm, dec, tele, val),
              crc_ok and abs(dec - expected) <= tol)
    val, tele, period = dshot_telemetry_value(35000)
    # 35000 eRPM -> 周期 1714.3us，12bit 编码可取 0x5AC(1712us) 或 0x5AD(1716us)，
    # 两者解码误差都 <0.2%，均为合法编码（编码端由 ESC 决定）。
    check('DShot 35000 eRPM -> tele 0x5AC/0x5AD (period 1712/1716us)',
          tele in (0x5AC, 0x5AD) and dshot_decode_value(val)[3],
          'got 0x%03X/0x%04X' % (tele, val))
    check('DShot 停转 -> 0x0FFF', dshot_telemetry_encode(0)[0] == 0x0FFF)
    w = dshot_wire_bits(val)
    check('DShot 21bit 线序长度/起始下降沿', len(w) == 21 and w[0] == '0', w)

    # 4) TxPDO / 遥测长度与 CRC
    frames = build_all_frames(2)
    check('EtherCAT TxPDO == 48 字节', len(frames[0]['txpdo']) == 48)
    check('USB GET_TELEMETRY 响应长度 == 6+55+2 = 63',
          len(frames[0]['usb']) == 63)
    check('USB 帧 CRC16 正确', crc16_ccitt_false(frames[0]['usb'][2:61]) ==
          struct.unpack('<H', frames[0]['usb'][61:63])[0])
    check('USB 遥测载荷长度 == 54', len(frames[0]['usb'][7:61]) == 54)

    print('\nSELFTEST %s' % ('PASS' if ok else 'FAIL'))
    return ok


def main():
    ap = argparse.ArgumentParser(description='ecat_drone_powertrain feedback frame simulator')
    ap.add_argument('--frames', type=int, default=10, help='帧数（默认 10）')
    ap.add_argument('--format', default='all',
                    choices=['all', 'dshot', 'servo', 'pdo', 'usb'], help='输出格式')
    ap.add_argument('--hex-only', action='store_true', help='只输出十六进制报文')
    ap.add_argument('--json', default='', help='同时把帧写入 JSON 文件')
    ap.add_argument('--selftest', action='store_true', help='运行算法自检')
    ap.add_argument('--crc32', default='', help='计算 4 的倍数长度 hex 数据的舵机 CRC32')
    args = ap.parse_args()

    if args.crc32:
        data = bytes.fromhex(args.crc32.replace(' ', ''))
        print('CRC32 = 0x%08X' % crc32_servo(data))
        return 0

    if args.selftest:
        return 0 if selftest() else 1

    frames = build_all_frames(max(args.frames, 1))
    if args.hex_only:
        for f in frames:
            print('%02d DShot1=%s DShot2=%s Servo1=%s Servo2=%s TxPDO=%s USB=%s'
                  % (f['index'],
                     hexs(struct.pack('<H', f['dshot_value'][0])),
                     hexs(struct.pack('<H', f['dshot_value'][1])),
                     hexs(f['servo1']), hexs(f['servo2']),
                     hexs(f['txpdo']), hexs(f['usb'])))
    else:
        print_frames(frames, args.format)

    if args.json:
        out = []
        for f in frames:
            out.append({
                'index': f['index'],
                'dshot_value': f['dshot_value'],
                'dshot_telemetry': f['dshot_tele'],
                'dshot_period_us': f['dshot_period'],
                'servo1_hex': hexs(f['servo1']),
                'servo2_hex': hexs(f['servo2']),
                'txpdo_hex': hexs(f['txpdo']),
                'usb_frame_hex': hexs(f['usb']),
            })
        with open(args.json, 'w', encoding='utf-8') as fp:
            json.dump(out, fp, ensure_ascii=False, indent=2)
        print('\nJSON written: %s' % args.json)
    return 0


if __name__ == '__main__':
    sys.exit(main())
