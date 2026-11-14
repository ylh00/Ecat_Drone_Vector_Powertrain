#!/usr/bin/env python3
"""
Host protocol contract test for the ecat_drone_powertrain firmware.

The test parses the *C truth* directly from the project headers:
  inc/host_proto.h  - frame constants, command ids, status codes, packed struct
                      layouts (telemetry blob, parameter descriptor, ...)
  inc/param.h       - parameter id enum (order defines the numeric ids)
  src/param.c       - parameter table (one entry per id)
  src/host_proto_app.c - command dispatcher (one case per HOST_CMD_*)
  docs/USB_PROTOCOL.md - protocol examples (every "A5 5A ..." hex run must
                      decode as a valid frame)

It then validates:
  1. CRC16-CCITT-FALSE against the standard test vector ("123456789" -> 0x29B1)
     and a second independent bitwise implementation on random data,
  2. frame encode/decode round trips for requests and responses,
  3. the exact wire sizes of the telemetry blob (54 B), parameter descriptor
     (70 B) and all other response blobs,
  4. a full SET_PARAM and GET_PARAM_DESC request/response example,
  5. that every HOST_CMD_* id has a dispatcher case and every PARAM_* id has a
     table entry,
  6. the hex examples in docs/USB_PROTOCOL.md (CRC, lengths, command ids).

Run:  python .work/proto_test/test_host_proto.py
      python .work/proto_test/test_host_proto.py --emit   (print doc examples)
"""

import re
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
# Works both from the delivered location (project/tests/) and from the
# development copy (.work/proto_test/ inside the workspace root).
if (HERE.parent / "inc" / "host_proto.h").exists():
    PROJECT = HERE.parent
else:
    WORKSPACE = HERE.parents[1]
    PROJECT = WORKSPACE / "sdk_env_v1.13.0" / "user_template" / "projects" / "ecat_drone_powertrain"

HOST_PROTO_H = PROJECT / "inc" / "host_proto.h"
PARAM_H = PROJECT / "inc" / "param.h"
PARAM_C = PROJECT / "src" / "param.c"
HOST_PROTO_C = PROJECT / "src" / "host_proto_app.c"
DOC = PROJECT / "docs" / "USB_PROTOCOL.md"

FAILS = []
CHECKS = [0]


def check(cond, msg):
    CHECKS[0] += 1
    if not cond:
        FAILS.append(msg)
        print("FAIL: %s" % msg)


def read(path):
    return path.read_text(encoding="utf-8", errors="replace")


# ---------------------------------------------------------------------------
# Header parsing helpers
# ---------------------------------------------------------------------------

def parse_defines(text):
    """Return {NAME: int} for numeric '#define NAME (0x12U)' style defines."""
    out = {}
    for m in re.finditer(r"^\s*#define\s+([A-Z0-9_]+)\s+\(?\s*(0x[0-9A-Fa-f]+|\d+)\s*[uUlL]*\s*\)?", text, re.M):
        out[m.group(1)] = int(m.group(2), 0)
    return out


def parse_enum(text, enum_name):
    """Return ordered [(name, value)] of a C enum (auto-incrementing)."""
    m = re.search(r"typedef\s+enum\s*\{([^{}]*)\}\s*%s\s*;" % re.escape(enum_name), text, re.S)
    if not m:
        raise SystemExit("enum %s not found" % enum_name)
    body = re.sub(r"/\*.*?\*/", "", m.group(1), flags=re.S)
    items = []
    value = 0
    for raw in body.split(","):
        line = raw.strip()
        if not line:
            continue
        parts = re.match(r"([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(0x[0-9A-Fa-f]+|\d+)", line)
        if parts:
            name, value = parts.group(1), int(parts.group(2), 0)
        else:
            name = line
        items.append((name, value))
        value += 1
    return items


