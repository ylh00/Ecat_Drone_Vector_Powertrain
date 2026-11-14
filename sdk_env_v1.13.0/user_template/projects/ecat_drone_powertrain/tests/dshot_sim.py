#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Host side simulation / validation of the BDShot math used by
  src/dshot_bidir.c  (dshot_crc_bidir / dshot_make_frame / dshot_telemetry_to_erpm /
                      dshot_decode_edges)
and of the AM32 4-way byte timing assumptions (19200 8N1, 52.08us/bit).

The decoder below is a line by line port of dshot_decode_edges() /
ds_decode_candidate() from src/dshot_bidir.c, the encoder is the inverse chain
described in .work/refs/DSHOT_AM32_NOTES.md:

  16 bit value (12 bit payload << 4 | inverted CRC)
    -> 4 x 5 bit GCR quintets            (20 bit)
    -> prepend the start bit "1"         (21 differential bits)
    -> differential line encode: 1 = toggle, 0 = keep  (idle HIGH, active LOW)
    -> edge timestamps at bit slot boundaries, in GPTMR1 capture ticks

It also builds an eRPM <-> telemetry table for the typical hover range so the
bench measurement can be sanity checked (7 pole pairs, 5000 mech RPM -> 35000
eRPM -> period 1714 us -> telemetry ~0x6B0).

