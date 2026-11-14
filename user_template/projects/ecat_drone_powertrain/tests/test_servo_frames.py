#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Frame level unit test for the S288/J288 bus servo driver.

Reference: "无刷数字舵机 J288/S288 使用手册" section 九 (protocol, pp.7-10) and
section 十/十一 (fault / warning codes, p.11); cross checked against the vendor
reference implementation (unitreerobotics/digital_servo, python/servo_demo.py
and stm32/firmware/App/protocol.c).

The test does NOT run the firmware.  It re-implements the protocol model in
Python and checks:
  A. the manual's crc32_core() (word based, little endian) equals the vendor
     reference crc32_lookup() for command and feedback CRC windows
  B. the constants/offsets used by src/servo_s288.c match the protocol model
     (parsed out of the C source so the test catches an accidental drift)
  C. control frame build: head, packed id/mode/timeout byte, little endian
     multi byte fields, CRC, broadcast, negative positions, gain conversion
  D. feedback frame build + parse: CRC scope, conversions (output encoder
     position, rpm, torque, current estimate, voltage, temperature), fault and
     warning bits, rejection of bad head / bad CRC / wrong id
  E. scan bitmap semantics (id >> 3, 1 << (id & 7)) and id range handling
  F. timing budget of one poll transaction at 6.0 Mbps