def parse_packed_struct(text, struct_name, defines=None):
    """Return [(field_name, c_type, array_len)] of a packed typedef struct."""
    defines = defines or {}
    m = re.search(
        r"typedef\s+struct\s+(?:__attribute__\(\(packed\)\)\s*)?\{([^{}]*)\}\s*%s\s*;"
        % re.escape(struct_name), text, re.S)
    if not m:
        raise SystemExit("packed struct %s not found" % struct_name)
    body = re.sub(r"/\*.*?\*/", "", m.group(1), flags=re.S)
    fields = []
    for raw in body.split(";"):
        line = raw.strip()
        if not line:
            continue
        fm = re.match(r"(uint\d+_t|int\d+_t|char)\s+([A-Za-z_][A-Za-z0-9_]*)\s*"
                      r"(?:\[\s*([A-Za-z0-9_]+)\s*\])?", line)
        if not fm:
            continue
        if fm.group(3) is not None:
            token = fm.group(3)
            n = int(token) if token.isdigit() else int(defines[token])
        else:
            n = 1
        fields.append((fm.group(2), fm.group(1), n))
    return fields


TYPE_SIZE = {
    "uint8_t": 1, "int8_t": 1, "char": 1,
    "uint16_t": 2, "int16_t": 2,
    "uint32_t": 4, "int32_t": 4,
}


def struct_layout(fields):
    """Return (size, {field: offset}) for a packed C struct.  Multi-element
    arrays are reported at the offset of their first element under the same
    field name; the test only uses single element offsets."""
    offsets = {}
    size = 0
    for name, ctype, n in fields:
        offsets[name] = size
        size += TYPE_SIZE[ctype] * n
    return size, offsets


def offset_comments(text, struct_name):
    """Parse '/* offset NN */' annotations of a struct field list."""
    m = re.search(
        r"typedef\s+struct\s+(?:__attribute__\(\(packed\)\)\s*)?\{([^{}]*)\}\s*%s\s*;"
        % re.escape(struct_name), text, re.S)
    out = {}
    for line in m.group(1).splitlines():
        fm = re.search(r"([A-Za-z_][A-Za-z0-9_]*)\s*(?:\[[^\]]*\])?\s*;\s*/\*\s*offset\s+(\d+)", line)
        if fm:
            out[fm.group(1)] = int(fm.group(2))
    return out


# ---------------------------------------------------------------------------
# CRC16-CCITT-FALSE, bitwise (independent of the C implementation style)
# ---------------------------------------------------------------------------

def crc16_ccitt_false(data):
    crc = 0xFFFF
    for byte in data:
        crc ^= byte << 8
        for _ in range(8):
            if crc & 0x8000:
                crc = ((crc << 1) ^ 0x1021) & 0xFFFF
            else:
                crc = (crc << 1) & 0xFFFF
    return crc


def crc16_table(data):
    """Second, table driven implementation used as a cross check."""
    table = []
    for i in range(256):
        crc = i << 8
        for _ in range(8):
            crc = ((crc << 1) ^ 0x1021) & 0xFFFF if crc & 0x8000 else (crc << 1) & 0xFFFF
        table.append(crc)
    crc = 0xFFFF
    for byte in data:
        crc = ((crc << 8) & 0xFFFF) ^ table[((crc >> 8) ^ byte) & 0xFF]
    return crc


# ---------------------------------------------------------------------------
# Frame codec (mirrors the C parser/emitter exactly)
# ---------------------------------------------------------------------------

