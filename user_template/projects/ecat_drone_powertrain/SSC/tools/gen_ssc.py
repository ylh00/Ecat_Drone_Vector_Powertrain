#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Generate the Beckhoff SSC Tool input files of the ecat_drone_powertrain project.

Outputs (all inside ../../ = the project directory):
    SSC/Config/HPM_ECAT_DRONE_Config.xml   SSC Tool "Configuration" file
    SSC/ESI/HPM_ECAT_DRONE.xml             hand-written ESI (device description)
    SSC/ecat_drone.xlsx                    SSC Tool "Application" description
    SSC/ecat_drone_application.csv         the same application table as CSV

The xlsx is written with the Python standard library only (zipfile + XML): the
bundled Python of the SDK environment has no openpyxl and there is no network
access on the build machines.  The file is a byte-level rewrite of the
hpm_sdk sample workbook (samples/ethercat/ecat_io/SSC/digital_io.xlsx): the
workbook/theme/styles parts are kept, only the sheet data and the shared string
table are regenerated.  This keeps the file loadable by Excel and by the SSC
Tool application parser.

The process data layout and the parameter object dictionary are generated from
the contracts of the project:
    inc/ecat_pdo.h     RxPDO (0x1600) / TxPDO (0x1A00) field order and size
    inc/app_config.h   EtherCAT identity
    inc/param.h        parameter list -> CoE manufacturer objects 0x2000 + id

Usage:
    python gen_ssc.py [--project-dir <dir>] [--sdk-dir <dir>]