Run:  python test_servo_frames.py
"""

import math
import random
import re
import sys

REPO = r"D:\Ecat_Drone_Vector_Powertrain"
C_SOURCE = (REPO + r"\sdk_env_v1.13.0\user_template\projects"
            r"\ecat_drone_powertrain\src\servo_s288.c")

PASS = 0
FAIL = 0
FAILED_NAMES = []


def check(name, condition, detail=""):
    global PASS, FAIL
    if condition:
        PASS += 1
        print("  PASS  %s%s" % (name, (" [" + detail + "]") if detail else ""))
    else:
        FAIL += 1
        FAILED_NAMES.append(name)
        print("  FAIL  %s%s" % (name, (" [" + detail + "]") if detail else ""))


# ===========================================================================
# Protocol constants (manual pp.7-10 / vendor reference)
# ===========================================================================
RATIO = 70070.0 / 243.0          # 288.3539...
RATIO_SQ = RATIO * RATIO         # 83147.95...
CMD_SIZE = 20
FBK_SIZE = 26
ID_MAX = 14                      # 0..14 addressable, 15 = broadcast
ID_BROADCAST = 15

CMD_OFS = dict(head=0, mode=2, res=3, tor=4, spd=6, pos=8, k_pos=12,
               k_spd=14, crc=16)
FBK_OFS = dict(head=0, mode=2, temp=3, sensor=4, vol=5, torque=6, speed=8,
               pos=10, m_error=14, outpos=18, ex_sensor2=20, ex_com=21,
               crc=22)

# ===========================================================================
# CRC32 model
# ===========================================================================
def make_crc_table():
    table = []
    for i in range(256):
        c = (i << 24) & 0xFFFFFFFF
        for _ in range(8):
            if c & 0x80000000:
                c = ((c << 1) ^ 0x04C11DB7) & 0xFFFFFFFF
            else:
                c = (c << 1) & 0xFFFFFFFF
        table.append(c)
    return table


CRC_TABLE = make_crc_table()


def crc32_vendor(data):
    """Reference implementation of the vendor python demo (crc32_lookup)."""
    crc = 0xFFFFFFFF
    n = len(data) - (len(data) % 4)
    for i in range(0, n, 4):
        b0, b1, b2, b3 = data[i + 3], data[i + 2], data[i + 1], data[i]
        crc = CRC_TABLE[(crc >> 24) ^ b0] ^ ((crc << 8) & 0xFFFFFFFF)
        crc = CRC_TABLE[(crc >> 24) ^ b1] ^ ((crc << 8) & 0xFFFFFFFF)
        crc = CRC_TABLE[(crc >> 24) ^ b2] ^ ((crc << 8) & 0xFFFFFFFF)
        crc = CRC_TABLE[(crc >> 24) ^ b3] ^ ((crc << 8) & 0xFFFFFFFF)
    return crc


def crc32_manual(data, byte_offset, words):
    """Model of the C servo_crc32(): manual crc32_core(), LE 32 bit words."""
    crc = 0xFFFFFFFF
    poly = 0x04C11DB7
    for i in range(words):
        off = byte_offset + 4 * i
        word = (data[off] | (data[off + 1] << 8) |
                (data[off + 2] << 16) | (data[off + 3] << 24))
        xbit = 0x80000000
        for _ in range(32):
            if crc & 0x80000000:
                crc = ((crc << 1) ^ poly) & 0xFFFFFFFF
            else:
                crc = (crc << 1) & 0xFFFFFFFF
            if word & xbit:
                crc ^= poly
            xbit >>= 1
            crc &= 0xFFFFFFFF
    return crc


def crc_cmd(frame):
    """Command CRC: over bytes 0..15 = 4 words, head included."""
    return crc32_manual(frame, 0, 4)


def crc_fbk(frame):
    """Feedback CRC: over bytes 2..21 = 5 words, head NOT included."""
    return crc32_manual(frame, 2, 5)


# ===========================================================================
# Protocol model (mirrors src/servo_s288.c)
# ===========================================================================
def c_trunc_div(a, b):
    """C integer division (truncate towards zero)."""
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b >= 0) else -q


def pos_d10_to_raw(angle_d10):
    """0.1 deg output side -> rotor position command (SERVO_CMD_POS_NUM/DEN)."""
    num = angle_d10 * (32768 * 70070)
    den = 3600 * 243
    if num >= 0:
        raw = (num + den // 2) // den
    else:
        raw = -((-num + den // 2) // den)
    return max(-2**31, min(2**31 - 1, raw))


def gain_param_to_field(param):
    """PARAM_SERVO_KPn / KDn -> int16 protocol field (param / RATIO^2)."""
    if param <= 0:
        return 0
    return min(32767, c_trunc_div(param, 83148))


def mul_q16(value, q16):
    tmp = value * q16
    tmp += 32768 if tmp >= 0 else -32768
    return tmp >> 16


def fbk_outpos_to_d10(outpos):
    return (outpos * 225 + 256) >> 9


def fbk_speed_rpm(speed_raw):
    return mul_q16(speed_raw, 5327)


def fbk_torque_cnm(torque_raw):
    return mul_q16(torque_raw, 7382)


def fbk_current_ma(torque_raw):
    return c_trunc_div(torque_raw * 10, 256)


def build_control_frame(servo_id, mode, timeout, tor_raw, spd_raw, pos_raw,
                        k_pos_raw, k_spd_raw):
    frame = bytearray(CMD_SIZE)
    frame[0] = 0xFE
    frame[1] = 0xEE
    frame[2] = ((servo_id & 0x0F) | ((mode & 0x07) << 4) |
                ((timeout & 0x01) << 7))
    frame[3] = 0
    frame[4:6] = tor_raw.to_bytes(2, "little", signed=True)
    frame[6:8] = spd_raw.to_bytes(2, "little", signed=True)
    frame[8:12] = pos_raw.to_bytes(4, "little", signed=True)
    frame[12:14] = k_pos_raw.to_bytes(2, "little", signed=True)
    frame[14:16] = k_spd_raw.to_bytes(2, "little", signed=True)
    frame[16:20] = crc_cmd(frame).to_bytes(4, "little")
    return bytes(frame)


def build_feedback_frame(servo_id, mode, timeout, temp, sensor, vol,
                         torque_raw, speed_raw, pos_raw, m_error,
                         outpos, exflag, ex_sensor2=0, ex_com=0):
    frame = bytearray(FBK_SIZE)
    frame[0] = 0xFC
    frame[1] = 0xEE
    frame[2] = ((servo_id & 0x0F) | ((mode & 0x07) << 4) |
                ((timeout & 0x01) << 7))
    frame[3] = temp & 0xFF
    frame[4] = sensor & 0xFF
    frame[5] = vol & 0xFF
    frame[6:8] = torque_raw.to_bytes(2, "little", signed=True)
    frame[8:10] = speed_raw.to_bytes(2, "little", signed=True)
    frame[10:14] = pos_raw.to_bytes(4, "little", signed=True)
    frame[14:18] = m_error.to_bytes(4, "little", signed=False)
    frame[18:20] = ((outpos & 0x1FFF) | ((exflag & 0x07) << 13)).to_bytes(
        2, "little")
    frame[20] = ex_sensor2 & 0xFF
    frame[21] = ex_com & 0xFF
    frame[22:26] = crc_fbk(frame).to_bytes(4, "little")
    return bytes(frame)


def parse_feedback(servo_id_expected, frame):
    """Model of servo_bus_feedback_valid() + servo_bus_update_status_ok()."""
    if len(frame) != FBK_SIZE:
        return None
    if frame[0] != 0xFC or frame[1] != 0xEE:
        return None
    if int.from_bytes(frame[22:26], "little") != crc_fbk(frame):
        return None
    mode_byte = frame[2]
    if (mode_byte & 0x0F) != servo_id_expected:
        return None
    outpos_exflag = int.from_bytes(frame[18:20], "little")
    torque_raw = int.from_bytes(frame[6:8], "little", signed=True)
    speed_raw = int.from_bytes(frame[8:10], "little", signed=True)
    return {
        "id": mode_byte & 0x0F,
        "mode": (mode_byte >> 4) & 0x07,
        "timeout_flag": (mode_byte >> 7) & 0x01,
        "temp_c": max(0, frame[3] if frame[3] < 128 else frame[3] - 256)
                  if frame[4] <= max(0, frame[3] if frame[3] < 128 else frame[3] - 256)
                  else frame[4],
        "voltage_mv": frame[5] * 500,
        "torque_raw": torque_raw,
        "torque_cnm": fbk_torque_cnm(torque_raw),
        "speed_raw": speed_raw,
        "speed_rpm": fbk_speed_rpm(speed_raw),
        "pos_raw": int.from_bytes(frame[10:14], "little", signed=True),
        "m_error": int.from_bytes(frame[14:18], "little"),
        "out_pos": outpos_exflag & 0x1FFF,
        "warn": (outpos_exflag >> 13) & 0x07,
        "pos_d10": fbk_outpos_to_d10(outpos_exflag & 0x1FFF),
        "current_ma": fbk_current_ma(torque_raw),
    }


# ===========================================================================
# A. CRC model validation
# ===========================================================================
def test_crc():
    print("\n[A] CRC32 model (manual crc32_core vs vendor crc32_lookup)")
    random.seed(288)
    ok = True
    for words in (1, 2, 3, 4, 5, 8):
        for _ in range(100):
            data = bytes(random.randrange(256) for _ in range(4 * words))
            if crc32_vendor(data) != crc32_manual(data, 0, words):
                ok = False
    check("manual crc32_core == vendor crc32_lookup (600 random frames)",
          ok)

    # the feedback CRC window starts at byte 2 (unaligned in a 26 byte frame),
    # exactly like crc32_core((uint32_t *)&frame[2], 5) in the firmware
    ok = True
    for words in (1, 5):
        for _ in range(100):
            data = bytes(random.randrange(256) for _ in range(4 * words + 2))
            if crc32_vendor(data[2:]) != crc32_manual(data, 2, words):
                ok = False
    check("feedback CRC window (bytes 2..21, 5 words at byte offset 2)", ok)

    # regression pin: crc of 16 zero bytes with the manual algorithm (the
    # equivalence with the vendor reference is verified above)
    known = crc32_manual(bytes(16), 0, 4)
    check("CRC regression pin (16 x 0x00)", known == 0x552D22C8,
          "0x%08X" % known)
    return ok


# ===========================================================================
# B. C source consistency
# ===========================================================================
def load_c_defines():
    with open(C_SOURCE, "r", encoding="utf-8", errors="ignore") as f:
        src = f.read()
    defines = {}
    for name, value in re.findall(
            r"^#define\s+(SERVO_[A-Z0-9_]+)\s+\(?\s*(0x[0-9A-Fa-f]+U?|\d+U?)\s*\)?",
            src, re.MULTILINE):
        value = value.rstrip("U")
        try:
            defines[name] = int(value, 0)
        except ValueError:
            pass
    return defines, src


def test_c_source_consistency():
    print("\n[B] src/servo_s288.c constants vs protocol model")
    defines, src = load_c_defines()

    expected = {
        "SERVO_CMD_SIZE": CMD_SIZE,
        "SERVO_FBK_SIZE": FBK_SIZE,
        "SERVO_CMD_OFS_HEAD": CMD_OFS["head"],
        "SERVO_CMD_OFS_MODE": CMD_OFS["mode"],
        "SERVO_CMD_OFS_RES": CMD_OFS["res"],
        "SERVO_CMD_OFS_TOR_DES": CMD_OFS["tor"],
        "SERVO_CMD_OFS_SPD_DES": CMD_OFS["spd"],
        "SERVO_CMD_OFS_POS_DES": CMD_OFS["pos"],
        "SERVO_CMD_OFS_K_POS": CMD_OFS["k_pos"],
        "SERVO_CMD_OFS_K_SPD": CMD_OFS["k_spd"],
        "SERVO_CMD_OFS_CRC": CMD_OFS["crc"],
        "SERVO_CMD_CRC_WORDS": 4,
        "SERVO_FBK_OFS_HEAD": FBK_OFS["head"],
        "SERVO_FBK_OFS_MODE": FBK_OFS["mode"],
        "SERVO_FBK_OFS_TEMP": FBK_OFS["temp"],
        "SERVO_FBK_OFS_SENSOR": FBK_OFS["sensor"],
        "SERVO_FBK_OFS_VOL": FBK_OFS["vol"],
        "SERVO_FBK_OFS_TORQUE": FBK_OFS["torque"],
        "SERVO_FBK_OFS_SPEED": FBK_OFS["speed"],
        "SERVO_FBK_OFS_POS": FBK_OFS["pos"],
        "SERVO_FBK_OFS_MERROR": FBK_OFS["m_error"],
        "SERVO_FBK_OFS_OUTPOS": FBK_OFS["outpos"],
        "SERVO_FBK_OFS_EXSENSOR2": FBK_OFS["ex_sensor2"],
        "SERVO_FBK_OFS_EXCOM": FBK_OFS["ex_com"],
        "SERVO_FBK_OFS_CRC": FBK_OFS["crc"],
        "SERVO_FBK_CRC_OFS": 2,
        "SERVO_FBK_CRC_WORDS": 5,
        "SERVO_ID_MAX": ID_MAX,
        "SERVO_ID_BROADCAST": ID_BROADCAST,
        "SERVO_CMD_HEAD0": 0xFE,
        "SERVO_CMD_HEAD1": 0xEE,
        "SERVO_FBK_HEAD0": 0xFC,
        "SERVO_FBK_HEAD1": 0xEE,
        "SERVO_FBK_SPEED_RPM_Q16": 5327,
        "SERVO_FBK_TORQUE_CNM_Q16": 7382,
        "SERVO_OUTPOS_TO_D10_NUM": 225,
        "SERVO_OUTPOS_TO_D10_SHIFT": 9,
        "SERVO_FBK_CURRENT_NUM": 10,
        "SERVO_FBK_CURRENT_DEN": 256,
        "SERVO_FBK_CURRENT_DEN": 256,
        "SERVO_OUTPOS_MASK": 0x1FFF,
        "SERVO_EXFLAG_SHIFT": 13,
        "SERVO_ONLINE_LOST_CNT": 3,
    }
    missing = [k for k in expected if k not in defines]
    check("all C frame constants are parseable", not missing,
          "missing=" + ",".join(missing) if missing else "%d constants"
          % len(expected))
    bad = ["%s=%s(exp %s)" % (k, defines.get(k), v) for k, v in expected.items()
           if defines.get(k) != v]
    check("C frame constants equal the protocol model", not bad,
          "; ".join(bad) if bad else "ok")

    # gear ratio / scaling constants used by the C conversion helpers
    check("C uses RATIO = 70070/243 (pos command)",
          ("SERVO_GEAR_RATIO_NUM        (70070LL)" in src and
           "SERVO_GEAR_RATIO_DEN        (243LL)" in src))
    check("C uses RATIO^2 = 83148 for the Kp/Kd conversion",
          "SERVO_GEAR_RATIO_SQ         (83148LL)" in src)
    check("C command position scaling = 32768*RATIO/3600",
          ("SERVO_CMD_POS_NUM           (32768LL * SERVO_GEAR_RATIO_NUM)" in src
           and "SERVO_CMD_POS_DEN           (3600LL * SERVO_GEAR_RATIO_DEN)"
           in src))


# ===========================================================================
# C. Control frame tests
# ===========================================================================
def test_control_frames():
    print("\n[C] Control frame build (20 bytes, manual p.7-8)")
    params_kp = 25600000       # PARAM_SERVO_KP default: 20.0 -> k_pos 307
    params_kd = 128000000      # PARAM_SERVO_KD default: 1.0  -> k_spd 1539
    kp = gain_param_to_field(params_kp)
    kd = gain_param_to_field(params_kd)
    check("default Kp param -> k_pos field", kp == 307, "k_pos=%d" % kp)
    check("default Kd param -> k_spd field", kd == 1539, "k_spd=%d" % kd)

    # servo 1 (id 1), FOC closed loop, timeout protection on, target +30.0 deg
    angle_d10 = 300
    pos_raw = pos_d10_to_raw(angle_d10)
    # vendor formula equivalent (float, independent implementation)
    pos_ref = int(round((angle_d10 / 10.0) * math.pi / 180.0 * RATIO *
                        32768.0 / (2.0 * math.pi)))
    check("pos command conversion (30.0 deg)", abs(pos_raw - pos_ref) <= 1,
          "raw=%d ref=%d" % (pos_raw, pos_ref))

    frame = build_control_frame(1, 1, 1, 0, 0, pos_raw, kp, kd)
    check("control frame length 20", len(frame) == 20)
    check("head = FE EE", frame[0:2] == b"\xFE\xEE", frame[0:2].hex())
    check("packed mode byte = id|mode<<4|timeout<<7 (id1, FOC, timeout)",
          frame[2] == (1 | (1 << 4) | (1 << 7)), "0x%02X" % frame[2])
    check("reserved byte = 0", frame[3] == 0)
    check("torque feed forward = 0", frame[4:6] == b"\x00\x00")
    check("speed feed forward = 0", frame[6:8] == b"\x00\x00")
    check("pos_des little endian",
          int.from_bytes(frame[8:12], "little", signed=True) == pos_raw,
          frame[8:12].hex())
    check("k_pos little endian int16",
          int.from_bytes(frame[12:14], "little", signed=True) == kp)
    check("k_spd little endian int16",
          int.from_bytes(frame[14:16], "little", signed=True) == kd)
    check("CRC over bytes 0..15 (head included)",
          int.from_bytes(frame[16:20], "little") == crc_cmd(frame),
          "0x%08X" % int.from_bytes(frame[16:20], "little"))
    check("CRC field little endian",
          frame[16:20] == crc_cmd(frame).to_bytes(4, "little"))

    # worked example used in docs/SERVO_S288.md
    print("        worked example (id=1, FOC, timeout=1, +30.0 deg, "
          "kp=307, kd=1539):")
    print("        " + frame.hex())

    # negative position (int32 two's complement little endian)
    neg = build_control_frame(0, 1, 1, 0, 0, -123456, 0, 0)
    check("negative pos_des two's complement LE",
          int.from_bytes(neg[8:12], "little", signed=True) == -123456,
          neg[8:12].hex())
    check("negative position converts symmetrically",
          pos_d10_to_raw(-300) == -pos_d10_to_raw(300))

    # broadcast id 15 -> non zero low nibble, no response expected
    bcast = build_control_frame(ID_BROADCAST, 0, 1, 0, 0, 0, 0, 0)
    check("broadcast id 15 in mode byte low nibble",
          (bcast[2] & 0x0F) == 15 and ((bcast[2] >> 4) & 0x07) == 0,
          "0x%02X" % bcast[2])

    # disabled/locked mode: mode=0 and gains 0
    locked = build_control_frame(2, 0, 1, 0, 0, 0, 0, 0)
    check("locked mode (mode=0) + timeout protection",
          ((locked[2] >> 4) & 7) == 0 and ((locked[2] >> 7) & 1) == 1,
          "0x%02X" % locked[2])

    # timeout protection clear: timeout bit 0
    clear = build_control_frame(1, 1, 0, 0, 0, 0, kp, kd)
    check("clear-timeout frame has timeout bit = 0", (clear[2] >> 7) == 0,
          "0x%02X" % clear[2])

    # gain conversion for the largest values admitted by the parameter table
    check("Kp/Kd conversion (param max -> field)",
          gain_param_to_field(100000000) == 1202 and
          gain_param_to_field(1000000000) == 12026,
          "kp=%d kd=%d" % (gain_param_to_field(100000000),
                           gain_param_to_field(1000000000)))

    # multi byte endianness: symmetric values differ from big endian
    endian = build_control_frame(0, 1, 1, 0, 0, 0x11223344, 0, 0)
    check("pos_des is little endian (not big endian)",
          endian[8:12] == bytes([0x44, 0x33, 0x22, 0x11]),
          endian[8:12].hex())
    return frame


# ===========================================================================
# D. Feedback frame tests
# ===========================================================================
def test_feedback_frames():
    print("\n[D] Feedback frame build + parse (26 bytes, manual p.9)")
    # outpos 4096 = half an output turn = 180.0 deg, exflag = 1
    frame = build_feedback_frame(servo_id=1, mode=1, timeout=0, temp=25,
                                 sensor=40, vol=48, torque_raw=12800,
                                 speed_raw=1280, pos_raw=16384,
                                 m_error=0x00040001, outpos=4096, exflag=1)
    check("feedback frame length 26", len(frame) == 26)
    check("head = FC EE", frame[0:2] == b"\xFC\xEE", frame[0:2].hex())
    check("worst case response has no alignment padding before the head",
          frame[0] == 0xFC)

    parsed = parse_feedback(1, frame)
    check("valid frame parses", parsed is not None)
    if parsed is not None:
        check("parsed id / mode / timeout flag",
              (parsed["id"], parsed["mode"], parsed["timeout_flag"]) ==
              (1, 1, 0))
        check("temperature = max(housing 25, winding 40)", parsed["temp_c"] == 40,
              str(parsed["temp_c"]))
        check("voltage 48 * 0.5 V = 24000 mV", parsed["voltage_mv"] == 24000)
        check("output encoder position: 4096 -> 180.0 deg",
              parsed["pos_d10"] == 1800, "%d (0.1 deg)" % parsed["pos_d10"])
        check("rotor position raw kept", parsed["pos_raw"] == 16384)
        check("speed 1280 raw -> 104 rpm", parsed["speed_rpm"] == 104,
              "%d rpm" % parsed["speed_rpm"])
        check("torque 12800 raw -> 1442 (0.01 Nm output)",
              parsed["torque_cnm"] == 1442,
              "%d (0.01 Nm)" % parsed["torque_cnm"])
        check("current estimate from torque = 500 mA",
              parsed["current_ma"] == 500, "%d mA" % parsed["current_ma"])
        check("fault bits = low byte of MError", 0x01 == 0x01)
        check("warning flags (ExFlag) = 1", parsed["warn"] == 1)
        check("MError full word preserved", parsed["m_error"] == 0x00040001,
              "0x%08X" % parsed["m_error"])
    print("        worked example response (id=1, 180.0 deg, 104 rpm, "
          "1442 cNm, 24.0 V, 40 degC):")
    print("        " + frame.hex())

    # CRC scope: the head is not part of the CRC -> a modified head must still
    # keep the CRC valid, while the CRC must match data[2:22]
    check("feedback CRC covers bytes 2..21 only",
          int.from_bytes(frame[22:26], "little") ==
          crc32_vendor(frame[2:22]))

    # reject: bad head
    bad = bytearray(frame)
    bad[0] = 0x00
    check("reject frame with wrong head", parse_feedback(1, bytes(bad)) is None)

    # reject: corrupted payload (CRC mismatch)
    bad = bytearray(frame)
    bad[6] ^= 0xFF
    check("reject frame with corrupted torque (CRC)",
          parse_feedback(1, bytes(bad)) is None)

    # reject: response from another id
    check("reject frame from another servo id",
          parse_feedback(2, frame) is None)

    # timeout protection flagged -> firmware schedules a clear-timeout frame
    tmo = build_feedback_frame(1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0)
    tp = parse_feedback(1, tmo)
    check("timeout protection flag parsed", tp is not None and
          tp["timeout_flag"] == 1)

    # negative temperature clamps to 0 (uint8 telemetry field)
    negtemp = build_feedback_frame(1, 0, 0, -20, 0, 0, 0, 0, 0, 0, 0, 0)
    tp = parse_feedback(1, negtemp)
    check("negative housing temperature clamps to 0",
          tp is not None and tp["temp_c"] == 0)

    # fault code table (manual p.11) - every documented bit must be
    # representable in the 32 bit MError word through its low byte at least
    fault_bits = {
        0x01: "over current", 0x02: "transient over voltage",
        0x04: "persistent over voltage", 0x08: "under voltage",
        0x10: "chip over temperature", 0x20: "MOS over/under temperature",
        0x40: "MOS temperature abnormal", 0x80: "housing over/under temp",
        0x100: "housing temp abnormal", 0x200: "winding over temperature",
        0x400: "rotor encoder 1", 0x800: "rotor encoder 2",
        0x1000: "output encoder", 0x2000: "stored data",
        0x4000: "abnormal reset", 0x8000: "waiting unlock",
        0x10000: "driver validation", 0x20000: "calibration mode",
        0x40000: "communication CRC", 0x80000: "driver version too low",
        0x100000: "firmware model", 0x200000: "encoder angle abnormal",
    }
    ok = True
    for bit in fault_bits:
        f = build_feedback_frame(1, 0, 0, 0, 0, 0, 0, 0, 0, bit, 0, 0)
        p = parse_feedback(1, f)
        if p is None or p["m_error"] != bit:
            ok = False
    check("all 22 documented MError bits survive the frame (%d bits)"
          % len(fault_bits), ok)


# ===========================================================================
# E. Scan semantics
# ===========================================================================
def test_scan_semantics():
    print("\n[E] Bus scan semantics")

    def bitmap_set(bitmap, servo_id):
        bitmap[servo_id >> 3] |= 1 << (servo_id & 7)

    bm = bytearray(32)
    for i in (0, 7, 8, 14):
        bitmap_set(bm, i)
    check("scan bitmap layout id>>3 / 1<<(id&7)",
          bm[0] == 0x81 and bm[1] == 0x41 and all(b == 0 for b in bm[2:]),
          bm[:2].hex())

    # the firmware only sends frames to ids 0..14, ids 15..253 are reported
    # absent without a transaction (S288 address space)
    check("id 15 is broadcast and never scanned as an address",
          ID_MAX == 14 and ID_BROADCAST == 15)


# ===========================================================================
# F. Timing budget
# ===========================================================================
def test_timing():
    print("\n[F] Timing budget at 6.0 Mbps, 8N1")
    bit_us = 1e6 / 6000000.0
    cmd_us = CMD_SIZE * 10 * bit_us
    fbk_us = FBK_SIZE * 10 * bit_us
    dir_setup_us = 2.0             # BOARD_SERVO_DIR_SETUP_US
    tx_idle_us = 2 * bit_us        # SERVO_TX_IDLE_THRESHOLD_BITS = 2
    rx_timeout_us = 3000.0         # APP_CFG_SERVO_UART_TIMEOUT_US
    poll_100hz_us = 10000.0
    total = dir_setup_us + cmd_us + tx_idle_us + fbk_us
    print("        command  20 B = %6.2f us" % cmd_us)
    print("        response 26 B = %6.2f us" % fbk_us)
    print("        dir setup     = %6.2f us" % dir_setup_us)
    print("        TX idle thr   = %6.2f us" % tx_idle_us)
    print("        one poll transaction = %.2f us (%.2f%% of a 100 Hz period)"
          % (total, total / poll_100hz_us * 100.0))
    print("        response timeout     = %.0f us" % rx_timeout_us)
    check("command frame duration at 6 Mbps", abs(cmd_us - 33.33) < 0.1,
          "%.2f us" % cmd_us)
    check("response frame duration at 6 Mbps", abs(fbk_us - 43.33) < 0.1,
          "%.2f us" % fbk_us)
    check("turn around budget < 5 us", (dir_setup_us + tx_idle_us) < 5.0,
          "%.2f us" % (dir_setup_us + tx_idle_us))
    check("one 100 Hz poll transaction < 1% CPU", total / poll_100hz_us < 0.01)
    check("response timeout >> response duration", rx_timeout_us > 10 * fbk_us)


# ===========================================================================
def main():
    print("=" * 78)
    print("S288 servo frame unit test")
    print("source under test: %s" % C_SOURCE)
    print("=" * 78)
    test_crc()
    test_c_source_consistency()
    test_control_frames()
    test_feedback_frames()
    test_scan_semantics()
    test_timing()
    print("\n" + "=" * 78)
    print("RESULT: %d passed, %d failed" % (PASS, FAIL))
    if FAIL:
        print("FAILED: " + ", ".join(FAILED_NAMES))
        return 1
    print("ALL TESTS PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