class Proto:
    def __init__(self, defines):
        self.SYNC0 = defines["HOST_PROTO_SYNC0"]
        self.SYNC1 = defines["HOST_PROTO_SYNC1"]
        self.HDR = defines["HOST_PROTO_HEADER_SIZE"]
        self.CRC = defines["HOST_PROTO_CRC_SIZE"]
        self.MAX_PAYLOAD = defines["APP_CFG_HOST_PROTO_MAX_PAYLOAD"]

    # --- encoder (device response / host request) ---
    def encode(self, cmd, seq, payload=b""):
        if len(payload) > self.MAX_PAYLOAD:
            raise ValueError("payload too large")
        body = bytes((cmd & 0xFF, seq & 0xFF)) + len(payload).to_bytes(2, "little") + payload
        crc = crc16_ccitt_false(body)
        return bytes((self.SYNC0, self.SYNC1)) + body + crc.to_bytes(2, "little")

    # --- decoder (streaming state machine, same states as the C parser) ---
    def decode(self, data, report=None):
        """Return (cmd, seq, payload) or None if no complete valid frame."""
        st = 0
        cmd = seq = 0
        ln = idx = 0
        calc = 0xFFFF
        recv = 0
        payload = bytearray()
        oversize = False
        i = 0
        while i < len(data):
            b = data[i]
            if st == 0:
                if b == self.SYNC0:
                    st = 1
            elif st == 1:
                if b == self.SYNC1:
                    calc = 0xFFFF
                    st = 2
                elif b != self.SYNC0:
                    st = 0
            elif st == 2:
                cmd = b
                calc = crc16_byte(calc, b)
                st = 3
            elif st == 3:
                seq = b
                calc = crc16_byte(calc, b)
                st = 4
            elif st == 4:
                ln = b
                calc = crc16_byte(calc, b)
                st = 5
            elif st == 5:
                ln |= b << 8
                calc = crc16_byte(calc, b)
                idx = 0
                oversize = ln > self.MAX_PAYLOAD
                if ln == 0:
                    st = 7
                else:
                    payload = bytearray()
                    st = 6
            elif st == 6:
                if not oversize:
                    payload.append(b)
                calc = crc16_byte(calc, b)
                idx += 1
                if idx >= ln:
                    st = 7
            elif st == 7:
                recv = b
                st = 8
            elif st == 8:
                recv |= b << 8
                st = 0
                if recv != calc:
                    if report is not None:
                        report.append(("crc_error", cmd, seq))
                elif oversize:
                    if report is not None:
                        report.append(("length_error", cmd, seq))
                else:
                    return cmd, seq, bytes(payload)
            i += 1
        return None


def crc16_byte(crc, byte):
    crc ^= byte << 8
    for _ in range(8):
        crc = ((crc << 1) ^ 0x1021) & 0xFFFF if crc & 0x8000 else (crc << 1) & 0xFFFF
    return crc


# ---------------------------------------------------------------------------
# Helpers to pack/unpack the documented blobs (offsets come from the header)
# ---------------------------------------------------------------------------

def pack_i32(value):
    return int(value).to_bytes(4, "little", signed=True)


def pack_u32(value):
    return int(value).to_bytes(4, "little", signed=False)


def pack_i16(value):
    return int(value).to_bytes(2, "little", signed=True)


def unpack_i32(buf, off):
    return int.from_bytes(buf[off:off + 4], "little", signed=True)


def pack_param_desc(offsets, size, pid, ptype, persist, readonly, pmin, pmax,
                    pdef, pvalue, name, desc):
    buf = bytearray(size)
    struct.pack_into("<H", buf, offsets["id"], pid)
    buf[offsets["type"]] = ptype
    buf[offsets["persist"]] = persist
    buf[offsets["readonly"]] = readonly
    buf[offsets["len"]] = len(name)
    struct.pack_into("<i", buf, offsets["min"], pmin)
    struct.pack_into("<i", buf, offsets["max"], pmax)
    struct.pack_into("<i", buf, offsets["def"], pdef)
    struct.pack_into("<i", buf, offsets["value"], pvalue)
    raw = name.encode("utf-8")
    buf[offsets["name"]:offsets["name"] + len(raw)] = raw
    raw = desc.encode("utf-8")
    buf[offsets["desc"]:offsets["desc"] + len(raw)] = raw
    return bytes(buf)


def unpack_param_desc(offsets, size, buf):
    name = buf[offsets["name"]:offsets["name"] + 16].split(b"\x00", 1)[0].decode("utf-8")
    desc = buf[offsets["desc"]:offsets["desc"] + 32].split(b"\x00", 1)[0].decode("utf-8")
    return {
        "id": struct.unpack_from("<H", buf, offsets["id"])[0],
        "type": buf[offsets["type"]],
        "persist": buf[offsets["persist"]],
        "readonly": buf[offsets["readonly"]],
        "len": buf[offsets["len"]],
        "min": struct.unpack_from("<i", buf, offsets["min"])[0],
        "max": struct.unpack_from("<i", buf, offsets["max"])[0],
        "def": struct.unpack_from("<i", buf, offsets["def"])[0],
        "value": struct.unpack_from("<i", buf, offsets["value"])[0],
        "name": name,
        "desc": desc,
    }


import struct  # noqa: E402  (kept after the pure-python CRC helpers for clarity)

# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------
def test_crc():
    vectors = [
        (b"", 0xFFFF),
        (b"123456789", 0x29B1),          # standard CRC-16/CCITT-FALSE check value
        (b"\x00", 0xE1F0),
        (b"\xFF", 0xFF00),
        (bytes((0xA5, 0x5A)), 0x043B),
    ]
    for data, expected in vectors:
        check(crc16_ccitt_false(data) == expected,
              "CRC vector %r: expected 0x%04X got 0x%04X" %
              (data, expected, crc16_ccitt_false(data)))
        check(crc16_table(data) == expected, "table CRC mismatch for %r" % (data,))
    random_data = bytes((i * 37 + 11) & 0xFF for i in range(4096))
    for start in range(0, 256, 17):
        chunk = random_data[start:start + 97]
        check(crc16_ccitt_false(chunk) == crc16_table(chunk),
              "independent CRC implementations disagree at %d" % start)


def test_frame_roundtrip(proto, cmd_ids):
    # request without payload
    ping = [c for (n, c) in cmd_ids if n == "HOST_CMD_PING"][0]
    for seq in (0, 1, 0x2A, 0xFF):
        frame = proto.encode(ping, seq)
        dec = proto.decode(frame)
        check(dec == (ping, seq, b""), "PING round trip failed for seq %d" % seq)
    # request with payload
    payload = bytes(range(0, 200))
    frame = proto.encode(0x51, 7, payload)
    dec = proto.decode(frame)
    check(dec == (0x51, 7, payload), "payload round trip failed")
    # response: status byte first, telemetry size known later
    payload = bytes([0x00]) + bytes(54)
    frame = proto.encode(0x03, 0x42, payload)
    dec = proto.decode(frame)
    check(dec == (0x03, 0x42, payload), "response round trip failed")
    check(len(frame) == proto.HDR + len(payload) + proto.CRC,
          "frame length formula mismatch")


def test_parser_resync(proto):
    # corrupted CRC must be detected
    good = bytearray(proto.encode(0x01, 9))
    bad = bytearray(good)
    bad[-1] ^= 0xFF
    report = []
    check(proto.decode(bytes(bad), report) is None, "corrupt CRC frame accepted")
    check(report and report[0][0] == "crc_error", "CRC error not reported")
    # garbage followed by a good frame must resynchronise
    stream = b"\xa5\x00\x11\x22\xaa\x5a\x5a" + bytes(good)
    dec = proto.decode(stream)
    check(dec == (0x01, 9, b""), "parser did not resynchronise after garbage")
    # oversize length field must be detected, not crash
    bad = bytes((proto.SYNC0, proto.SYNC1, 0x03, 0x01, 0xFF, 0x7F)) + b"\x00" * 100
    report = []
    check(proto.decode(bad, report) is None, "oversize frame accepted")


def test_sizes(defines, telemetry_size, param_desc_size, ctlm_offsets, desc_offsets):
    # telemetry blob
    check(telemetry_size == 54, "telemetry blob is %d bytes, expected 54" % telemetry_size)
    expected_tlm = {
        "status_word": 0, "fault_code": 2, "esc_erpm": 4, "esc_tlm_period_us": 12,
        "servo_pos_d10": 16, "servo_speed_rpm": 20, "servo_current_ma": 24,
        "servo_temp_c": 28, "servo_error": 30, "sys_voltage_dv": 32,
        "sys_current_ca": 34, "esc_current_ma": 36, "loop_counter": 40,
        "led_status": 42, "ecat_link_status": 44, "host_rx_frames": 46,
        "host_crc_errors": 50,
    }
    for name, off in expected_tlm.items():
        check(ctlm_offsets.get(name) == off,
              "telemetry field %s at %s, expected %d" % (name, ctlm_offsets.get(name), off))
    # parameter descriptor
    check(param_desc_size == 70, "param descriptor is %d bytes, expected 70" % param_desc_size)
    for name, macro in (
            ("id", "HOST_PARAM_DESC_OFF_ID"), ("type", "HOST_PARAM_DESC_OFF_TYPE"),
            ("persist", "HOST_PARAM_DESC_OFF_PERSIST"),
            ("readonly", "HOST_PARAM_DESC_OFF_READONLY"),
            ("len", "HOST_PARAM_DESC_OFF_LEN"), ("min", "HOST_PARAM_DESC_OFF_MIN"),
            ("max", "HOST_PARAM_DESC_OFF_MAX"), ("def", "HOST_PARAM_DESC_OFF_DEF"),
            ("value", "HOST_PARAM_DESC_OFF_VALUE"), ("name", "HOST_PARAM_DESC_OFF_NAME"),
            ("desc", "HOST_PARAM_DESC_OFF_DESC")):
        check(desc_offsets[name] == defines[macro],
              "param desc field %s at %d != %s=%d" %
              (name, desc_offsets[name], macro, defines[macro]))
    # response payload sizes stay inside one frame
    max_payload = defines["APP_CFG_HOST_PROTO_MAX_PAYLOAD"]
    check(1 + telemetry_size <= max_payload, "GET_TELEMETRY response too large")
    check(1 + param_desc_size <= max_payload, "GET_PARAM_DESC response too large")
    list_max = defines["HOST_PROTO_LIST_PARAMS_MAX"]
    check(1 + list_max * param_desc_size <= max_payload,
          "LIST_PARAMS max response %d > %d" % (1 + list_max * param_desc_size, max_payload))
    check(1 + 1 + defines["HOST_PROTO_AM32_XFER_MAX_OUT"] <= max_payload,
          "AM32_XFER response too large")
    # magic
    check(defines["HOST_PROTO_PING_MAGIC"] == int.from_bytes(b"PING", "little"),
          "PING magic is not the bytes 'PING' on the wire")