Run:  python .work/dshot_test/dshot_sim.py
"""

import math
import random
import sys

# ---------------------------------------------------------------------------
# Protocol constants (must match inc/dshot.h / src/dshot_bidir.c)
# ---------------------------------------------------------------------------
GCR_ENC = {
    0x0: 0x19, 0x1: 0x1B, 0x2: 0x12, 0x3: 0x13,
    0x4: 0x1D, 0x5: 0x15, 0x6: 0x16, 0x7: 0x17,
    0x8: 0x1A, 0x9: 0x09, 0xA: 0x0A, 0xB: 0x0B,
    0xC: 0x1E, 0xD: 0x0D, 0xE: 0x0E, 0xF: 0x0F,
}
GCR_VALID = set(GCR_ENC.values())
GCR_REV = {q: n for n, q in GCR_ENC.items()}

DSHOT_TELEMETRY_STOPPED = 0x0FFF
MAX_EDGES = 24


def dshot_crc_bidir(data12):
    """Inverted 4 bit CRC over a 12 bit value (BDShot, TX and RX)."""
    v = data12 & 0x0FFF
    v = ~(v ^ (v >> 4) ^ (v >> 8))
    return v & 0x0F


def dshot_make_frame(throttle, telemetry_request):
    v = ((throttle & 0x7FF) << 1) | (telemetry_request & 1)
    return ((v << 4) | dshot_crc_bidir(v)) & 0xFFFF


def telemetry_to_erpm(telemetry12):
    if (telemetry12 & 0xFFF) == DSHOT_TELEMETRY_STOPPED:
        return 0
    e = (telemetry12 >> 9) & 0x7
    m = telemetry12 & 0x1FF
    period = m << e
    if period == 0:
        return 0
    return 60000000 // period


def encode_wire_edges(value16):
    """16 bit value -> list of edge indices (slot indices of the 21 bit wire
    sequence, slot 0 = start bit) for an ideal (no jitter) frame."""
    gcr = 0
    for nib in ((value16 >> 12) & 0xF, (value16 >> 8) & 0xF,
                (value16 >> 4) & 0xF, value16 & 0xF):
        gcr = (gcr << 5) | GCR_ENC[nib]
    bits = [1] + [(gcr >> (19 - i)) & 1 for i in range(20)]  # start bit + 20 GCR
    # differential line encode: 1 = toggle; idle level is HIGH
    edges = []
    for i, b in enumerate(bits):
        if b:
            edges.append(i)
    return edges


def edges_to_timestamps(edge_slots, slot_ticks, jitter_ticks=0.0, rng=None):
    """Edge slot indices -> capture timestamps [ticks] with optional jitter."""
    ts = []
    for k in edge_slots:
        t = k * slot_ticks
        if jitter_ticks > 0:
            t += rng.uniform(-jitter_ticks, jitter_ticks)
        ts.append(int(round(t)))
    return ts


# ---------------------------------------------------------------------------
# Decoder: port of dshot_decode_edges() / ds_decode_candidate()
# ---------------------------------------------------------------------------
def decode_candidate(delta, slot_ticks):
    wire = 1
    nbits = 1
    res = 0
    for i in range(len(delta) - 1):
        d = delta[i + 1] - delta[i]
        n = (d + (slot_ticks >> 1)) // slot_ticks
        if n < 1 or n > 3:
            return None, 0
        err = abs(d - n * slot_ticks)
        if err * 4 > slot_ticks:          # 25% of a slot tolerance
            return None, 0
        if nbits + n > 21:
            return None, 0
        wire = (wire << n) | 1            # n-1 zeros then a 1
        nbits += n
        res += err
    if nbits > 21 or (21 - nbits) > 3:
        return None, 0
    wire = (wire << (21 - nbits)) & 0x1FFFFF
    value = 0
    for q in range(4):
        quint = (wire >> ((3 - q) * 5)) & 0x1F
        if quint not in GCR_VALID:
            return None, 0
        value = (value << 4) | GCR_REV[quint]
    tele = value >> 4
    crc = value & 0xF
    if dshot_crc_bidir(tele) != crc:
        return None, 0
    return value, res


def dshot_decode_edges(timestamps, slot_ticks_nominal):
    if len(timestamps) < 3 or slot_ticks_nominal == 0:
        return None
    ts = timestamps[:MAX_EDGES]
    t0 = ts[0]
    delta = [(t - t0) & 0xFFFFFFFF for t in ts]
    # candidate slot widths from the first AND second gap: the first gap may
    # span 1..3 slots (leading GCR bits may be 0), the second gap is an
    # independent estimate.  This makes the decoder robust against jitter.
    widths = []
    for src in (0, 1):
        if src == 0:
            if len(delta) < 2:
                continue
            gap = delta[1]
        else:
            if len(delta) < 3:
                continue
            gap = delta[2] - delta[1]
        for nslot in (1, 2, 3):
            width = (gap + nslot // 2) // nslot
            if width < 2:
                continue
            if width > (slot_ticks_nominal * 4) // 3 or width < (slot_ticks_nominal * 3) // 4:
                continue
            if any(abs(width - x) <= max(1, x // 64) for x in widths):
                continue
            widths.append(width)
    best = None
    best_res = 1 << 32
    for width in widths:
        value, res = decode_candidate(delta, width)
        if value is not None and res < best_res:
            best_res = res
            best = value
    return best


# ---------------------------------------------------------------------------
# Encoder for a decoded 16 bit value (used to build the synthetic frames)
# ---------------------------------------------------------------------------
def decode_value_to_telemetry(value16):
    return (value16 >> 4) & 0xFFF


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------
def log(msg):
    print(msg)


def test_crc_vectors():
    log("== 1. CRC / frame builder vectors ==")
    cases = [
        # throttle, tlm, expected 16 bit frame
        (0, 1, 0x001E),      # MOTOR_STOP with telemetry request (BDShot)
        (0, 0, 0x000F),
        (48, 1, None),
        (2047, 1, None),
    ]
    ok = True
    for throttle, tlm, expect in cases:
        frame = dshot_make_frame(throttle, tlm)
        v = (throttle << 1) | tlm
        crc = dshot_crc_bidir(v)
        # CRC must be the inverse of the single-direction folded checksum
        plain = (~(crc)) & 0xF
        plain_expected = (v ^ (v >> 4) ^ (v >> 8)) & 0xF
        good = (plain == plain_expected) and (dshot_crc_bidir(v) == ((~plain_expected) & 0xF))
        if expect is not None:
            good = good and (frame == expect)
        log("   throttle=%4d tlm=%d -> frame=0x%04X crc=0x%X %s"
            % (throttle, tlm, frame, crc, "OK" if good else "FAIL"))
        ok = ok and good
    # every 12 bit value must round-trip through the CRC check
    for v in range(0x1000):
        f = ((v << 4) | dshot_crc_bidir(v)) & 0xFFFF
        if dshot_crc_bidir((f >> 4) & 0xFFF) != (f & 0xF):
            ok = False
            break
    log("   all 4096 12-bit values round-trip the inverted CRC: %s" % ("OK" if ok else "FAIL"))
    return ok


def test_erpm_table():
    log("")
    log("== 2. eRPM / telemetry mapping (7 pole pairs unless noted) ==")
    log("   mech RPM |  eRPM   | period_us | telemetry | decoded eRPM | err")
    ok = True
    for mech_rpm in (0, 1000, 2000, 3000, 5000, 8000, 10000, 20000):
        pole_pairs = 7
        erpm = mech_rpm * pole_pairs
        if mech_rpm == 0:
            tele = DSHOT_TELEMETRY_STOPPED
            dec = 0
            log("   %8d | %7d | %9s | 0x%03X     | %12d | %s"
                % (mech_rpm, erpm, "-", tele, dec, "stopped OK"))
            continue
        period_us = 60000000 / erpm
        # find the best (e, m) pair, exactly like the ESC firmware does
        best = None
        for e in range(8):
            m = int(round(period_us / (1 << e)))
            if 1 <= m <= 0x1FF:
                p = m << e
                err = abs(p - period_us)
                if best is None or err < best[2]:
                    best = (e, m, err, p)
        e, m, _, p = best
        tele = (e << 9) | m
        dec = telemetry_to_erpm(tele)
        err = 0 if erpm == 0 else abs(dec - erpm) / erpm * 100.0
        good = err < 1.0
        ok = ok and good
        log("   %8d | %7d | %9.1f | 0x%03X     | %12d | %5.2f%% %s"
            % (mech_rpm, erpm, period_us, tele, dec, err, "OK" if good else "FAIL"))
    log("   (7 pole pairs at 5000 mech RPM = 35000 eRPM -> period 1714us -> telemetry 0x5AD,")
    log("    decoded 34965 eRPM = 4995 mech RPM, i.e. 0.1% quantization error)")
    return ok


def build_frame_timestamps(telemetry, slot_ticks=133.333, clock_err=0.0,
                           jitter_ticks=0.0, rng=None, gap_slots=22):
    """Build the capture timestamps of one BDShot response frame.

    gap_slots: idle slots between the TX end (timestamp origin) and the first
    edge; the decoder only uses deltas so it must not matter.
    """
    value16 = (telemetry << 4) | dshot_crc_bidir(telemetry)
    slots = encode_wire_edges(value16)
    actual_slot = slot_ticks * (1.0 + clock_err)
    ts = edges_to_timestamps(slots, actual_slot, jitter_ticks, rng)
    t0 = int(round(gap_slots * slot_ticks))
    return [t0 + t for t in ts], value16


def test_decoder_clean():
    log("")
    log("== 3. Decoder on clean synthetic frames (no jitter) ==")
    rng = random.Random(1234)
    slot = 133.3333        # 100 MHz GPTMR1, DSHOT600 telemetry (750 kbit/s)
    nominal = 133
    ok = True
    for _ in range(2000):
        # random 12 bit payload (CRC is recomputed by the encoder)
        tele = rng.randrange(0x1000)
        if rng.random() < 0.1:
            tele = DSHOT_TELEMETRY_STOPPED
        ts, value16 = build_frame_timestamps(tele, slot, 0.0, 0.0, rng)
        dec = dshot_decode_edges(ts, nominal)
        if dec != value16 or decode_value_to_telemetry(dec) != tele:
            ok = False
            log("   FAIL tele=0x%03X ts=%s dec=%s" % (tele, ts, dec))
            break
        if telemetry_to_erpm(tele) != telemetry_to_erpm(decode_value_to_telemetry(dec)):
            ok = False
            break
    log("   2000 random frames decoded exactly: %s" % ("OK" if ok else "FAIL"))
    return ok


def test_decoder_jitter():
    log("")
    log("== 4. Decoder with realistic jitter / ESC clock error ==")
    log("   case | tested | decoded | wrong value | rejected | max err")
    nominal = 133
    cases = [
        # name, clock error (%), edge jitter (ticks), minimum decode rate (%)
        ("clean",               0.0,  0.0, 100.0),
        ("+-2% clock",          2.0,  0.0, 100.0),
        ("+-5% clock",          5.0,  0.0, 100.0),
        ("3 ticks edge jitter", 0.0,  3.0, 100.0),
        ("clock 2% + 4 tick",   2.0,  4.0, 100.0),
        ("harsh 5% + 8 tick",   5.0,  8.0, 95.0),
    ]
    all_ok = True
    for name, cerr_pct, jit, min_rate in cases:
        cerr = cerr_pct / 100.0
        rng = random.Random(42)
        tested = 0
        decoded = 0
        wrong = 0
        rejected = 0
        for _ in range(3000):
            tele = rng.randrange(0x1000)
            # random sign for the ESC clock error
            ce = cerr * (1 if rng.random() < 0.5 else -1) * rng.random()
            ts, value16 = build_frame_timestamps(tele, 133.3333, ce, jit, rng)
            tested += 1
            dec = dshot_decode_edges(ts, nominal)
            if dec is None:
                rejected += 1
            elif dec == value16:
                decoded += 1
            else:
                wrong += 1
        rate = 100.0 * decoded / tested
        good = (wrong == 0) and (rate >= min_rate)
        all_ok = all_ok and good
        log("   %-22s | %6d | %7d | %11d | %8d | %6.2f%% %s"
            % (name, tested, decoded, wrong, rejected, rate, "OK" if good else "FAIL"))
    log("   (a wrong value must NEVER be produced; the harsh case is beyond the")
    log("    BDShot spec and only loses frames, it never mis-decodes)")
    return all_ok


def test_decoder_damaged():
    log("")
    log("== 5. Decoder on damaged frames (must reject, never produce a wrong value) ==")
    nominal = 133
    slot = 133.3333
    rng = random.Random(7)
    results = {}

    def run_case(name, mutate):
        wrong = 0
        accepted = 0
        for _ in range(3000):
            tele = rng.randrange(0x1000)
            ts, value16 = build_frame_timestamps(tele, slot, 0.0, 1.0, rng)
            ts2 = mutate(ts, rng, value16)
            dec = dshot_decode_edges(ts2, nominal)
            if dec is not None:
                if dec == value16:
                    accepted += 1
                else:
                    wrong += 1
        results[name] = (accepted, wrong)
        return wrong == 0

    ok = True
    ok &= run_case("one edge missing", lambda ts, r, v: ts[:len(ts) // 2] + ts[len(ts) // 2 + 1:])
    ok &= run_case("one extra noise edge", lambda ts, r, v: insert_noise(ts, r))
    ok &= run_case("truncated frame", lambda ts, r, v: ts[:max(3, len(ts) - 3)])
    ok &= run_case("random timestamps", lambda ts, r, v: sorted(r.randrange(0, 5000) for _ in range(len(ts))))
    for name, (acc, wrong) in results.items():
        log("   %-22s : accepted(correct)=%4d wrong=%d %s"
            % (name, acc, wrong, "OK" if wrong == 0 else "FAIL"))
    return ok


def insert_noise(ts, rng):
    out = list(ts)
    pos = rng.randrange(1, len(out))
    out.insert(pos, out[pos - 1] + rng.randrange(1, 200))
    return out


def test_first_gap_cases():
    log("")
    log("== 6. Decoder first-gap ambiguity (GCR quintets may start with 0..2 zeros) ==")
    slot = 133.3333
    nominal = 133
    rng = random.Random(99)
    hist = {}
    ok = True
    for tele in range(0x1000):
        value16 = (tele << 4) | dshot_crc_bidir(tele)
        slots = encode_wire_edges(value16)
        first_gap = slots[1] - slots[0]
        hist[first_gap] = hist.get(first_gap, 0) + 1
        ts, _ = build_frame_timestamps(tele, slot, 0.0, 0.5, rng)
        dec = dshot_decode_edges(ts, nominal)
        if dec != value16:
            ok = False
            log("   FAIL tele=0x%03X first_gap=%d" % (tele, first_gap))
            break
    log("   first gap distribution over all 4096 payloads: %s" % dict(sorted(hist.items())))
    log("   all 4096 payloads decoded regardless of the first gap: %s" % ("OK" if ok else "FAIL"))
    return ok


def test_tx_waveform():
    log("")
    log("== 7. TX pulse width check (75% / 37.5% of the bit period) ==")
    pwm_clock = 100_000_000          # typical clock_pwm0 value, queried at runtime
    rate = 600_000
    period = (pwm_clock + rate // 2) // rate       # matches ds_apply_config()
    reload_v = period - 1
    bit1 = period * 3 // 4
    bit0 = period * 3 // 8
    log("   PWM0 clock %d Hz, DSHOT600 -> period=%d ticks, reload=%d, bit1=%d (%.1f%%), bit0=%d (%.1f%%)"
        % (pwm_clock, period, reload_v, bit1, 100.0 * bit1 / period, bit0, 100.0 * bit0 / period))
    ok = (37.0 < 100.0 * bit0 / period < 38.0) and (74.0 < 100.0 * bit1 / period < 76.0)
    # Worst case DSHOT1200 at a slow PWM clock (24 MHz) must still give sane widths
    for clk, rate, name in ((100_000_000, 1_200_000, "100MHz/DSHOT1200"),
                            (24_000_000, 600_000, "24MHz/DSHOT600"),
                            (24_000_000, 1_200_000, "24MHz/DSHOT1200")):
        p = (clk + rate // 2) // rate
        b1 = p * 3 // 4
        b0 = p * 3 // 8
        log("   %-20s period=%3d ticks -> bit1=%3d (%.0f%%) bit0=%3d (%.0f%%)"
            % (name, p, b1, 100.0 * b1 / p, b0, 100.0 * b0 / p))
        ok = ok and p >= 16 and b0 >= 2 and b1 > b0
    return ok


def test_slot_timing():
    log("")
    log("== 8. Telemetry slot / RX window timing ==")
    tb = 100_000_000                  # GPTMR1 clock (queried at runtime)
    ok = True
    for rate in (150, 300, 600, 1200):
        rate_hz = rate * 1000
        slot = ((tb * 4) + (5 * rate_hz) // 2) // (5 * rate_hz)
        tlm_timeout_us = 70
        rx_window = (tlm_timeout_us * tb // 1000000) + 24 * slot
        log("   DSHOT%4d: telemetry slot=%4d ticks (%.3fus), rx_window=%4d ticks (%.1fus)"
            % (rate, slot, slot * 1e6 / tb, rx_window, rx_window * 1e6 / tb))
        ok = ok and slot >= 4 and (rx_window < tb // 1000)  # must fit into 1ms
    return ok


def main():
    log("BDShot / AM32 host side simulation for src/dshot_bidir.c")
    log("=" * 78)
    ok = True
    ok &= test_crc_vectors()
    ok &= test_erpm_table()
    ok &= test_decoder_clean()
    ok &= test_decoder_jitter()
    ok &= test_decoder_damaged()
    ok &= test_first_gap_cases()
    ok &= test_tx_waveform()
    ok &= test_slot_timing()
    log("")
    log("=" * 78)
    log("RESULT: %s" % ("ALL TESTS PASSED" if ok else "FAILURES PRESENT"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