The generated files are checked into the repository; re-run this script when
inc/ecat_pdo.h, inc/app_config.h or inc/param.h change.
"""

import argparse
import csv
import os
import re
import sys
import zipfile
from xml.etree import ElementTree as ET
from xml.sax.saxutils import escape

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_PROJECT = os.path.normpath(os.path.join(HERE, "..", ".."))
DEFAULT_SDK = os.path.normpath(os.path.join(HERE, "..", "..", "..", "..", "..", "hpm_sdk"))

# ---------------------------------------------------------------------------
# EtherCAT device identity (must match inc/app_config.h)
# ---------------------------------------------------------------------------
VENDOR_ID = 0x00000E5A
VENDOR_NAME = "HPMicro"
PRODUCT_CODE = 0x0000E3A1
REVISION_NO = 0x00010001
SERIAL_NO = 0x00000001

DEVICE_NAME = "ECAT_DRONE_POWERTRAIN"
GROUP_NAME = "ECAT_Device"
DEVICE_HW_VERSION = "ecat_drone_5e3y"
DEVICE_SW_VERSION = "1.0.0"
DEVICE_TYPE = 5001              # Modular Device Profile, as in the ecat_io sample
MIN_PD_CYCLE_TIME = 0x79E0      # 31200 ns, same as the ecat_io sample
MAX_PD_INPUT_SIZE = 0x40        # TxPDO 48 bytes
MAX_PD_OUTPUT_SIZE = 0x40       # RxPDO 22 bytes
ESC_EEPROM_SIZE = 0x800         # 2 Kbyte EEPROM image
ESC_CONFIG_DATA = "800C8166000000001234"

# ---------------------------------------------------------------------------
# Process data (must match inc/ecat_pdo.h byte for byte!)
#   (object index, ESI type, bit width, C name, description)
# ---------------------------------------------------------------------------
# TxPDO (slave -> master) 48 bytes, objects 0x6000..0x6017
TX_PDO_FIELDS = [
    (0x6000, "UINT", 16, "status_word", "Status word (application state bits)"),
    (0x6001, "UINT", 16, "fault_code", "Fault bitfield"),
    (0x6002, "UDINT", 32, "esc_erpm_1", "ESC1 electrical RPM (bidirectional DShot)"),
    (0x6003, "UDINT", 32, "esc_erpm_2", "ESC2 electrical RPM (bidirectional DShot)"),
    (0x6004, "UINT", 16, "esc_tlm_period_us_1", "ESC1 raw telemetry period in us"),
    (0x6005, "UINT", 16, "esc_tlm_period_us_2", "ESC2 raw telemetry period in us"),
    (0x6006, "INT", 16, "servo_pos_d10_1", "Servo1 actual position, 0.1 deg"),
    (0x6007, "INT", 16, "servo_pos_d10_2", "Servo2 actual position, 0.1 deg"),
    (0x6008, "INT", 16, "servo_speed_rpm_1", "Servo1 speed, rpm"),
    (0x6009, "INT", 16, "servo_speed_rpm_2", "Servo2 speed, rpm"),
    (0x600A, "INT", 16, "servo_current_ma_1", "Servo1 current, mA"),
    (0x600B, "INT", 16, "servo_current_ma_2", "Servo2 current, mA"),
    (0x600C, "USINT", 8, "servo_temp_c_1", "Servo1 temperature, degC"),
    (0x600D, "USINT", 8, "servo_temp_c_2", "Servo2 temperature, degC"),
    (0x600E, "USINT", 8, "servo_error_1", "Servo1 error/status byte"),
    (0x600F, "USINT", 8, "servo_error_2", "Servo2 error/status byte"),
    (0x6010, "UINT", 16, "sys_voltage_dv", "System voltage, 0.1 V"),
    (0x6011, "INT", 16, "sys_current_ca", "System current, 0.01 A"),
    (0x6012, "UINT", 16, "esc_current_ma_1", "ESC1 current, mA"),
    (0x6013, "UINT", 16, "esc_current_ma_2", "ESC2 current, mA"),
    (0x6014, "UINT", 16, "loop_counter", "Control loop counter"),
    (0x6015, "UINT", 16, "led_status", "RGB LED state / status code"),
    (0x6016, "UINT", 16, "ecat_link_status", "Link + AL state (bit0 port0, bit1 port1, bits4-7 AL)"),
    (0x6017, "UINT", 16, "reserved_0", "Reserved"),
]

# RxPDO (master -> slave) 22 bytes, objects 0x7000..0x7009
RX_PDO_FIELDS = [
    (0x7000, "UINT", 16, "control_word", "Control word (run / clear fault / e-stop ...)"),
    (0x7001, "UINT", 16, "esc_cmd_us_1", "ESC1 throttle command, 1000..2000 us"),
    (0x7002, "UINT", 16, "esc_cmd_us_2", "ESC2 throttle command, 1000..2000 us"),
    (0x7003, "INT", 16, "servo_angle_d10_1", "Servo1 target angle, 0.1 deg"),
    (0x7004, "INT", 16, "servo_angle_d10_2", "Servo2 target angle, 0.1 deg"),
    (0x7005, "UINT", 16, "servo_ctrl_word", "Servo control word (enable / clear fault)"),
    (0x7006, "UINT", 16, "dshot_cmd_1", "Raw DShot command for ESC1 (0 = none)"),
    (0x7007, "UINT", 16, "dshot_cmd_2", "Raw DShot command for ESC2 (0 = none)"),
    (0x7008, "UINT", 16, "reserved_0", "Reserved"),
    (0x7009, "UDINT", 32, "cycle_counter", "Master write counter (link debug)"),
]

PARAM_OBJECT_BASE = 0x2000

PARAM_TYPE_MAP = {
    "u8": ("USINT", 8),
    "u16": ("UINT", 16),
    "u32": ("UDINT", 32),
    "i16": ("INT", 16),
    "i32": ("DINT", 32),
    "str": ("STRING(16)", 128),
}


def read_text(path):
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        return f.read()


# Mapping of the authoritative param_type_t values in src/param.c to the short
# type codes used by PARAM_TYPE_MAP (and by the param.h comments).
PARAM_C_TYPE_MAP = {
    "PARAM_TYPE_U8": "u8",
    "PARAM_TYPE_U16": "u16",
    "PARAM_TYPE_U32": "u32",
    "PARAM_TYPE_I16": "i16",
    "PARAM_TYPE_I32": "i32",
    "PARAM_TYPE_STR": "str",
}


def parse_param_c(project_dir):
    """Parse the AUTHORITATIVE parameter descriptors from src/param.c.

    Returns a dict enum_name -> {"type": short type code, "desc": description}.
    The table uses designated initializers (order independent):

        [PARAM_X] = {
            "name", PARAM_TYPE_U16, min, max, def, persist, readonly,
            "description" },
    """
    text = read_text(os.path.join(project_dir, "src", "param.c"))
    pattern = re.compile(
        r"^[ \t]*\[\s*(PARAM_[A-Za-z0-9_]+)\s*\]\s*=\s*\{\s*\r?\n[ \t]*"
        r'"[^"]*"\s*,\s*(PARAM_TYPE_[A-Z0-9_]+)(.*?)\},',
        re.M | re.S,
    )
    out = {}
    for mm in pattern.finditer(text):
        enum_name = mm.group(1)
        ctype = mm.group(2)
        if ctype not in PARAM_C_TYPE_MAP:
            raise RuntimeError("unsupported descriptor type %s for %s in src/param.c"
                               % (ctype, enum_name))
        desc_matches = re.findall(r'"([^"]*)"', mm.group(3))
        out[enum_name] = {
            "type": PARAM_C_TYPE_MAP[ctype],
            "desc": desc_matches[-1].strip() if desc_matches else "",
        }
    if not out:
        raise RuntimeError("no parameter descriptors found in src/param.c")
    return out


def parse_params(project_dir):
    """Build the parameter list used for the SSC files.

    - object index offset / order  : inc/param.h param_id_t enum
    - object data type             : src/param.c s_param_desc[] (authoritative)
    - object description           : src/param.c, else the param.h comment

    Returns a list of dicts: id, enum, type, desc.
    """
    text = read_text(os.path.join(project_dir, "inc", "param.h"))
    pos = text.find("param_id_t")
    if pos < 0:
        raise RuntimeError("cannot find the param_id_t enum in inc/param.h")
    start = text.rfind("typedef enum", 0, pos)
    end = text.find("}", pos)
    if (start < 0) or (end < 0):
        raise RuntimeError("cannot locate the param_id_t enum body in inc/param.h")
    body = text[start:end]

    param_c = parse_param_c(project_dir)

    params = []
    # entries look like:  PARAM_XXX,   /* u16  ... description */   or
    #                     PARAM_XXX = 0, /* u8 ... */
    # NOTE: only spaces/tabs are used between the tokens, never \s, otherwise
    # the optional comment group would swallow the newline and the following
    # enum entry would be skipped by finditer().
    pattern = re.compile(
        r"^[ \t]*(PARAM_[A-Za-z0-9_]+)[ \t]*(?:=[^,\n]*)?,[ \t]*"
        r"(?:/\*[ \t]*(u8|u16|u32|i16|i32|str)\b([^*\n]*?)\*/)?",
        re.M,
    )
    for mm in pattern.finditer(body):
        enum_name = mm.group(1)
        if enum_name == "PARAM_COUNT":
            break
        comment_type = mm.group(2)
        rest = (mm.group(3) or "").strip()
        # strip the numeric range part "0..65535" from the description
        rest = re.sub(r"^[-0-9x.]+\s*\.\.[-0-9x.]+\s*", "", rest).strip()

        info = param_c.get(enum_name)
        if info is not None:
            ptype = info["type"]
            desc = info["desc"] if info["desc"] else (rest if rest else enum_name)
            if (comment_type is not None) and (comment_type != ptype):
                raise RuntimeError(
                    "parameter type mismatch for %s: inc/param.h comment says %s, "
                    "src/param.c descriptor says %s" % (enum_name, comment_type, ptype))
        elif comment_type is not None:
            # fall back to the param.h comment; the descriptor table should
            # always contain every parameter, so warn loudly
            print("WARNING: %s not found in src/param.c - using the inc/param.h "
                  "comment type %s" % (enum_name, comment_type))
            ptype = comment_type
            desc = rest if rest else enum_name
        else:
            raise RuntimeError(
                "cannot determine the data type of %s: not found in src/param.c "
                "and no type comment in inc/param.h" % enum_name)

        params.append(
            {
                "id": len(params),
                "enum": enum_name,
                "type": ptype,
                "desc": desc,
            }
        )
    if not params:
        raise RuntimeError("no parameters found in inc/param.h")

    # cross checks: param.c <-> param.h must describe exactly the same set
    enum_names = [p["enum"] for p in params]
    missing_in_enum = [e for e in param_c if e not in enum_names]
    missing_in_param_c = [e for e in enum_names if e not in param_c]
    if missing_in_enum:
        raise RuntimeError("src/param.c has descriptors not present in the "
                           "param_id_t enum: %s" % missing_in_enum)
    if missing_in_param_c:
        raise RuntimeError("parameters without a src/param.c descriptor: %s"
                           % missing_in_param_c)
    if len(params) != len(param_c):
        raise RuntimeError("parameter count mismatch: param.h enum %d, param.c %d"
                           % (len(params), len(param_c)))
    return params


def camel(name):
    parts = name.split("_")
    return "".join(p[:1].upper() + p[1:].lower() if p.isupper() else p[:1].upper() + p[1:]
                   for p in parts)


def param_c_name(enum_name):
    return "Param" + camel(enum_name[len("PARAM_"):])


# ---------------------------------------------------------------------------
# SSC configuration XML (HPM_ECAT_DRONE_Config.xml)
# ---------------------------------------------------------------------------
CONFIG_XML_TEMPLATE = """<?xml version="1.0"?>
<SscToolConfiguration SSCVersion="5.13" VendorID="#x{vendor_id_hex}" VendorName="{vendor_name}" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="SscToolConfiguration.xsd">
\t<Configurations>
\t\t<Configuration Name="HPMicro ECAT Drone Powertrain" Version="1.0.0.0" Info="">
\t\t\t<Setting CodeDefine="MCI_HW" Value="0">
\t\t\t\t<Flags Visible="False"/>
\t\t\t</Setting>
\t\t\t<Setting CodeDefine="EL9800_HW" Value="0">
\t\t\t\t<Flags Visible="False"/>
\t\t\t</Setting>
\t\t\t<Setting CodeDefine="FC1100_HW" Value="0">
\t\t\t\t<Flags Visible="False"/>
\t\t\t</Setting>
\t\t\t<Setting CodeDefine="_PIC18" Value="0">
\t\t\t\t<Flags Visible="False"/>
\t\t\t</Setting>
\t\t\t<Setting CodeDefine="_PIC24" Value="0">
\t\t\t\t<Flags Visible="False"/>
\t\t\t</Setting>
\t\t\t<Setting CodeDefine="EL9800_APPLICATION" Value="0">
\t\t\t\t<Flags Visible="False"/>
\t\t\t</Setting>
\t\t\t<Setting CodeDefine="TEST_APPLICATION" Value="0">
\t\t\t\t<Flags Visible="False"/>
\t\t\t</Setting>
\t\t\t<Setting CodeDefine="SAMPLE_APPLICATION" Value="0"/>
\t\t\t<Setting CodeDefine="SAMPLE_APPLICATION_INTERFACE" Value="0"/>
\t\t\t<Setting CodeDefine="ESC_16BIT_ACCESS" Value="0"/>
\t\t\t<Setting CodeDefine="ESC_32BIT_ACCESS" Value="1"/>
\t\t\t<Setting CodeDefine="MBX_16BIT_ACCESS" Value="0"/>
\t\t\t<Setting CodeDefine="BIG_ENDIAN_16BIT" Value="0">
\t\t\t\t<Flags Visible="False"/>
\t\t\t</Setting>
\t\t\t<Setting CodeDefine="BIG_ENDIAN_FORMAT" Value="0">
\t\t\t\t<Flags Visible="False"/>
\t\t\t</Setting>
\t\t\t<Setting CodeDefine="CONTROLLER_32BIT" Value="1">
\t\t\t\t<Flags Visible="True"/>
\t\t\t</Setting>
\t\t\t<Setting CodeDefine="CONTROLLER_16BIT" Value="0">
\t\t\t\t<Flags Visible="False"/>
\t\t\t</Setting>
\t\t\t<Setting CodeDefine="OBJ_DWORD_ALIGN" Value="0"/>
\t\t\t<Setting CodeDefine="OBJ_WORD_ALIGN" Value="0"/>
\t\t\t<Setting CodeDefine="MAILBOX_QUEUE" Value="1"/>
\t\t\t<Setting CodeDefine="AOE_SUPPORTED" Value="0"/>
\t\t\t<Setting CodeDefine="COE_SUPPORTED" Value="1"/>
\t\t\t<Setting CodeDefine="COMPLETE_ACCESS_SUPPORTED" Value="1"/>
\t\t\t<Setting CodeDefine="SEGMENTED_SDO_SUPPORTED" Value="1"/>
\t\t\t<Setting CodeDefine="BACKUP_PARAMETER_SUPPORTED" Value="0"/>
\t\t\t<Setting CodeDefine="SDO_RES_INTERFACE" Value="1"/>
\t\t\t<Setting CodeDefine="DIAGNOSIS_SUPPORTED" Value="0"/>
\t\t\t<Setting CodeDefine="EMERGENCY_SUPPORTED" Value="0"/>
\t\t\t<Setting CodeDefine="EOE_SUPPORTED" Value="0"/>
\t\t\t<Setting CodeDefine="FOE_SUPPORTED" Value="0"/>
\t\t\t<Setting CodeDefine="BOOTSTRAPMODE_SUPPORTED" Value="0"/>
\t\t\t<Setting CodeDefine="VOE_SUPPORTED" Value="0"/>
\t\t\t<Setting CodeDefine="SOE_SUPPORTED" Value="0"/>
\t\t\t<Setting CodeDefine="ESC_SM_WD_SUPPORTED" Value="1"/>
\t\t\t<Setting CodeDefine="OP_PD_REQUIRED" Value="1"/>
\t\t\t<Setting CodeDefine="UC_SET_ECAT_LED" Value="0"/>
\t\t\t<Setting CodeDefine="ESC_SUPPORT_ECAT_LED" Value="1"/>
\t\t\t<Setting CodeDefine="ECAT_TIMER_INT" Value="1"/>
\t\t\t<Setting CodeDefine="EXPLICIT_DEVICE_ID" Value="1"/>
\t\t\t<Setting CodeDefine="EXT_DEBUGER_INTERFACE" Value="0">
\t\t\t\t<Flags Visible="False"/>
\t\t\t</Setting>
\t\t\t<Setting CodeDefine="CiA402_SAMPLE_APPLICATION" Value="0"/>
\t\t\t<Setting CodeDefine="SYSTEM_HEADER_FILE" Value="#include &quot;hpm_common.h&quot;"/>
\t\t\t<Setting CodeDefine="STATIC_OBJECT_DIC" Value="0"/>
\t\t\t<Setting CodeDefine="ESC_EEPROM_ACCESS_SUPPORT" Value="0"/>
\t\t\t<Setting CodeDefine="ESC_EEPROM_EMULATION" Value="1"/>
\t\t\t<Setting CodeDefine="CREATE_EEPROM_CONTENT" Value="1"/>
\t\t\t<Setting CodeDefine="ESC_EEPROM_SIZE" Value="{eeprom_size}"/>
\t\t\t<Setting CodeDefine="ESC_CONFIG_DATA" Value="{esc_config_data}"/>
\t\t\t<Setting CodeDefine="HW_ACCESS_FILE" Value="#include &quot;hpm_ecat_hw.h&quot;"/>
\t\t\t<Setting CodeDefine="APPLICATION_FILE" Value="#include &quot;ecat_drone.h&quot;"/>
\t\t\t<Setting CodeDefine="USE_DEFAULT_MAIN" Value="0"/>
\t\t\t<Setting CodeDefine="BOOL" Value="uint8_t"/>
\t\t\t<Setting CodeDefine="UINT8" Value="uint8_t"/>
\t\t\t<Setting CodeDefine="UINT16" Value="uint16_t"/>
\t\t\t<Setting CodeDefine="UINT32" Value="uint32_t"/>
\t\t\t<Setting CodeDefine="UINT64" Value="uint64_t"/>
\t\t\t<Setting CodeDefine="USHORT" Value="uint16_t"/>
\t\t\t<Setting CodeDefine="INT8" Value="int8_t"/>
\t\t\t<Setting CodeDefine="INT16" Value="int16_t"/>
\t\t\t<Setting CodeDefine="INT32" Value="int32_t"/>
\t\t\t<Setting CodeDefine="INT64" Value="int64_t"/>
\t\t\t<Setting CodeDefine="CHAR" Value="char"/>
\t\t\t<Setting CodeDefine="UCHAR" Value="unsigned char"/>
\t\t\t<Setting CodeDefine="SIZEOF(x)" Value="sizeof(x)"/>
\t\t\t<Setting CodeDefine="STRUCT_PACKED_START" Value="ATTR_PACKED"/>
\t\t\t<Setting CodeDefine="MBX_STRUCT_PACKED_START" Value="ATTR_PACKED"/>
\t\t\t<Setting CodeDefine="OBJ_STRUCT_PACKED_START" Value="ATTR_PACKED"/>
\t\t\t<Setting CodeDefine="VENDOR_ID" Value="{vendor_id_value}"/>
\t\t\t<Setting CodeDefine="VENDOR_NAME" Value="{vendor_name}"/>
\t\t\t<Setting CodeDefine="PRODUCT_CODE" Value="{product_code}"/>
\t\t\t<Setting CodeDefine="REVISION_NUMBER" Value="{revision_no}"/>
\t\t\t<Setting CodeDefine="DEVICE_NAME" Value="{device_name}"/>
\t\t\t<Setting CodeDefine="GROUP_NAME" Value="{group_name}"/>
\t\t\t<Setting CodeDefine="DEVICE_HW_VERSION" Value="{device_hw_version}"/>
\t\t\t<Setting CodeDefine="DEVICE_SW_VERSION" Value="{device_sw_version}"/>
\t\t\t<Setting CodeDefine="MIN_PD_CYCLE_TIME" Value="{min_pd_cycle_time}"/>
\t\t\t<Setting CodeDefine="AL_EVENT_ENABLED" Value="1"/>
\t\t\t<Setting CodeDefine="DC_SUPPORTED" Value="1"/>
\t\t\t<Setting CodeDefine="MAX_PD_INPUT_SIZE" Value="{max_pd_input_size}"/>
\t\t\t<Setting CodeDefine="MAX_PD_OUTPUT_SIZE" Value="{max_pd_output_size}"/>
\t\t\t<Setting CodeDefine="MAKE_HUGE_PTR" Value=""/>
\t\t\t<ESI Path="/EtherCATInfo/Descriptions/Devices/Device/Info">
\t\t\t\t<Value>
\t\t\t\t\t<EtherCATController>
\t\t\t\t\t\t<DpramSize>61440</DpramSize>
\t\t\t\t\t\t<SmCount>8</SmCount>
\t\t\t\t\t\t<FmmuCount>8</FmmuCount>
\t\t\t\t\t</EtherCATController>
\t\t\t\t</Value>
\t\t\t\t<!--ESC Parameter-->
\t\t\t</ESI>
\t\t</Configuration>
\t</Configurations>
</SscToolConfiguration>
"""


def gen_config_xml():
    return CONFIG_XML_TEMPLATE.format(
        vendor_id_hex="%08X" % VENDOR_ID,
        vendor_id_value="0x%08X" % VENDOR_ID,
        vendor_name=VENDOR_NAME,
        product_code="0x%08X" % PRODUCT_CODE,
        revision_no="0x%08X" % REVISION_NO,
        device_name=DEVICE_NAME,
        group_name=GROUP_NAME,
        device_hw_version=DEVICE_HW_VERSION,
        device_sw_version=DEVICE_SW_VERSION,
        min_pd_cycle_time="0x%04X" % MIN_PD_CYCLE_TIME,
        max_pd_input_size="0x%02X" % MAX_PD_INPUT_SIZE,
        max_pd_output_size="0x%02X" % MAX_PD_OUTPUT_SIZE,
        eeprom_size="0x%03X" % ESC_EEPROM_SIZE,
        esc_config_data=ESC_CONFIG_DATA,
    )


# ---------------------------------------------------------------------------
# ESI generation
# ---------------------------------------------------------------------------
def hex_le32(value):
    """Little endian hex representation used in ESI <DefaultData>."""
    return "%02X%02X%02X%02X" % (value & 0xFF, (value >> 8) & 0xFF,
                                 (value >> 16) & 0xFF, (value >> 24) & 0xFF)


def hex_le(value, nbytes):
    return "".join("%02X" % ((value >> (8 * i)) & 0xFF) for i in range(nbytes))


def pdo_mapping_entries(fields):
    """PDO mapping entry values (bits 0-7 len, 8-15 subindex, 16-31 index)."""
    out = []
    for (index, _type, bits, _name, _desc) in fields:
        out.append(hex_le32((bits & 0xFF) | (0 << 8) | (index << 16)))
    return out


def esi_dt_pdo(type_name, fields):
    """ESI DataType for a PDO mapping object (0x1600/0x1A00)."""
    lines = []
    lines.append("\t\t\t\t\t\t\t<DataType>")
    lines.append("\t\t\t\t\t\t\t\t<Name>%s</Name>" % type_name)
    lines.append("\t\t\t\t\t\t\t\t<BitSize>%d</BitSize>" % (16 + 32 * len(fields)))
    lines.append("\t\t\t\t\t\t\t\t<SubItem>")
    lines.append("\t\t\t\t\t\t\t\t\t<SubIdx>0</SubIdx>")
    lines.append("\t\t\t\t\t\t\t\t\t<Name>SubIndex 000</Name>")
    lines.append("\t\t\t\t\t\t\t\t\t<Type>USINT</Type>")
    lines.append("\t\t\t\t\t\t\t\t\t<BitSize>8</BitSize>")
    lines.append("\t\t\t\t\t\t\t\t\t<BitOffs>0</BitOffs>")
    lines.append("\t\t\t\t\t\t\t\t\t<Flags>")
    lines.append("\t\t\t\t\t\t\t\t\t\t<Access>ro</Access>")
    lines.append("\t\t\t\t\t\t\t\t\t</Flags>")
    lines.append("\t\t\t\t\t\t\t\t</SubItem>")
    for i, (index, _type, _bits, _name, _desc) in enumerate(fields):
        lines.append("\t\t\t\t\t\t\t\t<SubItem>")
        lines.append("\t\t\t\t\t\t\t\t\t<SubIdx>%d</SubIdx>" % (i + 1))
        lines.append("\t\t\t\t\t\t\t\t\t<Name>SubIndex %03d</Name>" % (i + 1))
        lines.append("\t\t\t\t\t\t\t\t\t<Type>UDINT</Type>")
        lines.append("\t\t\t\t\t\t\t\t\t<Comment>Reference to 0x%04X.0</Comment>" % index)
        lines.append("\t\t\t\t\t\t\t\t\t<BitSize>32</BitSize>")
        lines.append("\t\t\t\t\t\t\t\t\t<BitOffs>%d</BitOffs>" % (16 + 32 * i))
        lines.append("\t\t\t\t\t\t\t\t\t<Flags>")
        lines.append("\t\t\t\t\t\t\t\t\t\t<Access>ro</Access>")
        lines.append("\t\t\t\t\t\t\t\t\t</Flags>")
        lines.append("\t\t\t\t\t\t\t\t</SubItem>")
    lines.append("\t\t\t\t\t\t\t</DataType>")
    return "\n".join(lines)


def esi_obj(index, name, type_name, bit_size, default_hex, access,
            sub_items=None, pdo=None):
    lines = []
    lines.append("\t\t\t\t\t\t\t<Object>")
    lines.append("\t\t\t\t\t\t\t\t<Index>#x%04X</Index>" % index)
    lines.append("\t\t\t\t\t\t\t\t<Name>%s</Name>" % escape(name))
    lines.append("\t\t\t\t\t\t\t\t<Type>%s</Type>" % type_name)
    lines.append("\t\t\t\t\t\t\t\t<BitSize>%d</BitSize>" % bit_size)
    lines.append("\t\t\t\t\t\t\t\t<Info>")
    if sub_items:
        for sub in sub_items:
            lines.append("\t\t\t\t\t\t\t\t\t<SubItem>")
            lines.append("\t\t\t\t\t\t\t\t\t\t<Name>%s</Name>" % escape(sub[0]))
            lines.append("\t\t\t\t\t\t\t\t\t\t<Info>")
            lines.append("\t\t\t\t\t\t\t\t\t\t\t<DefaultData>%s</DefaultData>" % sub[1])
            lines.append("\t\t\t\t\t\t\t\t\t\t</Info>")
            lines.append("\t\t\t\t\t\t\t\t\t</SubItem>")
    else:
        lines.append("\t\t\t\t\t\t\t\t\t<DefaultData>%s</DefaultData>" % default_hex)
    lines.append("\t\t\t\t\t\t\t\t</Info>")
    lines.append("\t\t\t\t\t\t\t\t<Flags>")
    if pdo == "r":
        lines.append("\t\t\t\t\t\t\t\t\t<Access>rw</Access>")
        lines.append("\t\t\t\t\t\t\t\t\t<PdoMapping>r</PdoMapping>")
    elif pdo == "t":
        lines.append("\t\t\t\t\t\t\t\t\t<Access>ro</Access>")
        lines.append("\t\t\t\t\t\t\t\t\t<PdoMapping>t</PdoMapping>")
    else:
        lines.append("\t\t\t\t\t\t\t\t\t<Access>%s</Access>" % access)
    lines.append("\t\t\t\t\t\t\t\t</Flags>")
    lines.append("\t\t\t\t\t\t\t</Object>")
    return "\n".join(lines)


def esi_mapping_object(index, name, dt_type, fields):
    sub_items = [("SubIndex 000", "%02X" % len(fields))]
    for i, entry in enumerate(pdo_mapping_entries(fields)):
        sub_items.append(("SubIndex %03d" % (i + 1), entry))
    lines = []
    lines.append("\t\t\t\t\t\t\t<Object>")
    lines.append("\t\t\t\t\t\t\t\t<Index>#x%04X</Index>" % index)
    lines.append("\t\t\t\t\t\t\t\t<Name>%s</Name>" % escape(name))
    lines.append("\t\t\t\t\t\t\t\t<Type>%s</Type>" % dt_type)
    lines.append("\t\t\t\t\t\t\t\t<BitSize>%d</BitSize>" % (16 + 32 * len(fields)))
    lines.append("\t\t\t\t\t\t\t\t<Info>")
    for (sub_name, data) in sub_items:
        lines.append("\t\t\t\t\t\t\t\t\t<SubItem>")
        lines.append("\t\t\t\t\t\t\t\t\t\t<Name>%s</Name>" % sub_name)
        lines.append("\t\t\t\t\t\t\t\t\t\t<Info>")
        lines.append("\t\t\t\t\t\t\t\t\t\t\t<DefaultData>%s</DefaultData>" % data)
        lines.append("\t\t\t\t\t\t\t\t\t\t</Info>")
        lines.append("\t\t\t\t\t\t\t\t\t</SubItem>")
    lines.append("\t\t\t\t\t\t\t\t</Info>")
    lines.append("\t\t\t\t\t\t\t</Object>")
    return "\n".join(lines)


def esi_pdo(kind, index, sm, fields, name):
    lines = []
    lines.append("\t\t\t\t\t<%sPdo Fixed=\"true\" Mandatory=\"true\" Sm=\"%d\">" % (kind, sm))
    lines.append("\t\t\t\t\t\t<Index>#x%04X</Index>" % index)
    lines.append("\t\t\t\t\t\t<Name>%s</Name>" % escape(name))
    for (obj_index, type_name, bits, cname, desc) in fields:
        lines.append("\t\t\t\t\t\t<Entry>")
        lines.append("\t\t\t\t\t\t\t<Index>#x%04X</Index>" % obj_index)
        lines.append("\t\t\t\t\t\t\t<SubIndex>0</SubIndex>")
        lines.append("\t\t\t\t\t\t\t<BitLen>%d</BitLen>" % bits)
        lines.append("\t\t\t\t\t\t\t<Name>%s</Name>" % escape(cname))
        lines.append("\t\t\t\t\t\t\t<DataType>%s</DataType>" % type_name)
        lines.append("\t\t\t\t\t\t</Entry>")
    lines.append("\t\t\t\t\t</%sPdo>" % kind)
    return "\n".join(lines)


def gen_esi(sdk_dir, params):
    sample_path = os.path.join(sdk_dir, "samples", "ethercat", "ecat_io", "SSC",
                               "ESI", "ECAT_IO.xml")
    text = read_text(sample_path)
    tab = "\t"

    # ---- vendor id / device identity ------------------------------------
    text = text.replace("<Id>#x0048504D</Id>", "<Id>#x%08X</Id>" % VENDOR_ID)
    text = re.sub(r'<Device Physics="\w+">', '<Device Physics="YY">', text)
    text = re.sub(
        r'<Type ProductCode="#x[0-9A-Fa-f]+" RevisionNo="#x[0-9A-Fa-f]+">digital_io</Type>',
        '<Type ProductCode="#x%08X" RevisionNo="#x%08X">%s</Type>'
        % (PRODUCT_CODE, REVISION_NO, DEVICE_NAME),
        text,
    )
    text = text.replace("<Name>digital_io</Name>", "<Name>%s</Name>" % DEVICE_NAME, 1)

    # ---- string data types (device name / HW / SW version) --------------
    text = re.sub(r"<Name>STRING\(3\)</Name>(\s*)<BitSize>24</BitSize>",
                  r"<Name>STRING(%d)</Name>\1<BitSize>%d</BitSize>"
                  % (len(DEVICE_SW_VERSION), 8 * len(DEVICE_SW_VERSION)), text)
    text = re.sub(r"<Name>STRING\(11\)</Name>(\s*)<BitSize>88</BitSize>",
                  r"<Name>STRING(%d)</Name>\1<BitSize>%d</BitSize>"
                  % (len(DEVICE_HW_VERSION), 8 * len(DEVICE_HW_VERSION)), text)
    text = re.sub(r"<Name>STRING\(10\)</Name>(\s*)<BitSize>80</BitSize>",
                  r"<Name>STRING(%d)</Name>\1<BitSize>%d</BitSize>"
                  % (len(DEVICE_NAME), 8 * len(DEVICE_NAME)), text)

    def ascii_hex(s):
        return "".join("%02X" % ord(c) for c in s)

    # 0x1008 device name / 0x1009 HW version / 0x100A SW version
    text = re.sub(
        r"(<Index>#x1008</Index>\s*<Name>[^<]*</Name>\s*<Type>)STRING\(10\)(</Type>\s*<BitSize>)80(</BitSize>\s*<Info>\s*<DefaultData>)[0-9A-Fa-f]+",
        lambda m: "%sSTRING(%d)%s%d%s%s" % (m.group(1), len(DEVICE_NAME), m.group(2),
                                            8 * len(DEVICE_NAME), m.group(3), ascii_hex(DEVICE_NAME)),
        text, count=1)
    text = re.sub(
        r"(<Index>#x1009</Index>\s*<Name>[^<]*</Name>\s*<Type>)STRING\(11\)(</Type>\s*<BitSize>)88(</BitSize>\s*<Info>\s*<DefaultData>)[0-9A-Fa-f]+",
        lambda m: "%sSTRING(%d)%s%d%s%s" % (m.group(1), len(DEVICE_HW_VERSION), m.group(2),
                                            8 * len(DEVICE_HW_VERSION), m.group(3), ascii_hex(DEVICE_HW_VERSION)),
        text, count=1)
    text = re.sub(
        r"(<Index>#x100A</Index>\s*<Name>[^<]*</Name>\s*<Type>)STRING\(3\)(</Type>\s*<BitSize>)24(</BitSize>\s*<Info>\s*<DefaultData>)[0-9A-Fa-f]+",
        lambda m: "%sSTRING(%d)%s%d%s%s" % (m.group(1), len(DEVICE_SW_VERSION), m.group(2),
                                            8 * len(DEVICE_SW_VERSION), m.group(3), ascii_hex(DEVICE_SW_VERSION)),
        text, count=1)

    # ---- 0x1018 identity ------------------------------------------------
    m = re.search(r"<Object>\s*<Index>#x1018</Index>.*?</Object>", text, re.S)
    if not m:
        raise RuntimeError("0x1018 object not found in the ESI sample")
    block = m.group(0)
    block = block.replace("<DefaultData>4D504800</DefaultData>",
                          "<DefaultData>%s</DefaultData>" % hex_le(VENDOR_ID, 4))
    block = block.replace("<DefaultData>01000000</DefaultData>",
                          "<DefaultData>%s</DefaultData>" % hex_le(PRODUCT_CODE, 4), 1)
    block = block.replace("<DefaultData>01000000</DefaultData>",
                          "<DefaultData>%s</DefaultData>" % hex_le(REVISION_NO, 4), 1)
    block = block.replace("<DefaultData>00000000</DefaultData>",
                          "<DefaultData>%s</DefaultData>" % hex_le(SERIAL_NO, 4), 1)
    text = text[:m.start()] + block + text[m.end():]

    # ---- DataTypes of the PDO mapping objects ---------------------------
    text = re.sub(r"<DataType>\s*<Name>DT1600</Name>.*?</DataType>",
                  esi_dt_pdo("DT1600", RX_PDO_FIELDS), text, count=1, flags=re.S)
    text = re.sub(r"<DataType>\s*<Name>DT1A00</Name>.*?</DataType>",
                  esi_dt_pdo("DT1A00", TX_PDO_FIELDS), text, count=1, flags=re.S)

    # ---- PDO mapping objects 0x1600 / 0x1A00 ---------------------------
    text = re.sub(r"<Object>\s*<Index>#x1600</Index>.*?</Object>",
                  esi_mapping_object(0x1600, "RxPDO process data mapping", "DT1600", RX_PDO_FIELDS),
                  text, count=1, flags=re.S)
    text = re.sub(r"<Object>\s*<Index>#x1A00</Index>.*?</Object>",
                  esi_mapping_object(0x1A00, "TxPDO process data mapping", "DT1A00", TX_PDO_FIELDS),
                  text, count=1, flags=re.S)

    # ---- process data variables 0x6000.. / 0x7000.. / parameters 0x2000..
    def pdo_vars(fields, direction):
        return "\n".join(
            esi_obj(index, cname, type_name, bits, "00" * (bits // 8), "ro" if direction == "t" else "rw",
                    pdo=direction)
            for (index, type_name, bits, cname, _desc) in fields
        )

    text = re.sub(r"<Object>\s*<Index>#x6000</Index>.*?</Object>",
                  pdo_vars(TX_PDO_FIELDS, "t"), text, count=1, flags=re.S)

    rx_and_params = pdo_vars(RX_PDO_FIELDS, "r") + "\n"
    rx_and_params += "\n".join(
        esi_obj(PARAM_OBJECT_BASE + p["id"], p["enum"], PARAM_TYPE_MAP[p["type"]][0],
                PARAM_TYPE_MAP[p["type"]][1], "00" * (PARAM_TYPE_MAP[p["type"]][1] // 8),
                "rw")
        for p in params
    )
    text = re.sub(r"<Object>\s*<Index>#x7010</Index>.*?</Object>",
                  rx_and_params, text, count=1, flags=re.S)

    # ---- PDO definitions ------------------------------------------------
    text = re.sub(r"<RxPdo Fixed=.*?</RxPdo>",
                  esi_pdo("Rx", 0x1600, 2, RX_PDO_FIELDS, "RxPDO (master -> slave)"),
                  text, count=1, flags=re.S)
    text = re.sub(r"<TxPdo Fixed=.*?</TxPdo>",
                  esi_pdo("Tx", 0x1A00, 3, TX_PDO_FIELDS, "TxPDO (slave -> master)"),
                  text, count=1, flags=re.S)

    # ---- sync manager sizes --------------------------------------------
    # SM2 (outputs / RxPDO) = 22 bytes, SM3 (inputs / TxPDO) = 48 bytes
    text = re.sub(r'(<Sm DefaultSize=")4(" StartAddress="#x1100")',
                  r"\g<1>22\2", text, count=1)
    text = re.sub(r'(<Sm DefaultSize=")4(" StartAddress="#x1400")',
                  r"\g<1>48\2", text, count=1)

    # ---- EEPROM size ----------------------------------------------------
    text = text.replace("<ByteSize>2048</ByteSize>", "<ByteSize>%d</ByteSize>" % ESC_EEPROM_SIZE)

    # sanity check: must be well formed XML
    try:
        ET.fromstring(text.encode("utf-8"))
    except Exception as exc:
        raise RuntimeError("generated ESI is not valid XML: %s" % exc)
    return text


# ---------------------------------------------------------------------------
# SSC application description (xlsx + csv)
# ---------------------------------------------------------------------------
def xlsx_cell(col, row, value):
    ref = "%s%d" % (col, row)
    if isinstance(value, (int, float)):
        return '<c r="%s"><v>%s</v></c>' % (ref, value)
    if value is None or value == "":
        return ""
    return '<c r="%s" t="s"><v>%d</v></c>', value  # placeholder, handled below


def col_letter(idx):
    """0 -> A, 1 -> B, ... (only the columns we use)."""
    letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
    return letters[idx]


class SheetBuilder(object):
    def __init__(self):
        self.rows = []        # list of list of (col_index, value)
        self.shared = {}
        self.shared_list = []
        self.use_count = 0

    def add_row(self, cells):
        """cells: dict {col_letter: value} or list of (col_letter, value)."""
        if isinstance(cells, dict):
            items = sorted(cells.items(), key=lambda kv: (len(kv[0]), kv[0]))
        else:
            items = cells
        self.rows.append(items)

    def string_index(self, text):
        if text not in self.shared:
            self.shared[text] = len(self.shared_list)
            self.shared_list.append(text)
        return self.shared[text]

    def to_xml(self):
        cells_out = []
        for r_idx, row in enumerate(self.rows, start=1):
            cell_xml = []
            for (col, value) in row:
                if value is None or value == "":
                    continue
                ref = "%s%d" % (col, r_idx)
                if isinstance(value, (int, float)):
                    cell_xml.append('<c r="%s"><v>%s</v></c>' % (ref, value))
                else:
                    idx = self.string_index(str(value))
                    cell_xml.append('<c r="%s" t="s"><v>%d</v></c>' % (ref, idx))
            cells_out.append('<row r="%d">%s</row>' % (r_idx, "".join(cell_xml)))
        max_col = max(col_letter(i) for i in range(16))
        sheet = (
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
            '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
            'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
            '<dimension ref="B1:%s%d"/><sheetData>%s</sheetData></worksheet>'
            % (max_col, len(self.rows), "".join(cells_out))
        )
        shared = (
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
            '<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
            'count="%d" uniqueCount="%d">%s</sst>'
            % (self.use_count, len(self.shared_list),
               "".join('<si><t xml:space="preserve">%s</t></si>' % escape(s) for s in self.shared_list))
        )
        return sheet, shared


def build_application_sheet(params):
    b = SheetBuilder()
    # --- fixed header (identical layout to the hpm_sdk digital_io.xlsx) ---
    b.add_row({"B": "Device Profile:", "C": DEVICE_TYPE, "D": "Modular Device Profile",
               "F": "Usage Notes: \n"
                    "- The PDO mapping object and SyncManager assignment object doesn't need to be defined. "
                    "In that case they are created automatically.\n"
                    "- The following objects are fixed included in the SSC and shall not be defined in the file : "
                    "0x1000, 0x1001, 0x1008, 0x1009, 0x100a, 0x1010, 0x1011, 0x1018, 0x10F0, 0x10F1, 0x10F3, "
                    "0x1c00, 0x1c32, 0x1c33\n"
                    "- Entries less or equal one 8Bit shall not overlap byte borders.\n"
                    "- Entries greater 8Bit shall always start at an exact word border"})
    b.add_row({"B": "Modul Profile:", "C": 0})
    b.add_row({"B": "IndexIncrement", "C": 0})
    b.add_row({"B": "PdoIndexIncrement:", "C": 0})
    b.add_row({})
    b.add_row({})
    b.add_row({})
    b.add_row({"B": "The object dictionary defined here shall be used complementary with ETG.5001 and ETG.1000"})
    b.add_row({})
    b.add_row({"B": "Index", "C": "ObjectCode", "D": "SI", "E": "DataType", "F": "Name",
               "G": "Default Value", "H": "Min Value", "I": "Max Value", "J": "M/O/C",
               "K": "B/S", "L": "Access", "M": "rx/tx", "N": "CoeRead", "O": "CoeWrite",
               "P": "Description"})

    def comment(text):
        b.add_row({"B": text})

    def object_row(index, object_code="VARIABLE", si="", data_type="", name="",
                   default="", min_v="", max_v="", moc="o", bs="",
                   access="", direction="", coe_read="", coe_write="", desc=""):
        b.add_row({"B": "0x%04X" % index if isinstance(index, int) else index,
                   "C": object_code, "D": si, "E": data_type, "F": name,
                   "G": default, "H": min_v, "I": max_v, "J": moc, "K": bs,
                   "L": access, "M": direction, "N": coe_read, "O": coe_write,
                   "P": desc})

    # --- manufacturer specific parameter objects (0x2000 + param id) ----
    comment("//0x2000")
    comment("Manufacturer specific parameters (0x2000 - 0x2FFF), one object per runtime parameter")
    for p in params:
        type_name = PARAM_TYPE_MAP[p["type"]][0]
        object_row(PARAM_OBJECT_BASE + p["id"], data_type=type_name,
                   name=param_c_name(p["enum"]), default="0",
                   moc="o", access="rw",
                   coe_read="ecat_coe_obj_read", coe_write="ecat_coe_obj_write",
                   desc=p["desc"][:120])

    # --- input data of the module (TxPDO) --------------------------------
    comment("//0x6nnx")
    comment("Input Data of the Module (0x6000 - 0x6FFF) -> TxPDO 0x1A00, 48 bytes")
    for (index, type_name, _bits, cname, desc) in TX_PDO_FIELDS:
        object_row(index, data_type=type_name, name=cname, default="0",
                   moc="o", access="ro", direction="tx", desc=desc)

    # --- output data of the module (RxPDO) -------------------------------
    comment("//0x7nnx")
    comment("Output Data of the Module (0x7000 - 0x7FFF) -> RxPDO 0x1600, 22 bytes")
    for (index, type_name, _bits, cname, desc) in RX_PDO_FIELDS:
        object_row(index, data_type=type_name, name=cname, default="0",
                   moc="o", access="rw", direction="rx", desc=desc)

    # --- notes -----------------------------------------------------------
    comment("//0x16nn")
    comment("RxPDO mapping (0x1600 - 0x17FF) is created automatically from the rx objects above")
    comment("//0x1Ann")
    comment("TxPDO mapping (0x1A00 - 0x1BFF) is created automatically from the tx objects above")
    comment("//0x1C12")
    comment("SyncManager 2 assignment (0x1C12) is created automatically")
    comment("//0x1C13")
    comment("SyncManager 3 assignment (0x1C13) is created automatically")
    return b


def gen_xlsx(sdk_dir, params, out_path):
    sample_path = os.path.join(sdk_dir, "samples", "ethercat", "ecat_io", "SSC", "digital_io.xlsx")
    builder = build_application_sheet(params)
    sheet_xml, shared_xml = builder.to_xml()

    # count string references for the sst count attribute
    builder.use_count = sum(
        1 for row in builder.rows for (_c, v) in row if isinstance(v, str) and v != ""
    )
    sheet_xml, shared_xml = builder.to_xml()

    with zipfile.ZipFile(sample_path, "r") as zin:
        entries = [(info, zin.read(info.filename)) for info in zin.infolist()]

    with zipfile.ZipFile(out_path, "w", zipfile.ZIP_DEFLATED) as zout:
        for info, data in entries:
            name = info.filename
            if name == "xl/worksheets/sheet1.xml":
                data = sheet_xml.encode("utf-8")
            elif name == "xl/sharedStrings.xml":
                data = shared_xml.encode("utf-8")
            zout.writestr(info, data)

    # sanity check: reopen and parse both XML parts
    with zipfile.ZipFile(out_path, "r") as z:
        ET.fromstring(z.read("xl/worksheets/sheet1.xml"))
        ET.fromstring(z.read("xl/sharedStrings.xml"))
        ET.fromstring(z.read("xl/workbook.xml"))
    return len(builder.rows)


def gen_csv(params, out_path):
    lines = []
    lines.append("\"Index\",\"ObjectCode\",\"SI\",\"DataType\",\"Name\",\"Default Value\","
                 "\"Min Value\",\"Max Value\",\"M/O/C\",\"B/S\",\"Access\",\"rx/tx\","
                 "\"CoeRead\",\"CoeWrite\",\"Description\"")
    for p in params:
        lines.append('"0x%04X","VARIABLE","","%s","%s","0","","","","","rw","","%s","%s","%s"'
                     % (PARAM_OBJECT_BASE + p["id"], PARAM_TYPE_MAP[p["type"]][0],
                        param_c_name(p["enum"]), "ecat_coe_obj_read", "ecat_coe_obj_write",
                        p["desc"].replace('"', "'")))
    for (index, type_name, _bits, cname, desc) in TX_PDO_FIELDS:
        lines.append('"0x%04X","VARIABLE","","%s","%s","0","","","","","ro","tx","","","%s"'
                     % (index, type_name, cname, desc.replace('"', "'")))
    for (index, type_name, _bits, cname, desc) in RX_PDO_FIELDS:
        lines.append('"0x%04X","VARIABLE","","%s","%s","0","","","","","rw","rx","","","%s"'
                     % (index, type_name, cname, desc.replace('"', "'")))
    with open(out_path, "w", encoding="utf-8", newline="\r\n") as f:
        f.write("\r\n".join(lines) + "\r\n")


# ---------------------------------------------------------------------------
# verification of the generated files against the authoritative param.c table
# ---------------------------------------------------------------------------
def expected_param_objects(params):
    """index -> (esi type, bit size, param enum) for all parameter objects."""
    out = {}
    for p in params:
        esi_type, bits = PARAM_TYPE_MAP[p["type"]]
        out[PARAM_OBJECT_BASE + p["id"]] = (esi_type, bits, p["enum"])
    return out


def _xlsx_rows(path):
    """Return the sheet rows of the generated xlsx as dicts {column: value}."""
    nsm = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"
    with zipfile.ZipFile(path, "r") as z:
        shared = ["".join(t.text or "" for t in si.iter(nsm + "t"))
                  for si in ET.fromstring(z.read("xl/sharedStrings.xml")).findall(nsm + "si")]
        sheet = ET.fromstring(z.read("xl/worksheets/sheet1.xml"))
        rows = []
        for row in sheet.find(nsm + "sheetData").findall(nsm + "row"):
            cells = {}
            for c in row.findall(nsm + "c"):
                col = re.match(r"[A-Z]+", c.get("r")).group(0)
                v = c.find(nsm + "v")
                if (c.get("t") == "s") and (v is not None):
                    cells[col] = shared[int(v.text)]
                elif v is not None:
                    cells[col] = v.text
            rows.append(cells)
    return rows


def verify_generated(project_dir, params):
    """Self check: ESI / CSV / xlsx parameter object types must match param.c.

    This is deliberately strict (it is the fix for the verifier finding that
    array parameters like PARAM_SERVO_ANGLE_MIN2 were emitted as UDINT because
    inc/param.h has no type comment on the second array element).
    """
    errors = []
    expected = expected_param_objects(params)

    # ---- ESI ------------------------------------------------------------
    esi_path = os.path.join(project_dir, "SSC", "ESI", "HPM_ECAT_DRONE.xml")
    esi_root = ET.parse(esi_path).getroot()
    esi_seen = set()
    for obj in esi_root.iter("Object"):
        idx_text = obj.findtext("Index")
        if (idx_text is None) or (not idx_text.startswith("#x")):
            continue
        index = int(idx_text[2:], 16)
        if index not in expected:
            continue
        esi_seen.add(index)
        want_type, want_bits, enum_name = expected[index]
        got_type = obj.findtext("Type")
        got_bits = obj.findtext("BitSize")
        if got_type != want_type:
            errors.append("ESI 0x%04X (%s): Type=%r, expected %r"
                          % (index, enum_name, got_type, want_type))
        if (got_bits is None) or (int(got_bits) != want_bits):
            errors.append("ESI 0x%04X (%s): BitSize=%r, expected %d"
                          % (index, enum_name, got_bits, want_bits))
    if len(esi_seen) != len(expected):
        errors.append("ESI: found %d/%d parameter objects" % (len(esi_seen), len(expected)))

    # ---- CSV ------------------------------------------------------------
    csv_path = os.path.join(project_dir, "SSC", "ecat_drone_application.csv")
    csv_seen = set()
    with open(csv_path, "r", encoding="utf-8", errors="replace", newline="") as f:
        reader = csv.reader(f)
        header = next(reader, None)
        for row in reader:
            if len(row) < 4:
                continue
            try:
                index = int(row[0], 16)
            except (ValueError, TypeError):
                continue
            if index not in expected:
                continue
            csv_seen.add(index)
            want_type, _bits, enum_name = expected[index]
            if row[3] != want_type:
                errors.append("CSV 0x%04X (%s): DataType=%r, expected %r"
                              % (index, enum_name, row[3], want_type))
    if len(csv_seen) != len(expected):
        errors.append("CSV: found %d/%d parameter objects" % (len(csv_seen), len(expected)))

    # ---- xlsx -----------------------------------------------------------
    xlsx_path = os.path.join(project_dir, "SSC", "ecat_drone.xlsx")
    xlsx_seen = set()
    for cells in _xlsx_rows(xlsx_path):
        b = cells.get("B", "")
        if not b.startswith("0x"):
            continue
        try:
            index = int(b, 16)
        except ValueError:
            continue
        if index not in expected:
            continue
        xlsx_seen.add(index)
        want_type, _bits, enum_name = expected[index]
        if cells.get("E") != want_type:
            errors.append("xlsx 0x%04X (%s): DataType=%r, expected %r"
                          % (index, enum_name, cells.get("E"), want_type))
    if len(xlsx_seen) != len(expected):
        errors.append("xlsx: found %d/%d parameter objects" % (len(xlsx_seen), len(expected)))

    return errors


# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--project-dir", default=DEFAULT_PROJECT)
    ap.add_argument("--sdk-dir", default=DEFAULT_SDK)
    args = ap.parse_args()

    project_dir = os.path.abspath(args.project_dir)
    sdk_dir = os.path.abspath(args.sdk_dir)
    if not os.path.isdir(sdk_dir):
        print("ERROR: HPM sdk directory not found: %s" % sdk_dir)
        return 2

    params = parse_params(project_dir)
    print("parameters parsed from inc/param.h + src/param.c: %d (CoE objects 0x%04X..0x%04X)"
          % (len(params), PARAM_OBJECT_BASE,
             PARAM_OBJECT_BASE + len(params) - 1))
    type_counts = {}
    for p in params:
        type_counts[p["type"]] = type_counts.get(p["type"], 0) + 1
    print("parameter data types (from src/param.c descriptors): %s"
          % ", ".join("%s=%d" % (t, type_counts[t]) for t in sorted(type_counts)))

    config_dir = os.path.join(project_dir, "SSC", "Config")
    esi_dir = os.path.join(project_dir, "SSC", "ESI")
    os.makedirs(config_dir, exist_ok=True)
    os.makedirs(esi_dir, exist_ok=True)

    config_path = os.path.join(config_dir, "HPM_ECAT_DRONE_Config.xml")
    with open(config_path, "w", encoding="utf-8", newline="\r\n") as f:
        f.write(gen_config_xml())
    print("written: %s" % config_path)

    esi_path = os.path.join(esi_dir, "HPM_ECAT_DRONE.xml")
    with open(esi_path, "w", encoding="utf-8", newline="\r\n") as f:
        f.write(gen_esi(sdk_dir, params))
    print("written: %s" % esi_path)

    xlsx_path = os.path.join(project_dir, "SSC", "ecat_drone.xlsx")
    rows = gen_xlsx(sdk_dir, params, xlsx_path)
    print("written: %s (%d sheet rows)" % (xlsx_path, rows))

    csv_path = os.path.join(project_dir, "SSC", "ecat_drone_application.csv")
    gen_csv(params, csv_path)
    print("written: %s" % csv_path)

    # self check: the generated ESI / CSV / xlsx objects must use exactly the
    # data types of the src/param.c descriptors (see INCIDENT: array parameter
    # 2nd elements were UDINT before this check was added).
    errors = verify_generated(project_dir, params)
    if errors:
        print("\nSELF CHECK FAILED:")
        for err in errors:
            print("  - %s" % err)
        return 1

    print("self check OK: %d CoE parameter objects match src/param.c (ESI, CSV, xlsx)"
          % len(params))
    return 0


if __name__ == "__main__":
    sys.exit(main())