def test_commands(proto, defines, cmd_ids):
    # every HOST_CMD_* in the header must have a dispatcher case
    src = read(HOST_PROTO_C)
    dispatched = set()
    for m in re.finditer(r"case\s+(HOST_CMD_[A-Z0-9_]+)\s*:", src):
        val = dict(cmd_ids).get(m.group(1))
        if val is not None:
            dispatched.add(val)
    for name, val in cmd_ids:
        check(val in dispatched, "command %s (0x%02X) not dispatched" % (name, val))

    # status codes are unique
    status_ids = parse_enum(read(HOST_PROTO_H), "host_status_t")
    values = [v for _, v in status_ids]
    check(len(values) == len(set(values)), "duplicate host_status_t values")


def test_param_table(param_ids):
    src = read(PARAM_C)
    for name, pid in param_ids:
        check(re.search(r"\[\s*%s\s*\]\s*=" % re.escape(name), src) is not None,
              "parameter %s (%d) missing from src/param.c table" % (name, pid))
    check(len(param_ids) == 60, "expected 60 parameters, header declares %d" % len(param_ids))
    # per channel id stretches used by the PARAM_xxx(ch) macros must be adjacent
    index = {name: pid for name, pid in param_ids}
    pairs = [("PARAM_DSHOT_DIRECTION1", "PARAM_DSHOT_DIRECTION2"),
             ("PARAM_DSHOT_3D_MODE1", "PARAM_DSHOT_3D_MODE2"),
             ("PARAM_SERVO_ID1", "PARAM_SERVO_ID2"),
             ("PARAM_SERVO_DIR_TX_LEVEL1", "PARAM_SERVO_DIR_TX_LEVEL2"),
             ("PARAM_SERVO_ANGLE_MIN1", "PARAM_SERVO_ANGLE_MIN2"),
             ("PARAM_SERVO_ANGLE_MAX1", "PARAM_SERVO_ANGLE_MAX2"),
             ("PARAM_SERVO_ANGLE_GAIN1", "PARAM_SERVO_ANGLE_GAIN2"),
             ("PARAM_SERVO_ANGLE_OFS1", "PARAM_SERVO_ANGLE_OFS2"),
             ("PARAM_SERVO_TORQUE_LIMIT1", "PARAM_SERVO_TORQUE_LIMIT2"),
             ("PARAM_SERVO_KP1", "PARAM_SERVO_KP2"),
             ("PARAM_SERVO_KD1", "PARAM_SERVO_KD2")]
    for a, b in pairs:
        check(index[b] == index[a] + 1, "%s/%s are not adjacent ids" % (a, b))


def test_full_example(proto, param_ids, defines, desc_offsets):
    """A complete SET_PARAM then GET_PARAM_DESC exchange, encoded and decoded
    on both sides using only the constants parsed from the C headers."""
    ids = dict(param_ids)
    dshot_rate = ids["PARAM_DSHOT_RATE"]
    set_param = ids["PARAM_DSHOT_RATE"]

    # --- host -> device: SET_PARAM(id=PARAM_DSHOT_RATE, value=600) ---
    payload = struct.pack("<Hi", dshot_rate, 600)
    req = proto.encode(defines["HOST_CMD_SET_PARAM"] if "HOST_CMD_SET_PARAM" in defines else 0x11,
                       0x2A, payload)
    got = proto.decode(req)
    check(got is not None and got[1] == 0x2A, "SET_PARAM request did not decode")
    check(struct.unpack_from("<Hi", got[2], 0) == (dshot_rate, 600),
          "SET_PARAM payload mismatch")
    # device answer: status byte only
    rsp = proto.encode(got[0], got[1], bytes([0x00]))
    dec = proto.decode(rsp)
    check(dec == (got[0], got[1], b"\x00"), "SET_PARAM response did not decode")
    check(dec[2][0] == 0, "SET_PARAM response status is not OK")

    # --- GET_PARAM_DESC(id) -> 70 byte descriptor after the status byte ---
    req = proto.encode(0x12, 1, struct.pack("<H", dshot_rate))
    got = proto.decode(req)
    check(got is not None, "GET_PARAM_DESC request did not decode")
    desc = pack_param_desc(desc_offsets, defines["HOST_PARAM_DESC_SIZE"],
                           dshot_rate, 1, 1, 0, 150, 1200, 600, 600,
                           "dshot_rate", "DShot rate: 150/300/600/1200")
    rsp = proto.encode(got[0], got[1], bytes([0x00]) + desc)
    dec = proto.decode(rsp)
    check(dec is not None and dec[2][0] == 0, "GET_PARAM_DESC response did not decode")
    unpacked = unpack_param_desc(desc_offsets, defines["HOST_PARAM_DESC_SIZE"], dec[2][1:])
    check(unpacked["id"] == dshot_rate, "descriptor id mismatch")
    check(unpacked["name"] == "dshot_rate", "descriptor name mismatch")
    check(unpacked["min"] == 150 and unpacked["max"] == 1200 and unpacked["def"] == 600,
          "descriptor range/default mismatch")
    check(unpacked["value"] == 600, "descriptor value mismatch")
    check(unpacked["len"] == len("dshot_rate"), "descriptor len mismatch")

    # response frame length must be exactly status + 70 data + framing
    check(len(rsp) == defines["HOST_PROTO_HEADER_SIZE"] + defines["HOST_PROTO_CRC_SIZE"] +
          1 + defines["HOST_PARAM_DESC_SIZE"], "GET_PARAM_DESC frame length mismatch")


def test_am32_prog(proto, cmd_ids):
    """AM32 DShot programming mode commands (write one EEPROM byte, save)."""
    ids = dict(cmd_ids)
    prog_write = ids["HOST_CMD_AM32_PROG_WRITE"]
    prog_save = ids["HOST_CMD_AM32_PROG_SAVE"]
    check(prog_write == 0x54, "HOST_CMD_AM32_PROG_WRITE is not 0x54")
    check(prog_save == 0x55, "HOST_CMD_AM32_PROG_SAVE is not 0x55")

    # PROG_WRITE request: esc, addr, value
    payload = bytes((0, 0x5A, 0x12))
    req = proto.encode(prog_write, 0x10, payload)
    got = proto.decode(req)
    check(got == (prog_write, 0x10, payload), "AM32 PROG_WRITE request round trip failed")
    check(got[2][1] <= 191, "AM32 PROG_WRITE address example out of range")
    rsp = proto.encode(got[0], got[1], bytes([0x00]))
    check(proto.decode(rsp) == (prog_write, 0x10, b"\x00"),
          "AM32 PROG_WRITE OK response round trip failed")
    # ERR_BUSY response (queue full, host retries the same request)
    rsp = proto.encode(prog_write, 0x10, bytes([0x07]))
    check(proto.decode(rsp) == (prog_write, 0x10, b"\x07"),
          "AM32 PROG_WRITE ERR_BUSY response round trip failed")

    # PROG_SAVE request: esc only, OK response is status only
    payload = bytes((0,))
    req = proto.encode(prog_save, 0x11, payload)
    check(proto.decode(req) == (prog_save, 0x11, payload),
          "AM32 PROG_SAVE request round trip failed")
    rsp = proto.encode(prog_save, 0x11, bytes([0x00]))
    check(proto.decode(rsp) == (prog_save, 0x11, b"\x00"),
          "AM32 PROG_SAVE OK response round trip failed")


def test_docs(proto, defines, cmd_ids, param_ids):
    if not DOC.exists():
        check(False, "docs/USB_PROTOCOL.md is missing")
        return
    text = read(DOC)
    hex_runs = re.findall(r"(?:[0-9A-F]{2}[ \t]+){5,}[0-9A-F]{2}", text)
    check(len(hex_runs) >= 4, "expected at least 4 protocol hex examples in the docs")
    cmd_values = {v for _, v in cmd_ids}
    cmd_ids_dict = dict(cmd_ids)
    cmd_values_prog_write = cmd_ids_dict["HOST_CMD_AM32_PROG_WRITE"]
    cmd_values_prog_save = cmd_ids_dict["HOST_CMD_AM32_PROG_SAVE"]
    parts = dict(param_ids)
    dshot_rate_id = parts["PARAM_DSHOT_RATE"]
    seen_telemetry_response = False
    seen_set_param = False
    seen_prog_write = False
    seen_prog_save = False
    for run in hex_runs:
        data = bytes(int(x, 16) for x in run.split())
        dec = proto.decode(data)
        if dec is None:
            check(False, "doc example does not decode as a valid frame: %s" % run.strip())
            continue
        cmd, seq, payload = dec
        check(cmd in cmd_values, "doc example uses unknown command 0x%02X" % cmd)
        # a GET_TELEMETRY *response* example must be status + 54 byte telemetry
        if (cmd == 0x03) and (len(payload) > 0):
            seen_telemetry_response = True
            check(len(payload) == 1 + 54,
                  "doc GET_TELEMETRY response payload is %d, expected 55" % len(payload))
        # a PING response example must be status + u32 + u16 + u16 + u16
        if (cmd == 0x01) and (len(payload) > 0):
            check(len(payload) == 11,
                  "doc PING response payload is %d, expected 11" % len(payload))
        # the SET_PARAM example must use the real PARAM_DSHOT_RATE id from param.h
        if (cmd == 0x11) and (len(payload) == 6):
            seen_set_param = True
            pid, value = struct.unpack_from("<Hi", payload, 0)
            check(pid == dshot_rate_id,
                  "doc SET_PARAM example uses id %d but param.h says %d" %
                  (pid, dshot_rate_id))
            check(value == 600, "doc SET_PARAM example value is not 600")
        # AM32 DShot programming mode examples
        if cmd == cmd_values_prog_write:
            if len(payload) == 3:
                seen_prog_write = True
                check(payload[1] <= 191, "doc PROG_WRITE address > 191")
            else:
                check(len(payload) == 1, "doc PROG_WRITE response must be status only")
        if cmd == cmd_values_prog_save:
            seen_prog_save = True
            check(len(payload) == 1, "doc PROG_SAVE payload must be 1 byte")
    check(seen_telemetry_response, "docs missing a full GET_TELEMETRY response example")
    check(seen_set_param, "docs missing the SET_PARAM request example")
    check(seen_prog_write, "docs missing the AM32 PROG_WRITE example")
    check(seen_prog_save, "docs missing the AM32 PROG_SAVE example")
    # PING response example must carry the PING magic
    check("47 4E 49 50" in text.upper() or "0x474E4950" in text,
          "docs do not show the PING magic bytes")


def emit_examples(proto, defines, cmd_ids, param_ids):
    """Print the frames used in the docs (computed with the same codec, so the
    docs can never drift from the header defined ids/sizes)."""
    ids = dict(cmd_ids)
    parts = dict(param_ids)
    print("PING request  seq=0x2A:", " ".join("%02X" % b for b in
          proto.encode(ids["HOST_CMD_PING"], 0x2A)))
    print("PING response seq=0x2A:", " ".join("%02X" % b for b in
          proto.encode(ids["HOST_CMD_PING"], 0x2A,
                       bytes([0x00]) +
                       struct.pack("<IHHH", defines["HOST_PROTO_PING_MAGIC"], 1, 0, 0))))
    payload = struct.pack("<Hi", parts["PARAM_DSHOT_RATE"], 600)
    print("SET_PARAM request:", " ".join("%02X" % b for b in
          proto.encode(ids["HOST_CMD_SET_PARAM"], 0x07, payload)))
    print("SET_PARAM response:", " ".join("%02X" % b for b in
          proto.encode(ids["HOST_CMD_SET_PARAM"], 0x07, bytes([0x00]))))
    tlm = bytes(54)
    print("GET_TELEMETRY request:", " ".join("%02X" % b for b in
          proto.encode(ids["HOST_CMD_GET_TELEMETRY"], 0x01)))
    print("GET_TELEMETRY response:", " ".join("%02X" % b for b in
          proto.encode(ids["HOST_CMD_GET_TELEMETRY"], 0x01, bytes([0x00]) + tlm)))
    print("PARAM_GET_COUNT request:", " ".join("%02X" % b for b in
          proto.encode(ids["HOST_CMD_PARAM_GET_COUNT"], 0x02)))
    print("PARAM_GET_COUNT response:", " ".join("%02X" % b for b in
          proto.encode(ids["HOST_CMD_PARAM_GET_COUNT"], 0x02,
                       struct.pack("<H", len(param_ids)))))
    print("AM32_PROG_WRITE request:", " ".join("%02X" % b for b in
          proto.encode(ids["HOST_CMD_AM32_PROG_WRITE"], 0x10, bytes((0x00, 0x5A, 0x12)))))
    print("AM32_PROG_WRITE response:", " ".join("%02X" % b for b in
          proto.encode(ids["HOST_CMD_AM32_PROG_WRITE"], 0x10, bytes([0x00]))))
    print("AM32_PROG_SAVE request:", " ".join("%02X" % b for b in
          proto.encode(ids["HOST_CMD_AM32_PROG_SAVE"], 0x11, bytes([0x00]))))
    print("AM32_PROG_SAVE response:", " ".join("%02X" % b for b in
          proto.encode(ids["HOST_CMD_AM32_PROG_SAVE"], 0x11, bytes([0x00]))))


def main():
    emit = "--emit" in sys.argv

    proto_text = read(HOST_PROTO_H)
    param_text = read(PARAM_H)
    defines = parse_defines(proto_text)
    defines.update(parse_defines(read(PROJECT / "inc" / "app_config.h")))
    proto = Proto(defines)

    cmd_ids = [(n, v) for n, v in parse_enum(proto_text, "host_cmd_t") if n.startswith("HOST_CMD_")]
    param_ids = [(n, v) for n, v in parse_enum(param_text, "param_id_t") if n != "PARAM_COUNT"]

    tlm_fields = parse_packed_struct(proto_text, "host_telemetry_t")
    telemetry_size, tlm_offsets = struct_layout(tlm_fields)
    tlm_comment_offsets = offset_comments(proto_text, "host_telemetry_t")
    desc_fields = parse_packed_struct(proto_text, "host_param_desc_t", defines)
    param_desc_size, desc_offsets = struct_layout(desc_fields)

    print("project     :", PROJECT)
    print("commands    :", len(cmd_ids))
    print("parameters  :", len(param_ids))
    print("telemetry   : %d bytes (header comment offsets: %d fields)" %
          (telemetry_size, len(tlm_comment_offsets)))
    print("param desc  : %d bytes" % param_desc_size)
    print()

    if emit:
        emit_examples(proto, defines, cmd_ids, param_ids)
        return 0

    test_crc()
    test_frame_roundtrip(proto, cmd_ids)
    test_parser_resync(proto)
    test_sizes(defines, telemetry_size, param_desc_size, tlm_offsets, desc_offsets)
    # the offset annotations in the header must agree with the packed layout
    for name, off in tlm_comment_offsets.items():
        check(tlm_offsets[name] == off,
              "telemetry comment offset %s says %d but layout says %d" %
              (name, off, tlm_offsets[name]))
    test_commands(proto, defines, cmd_ids)
    test_param_table(param_ids)
    test_full_example(proto, param_ids, defines, desc_offsets)
    test_am32_prog(proto, cmd_ids)
    test_docs(proto, defines, cmd_ids, param_ids)

    print()
    if FAILS:
        print("%d/%d checks FAILED" % (len(FAILS), CHECKS[0]))
        return 1
    print("All %d checks OK" % CHECKS[0])
    return 0


if __name__ == "__main__":
    sys.exit(main())

