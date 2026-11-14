# 验证报告 — ecat_drone_powertrain

> 被验证对象：`sdk_env_v1.13.0\user_template\projects\ecat_drone_powertrain`
> （HPM5E3Y / board `ecat_drone_5e3y` / hpm_sdk v1.13.0）
> 验证者：verifier（独立验证，不重写模块）　验证日期：2026-11-14
> 工具链：RISC-V GCC 13.2.0 (`rv32imac_zicsr_zifencei_multilib_b_ext-win`)、CMake+Ninja、Python 3（SDK 自带）、.NET Framework 4.0 `csc.exe`
> 结论：**构建/链接通过，0 warning；编译期契约全部通过；PDO/CoE/上位机协议/参数系统一致性通过；未发现 critical 级缺陷。EtherCAT 真实协议栈、DShot/舵机/ADC 的硬件时序未验证（无实物板）。**
> 本报告中的构建目录 `D:\Ecat_Drone_Vector_Powertrain\.work\build_verify` 是验证专用目录（非交付物）；交付的回归测试脚本位于 `projects\ecat_drone_powertrain\tests\`。

---

## 0. 结论摘要

| 项目 | 结果 | 证据 |
|---|---|---|
| flash_xip 全量构建（从零，`scripts/build.cmd clean`） | **通过** | §1，0 warning |
| 编译警告数 | **0** | §1.4，构建日志全文检索 `warning:` 计数 = 0 |
| `scripts/check_syntax.py` 全部 src/port 文件 | **16/16 OK** | §1.5 |
| 结构体/协议契约（22/48/54/70 B 等） | **全部通过** | §2 `_Static_assert` + `nm` |
| EtherCAT PDO（ESI/CSV ↔ `ecat_pdo.h`） | **一致（22/48 B，偏移逐项吻合）** | §4 |
| CoE 参数对象 0x2000..0x203B（60 项）类型/宽度 | **60/60 一致**（此前 11 项类型缺陷已由 ecat-agent 修复并复验） | §4.4 |
| Host 协议测试（交付 `tests/test_host_proto.py`） | **238/238 OK** | §5 |
| 舵机帧测试（交付 `tests/test_servo_frames.py`） | **58 passed / 0 failed** | §5 |
| DShot 收发算法仿真（交付 `tests/dshot_sim.py`） | **ALL PASSED** | §5 |
| 上位机 `build.cmd -Selftest` | **43/43 PASS（含 uitest PASS）** | §5 |
| PARAM_COUNT / 参数表完整性 | **60 / 0..59 全覆盖** | §6.1 |
| 固件镜像 vs 504 KB 应用区上限 | **156,332 B << 516,096 B（30.3%）** | §6.3 |
| 参数区 / EEPROM 模拟区冲突 | **无冲突** | §6.4 |

---

## 1. 构建结果

### 1.1 环境与命令（可复现）

```powershell
$env:HPM_SDK_BASE='D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\hpm_sdk'
$env:GNURISCV_TOOLCHAIN_PATH='D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\toolchains\rv32imac_zicsr_zifencei_multilib_b_ext-win'
$env:PATH='D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\tools\cmake\bin;D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\tools\ninja;D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\tools\python3;' + $env:GNURISCV_TOOLCHAIN_PATH + '\bin;' + $env:PATH

# 最终从零构建（交付路径；修复了 build.cmd 的 %~dp0 参数移位问题）
cmd /c D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\scripts\build.cmd clean

# 独立验证构建（验证专用目录，等价 CMake 参数）
powershell -NoProfile -ExecutionPolicy Bypass -File `
  D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\scripts\build.ps1 `
  -Project D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\projects\ecat_drone_powertrain `
  -Board ecat_drone_5e3y -BuildType flash_xip -Config debug `
  -BuildDir D:\Ecat_Drone_Vector_Powertrain\.work\build_verify
```

等价手动 CMake 参数：

```
-G Ninja -S <project> -B <builddir> -DBOARD=ecat_drone_5e3y
-DBOARD_SEARCH_PATH=D:\...\user_template\boards
-DHPM_BUILD_TYPE=flash_xip -DCMAKE_BUILD_TYPE=debug
```

两次构建（交付 `build.cmd clean` 与 verifier 的 `.work\build_verify`）均 **BUILD OK、0 warning**，内存数据一致（FLASH 因修复后源码略增，见下）。验证构建日志：`.work\build_verify\build_flash_xip.log`。

### 1.2 产物与体积

| 文件 | 大小 | 说明 |
|---|---|---|
| `output\ecat_drone_powertrain.elf`（最终构建） | 约 839 KB（839,xxx B） | 含调试信息 |
| `output\ecat_drone_powertrain.bin` | 约 162 KB（验证构建实测 161,800 B） | 烧录镜像（含 boot header / nor cfg） |
| `.text`（验证构建） | 148,052 B | `riscv32-unknown-elf-size` |
| `.data` | 2,640 B | |
| `.bss`（含 heap+stack） | 85,136 B | |

### 1.3 内存占用（链接器 `--print-memory-usage`）

**最终 `scripts/build.cmd clean`（修复后源码）：**

```
Memory region         Used Size  Region Size  %age Used
           FLASH:      156332 B         1 MB     14.91%
             ILM:       10000 B       128 KB      7.63%
             DLM:         32 KB       128 KB     25.00%
        AXI_SRAM:       42560 B       192 KB     21.65%
AXI_SRAM_NONCACHEABLE:  12448 B        64 KB     18.99%
        AHB_SRAM:          0 GB        32 KB      0.00%
```

验证构建（修复前源码）为 `FLASH: 154876 B (14.77%)`，其余区域数字相同。DLM 的 32 KB = 链接脚本默认 `_heap_size=0x4000`（16 KB）+ `_stack_size=0x4000`（16 KB）；应用代码不使用动态分配（见 §8 正面结论），heap 可后续按需调小。`AXI_SRAM_NONCACHEABLE` 为 DShot TX burst、舵机 TX/RX 帧、USB 端点等 DMA 缓冲。

### 1.4 警告清单（**0 条**）

- 最终构建与验证构建日志检索 `warning:` → **0 条**（`(Select-String <log> -Pattern 'warning:').Count = 0`）。
- 早期构建出现过、现已修复/消失的警告：
  - `SSC/StubSrc/ssc_stub.c:188 'stub_enter_state' defined but not used` → 已由 ecat-agent 修复；
  - `src/host_proto_app.c:224 'host_wr_i16' defined but not used` → 已由 comms-agent 修复；
  - Lead 提到的 dshot `s_tx_dma_buf`：最终代码为 `ATTR_PLACE_AT_NONCACHEABLE_BSS_WITH_ALIGNMENT(32)` 且被 DMA 引用（`src/dshot_bidir.c:253`），不产生警告。

### 1.5 语法检查

```powershell
python D:\...\user_template\scripts\check_syntax.py `
  --build-dir D:\Ecat_Drone_Vector_Powertrain\.work\build_verify `
  --project D:\...\projects\ecat_drone_powertrain `
  <proj>\src\*.c <proj>\port\*.c
# 输出: All 16 file(s) OK
```

（`-fsyntax-only` 不产生诊断；编译期告警以上面的真实构建为准。）

---

## 2. 编译期契约检查

方法：用**目标工具链、与 `src/app_task.c` 完全相同的编译选项**编译 `.work\build_verify\contract_check.c`（内含 `_Static_assert`），再用 `nm -S` 读取数组符号大小得到数值；避免“主机端 C 程序 ABI 与目标不一致”的问题。

```powershell
# 从 compile_commands.json 取 app_task.c 的编译命令，替换源文件/输出为 contract_check.c
cmd /c <app_task compile command with " -o ... -c ..." replaced>
# 输出: COMPILE_EXIT=0
riscv32-unknown-elf-nm.exe -S --defined-only contract_check.obj | findstr rpt_
```

### 2.1 结构体大小（全部 `_Static_assert` 通过，`nm` 实测值）

| 契约 | 期望 | 实测（`nm` 符号大小） | 结果 |
|---|---|---|---|
| `sizeof(ecat_rxpdo_t)` | 22 | `0x16` = 22 | ✅ |
| `sizeof(ecat_txpdo_t)` | 48 | `0x30` = 48 | ✅ |
| `sizeof(host_telemetry_t)` | 54 | `0x36` = 54 | ✅ |
| `HOST_PARAM_DESC_SIZE` | 70 | 70 | ✅ |
| `sizeof(host_param_desc_t)` | 70 | `0x46` = 70 | ✅ |
| `sizeof(host_dshot_tlm_t)` | 26 | `0x1a` = 26（固件内部 `dshot_telemetry_t` 为 28，经逐字段序列化，非 memcpy） | ✅ |
| `sizeof(host_servo_status_t)` | 20 | `0x14` = 20 | ✅ |
| `sizeof(host_adc_blob_t)` | 24 | `0x18` = 24 | ✅ |
| `sizeof(host_am32_status_t)` | 2 | 2 | ✅ |
| `PARAM_COUNT` | 60 | `0x3c` = 60 | ✅ |

### 2.2 字段偏移（`offsetof` 全部通过）

- RxPDO：`control_word@0, esc_cmd_us@2, servo_angle_d10@6, servo_ctrl_word@10, dshot_cmd@12, reserved0@16, cycle_counter@18`（`dshot_cmd[2]` 占 12..15，故 reserved0 在 **16**、cycle_counter 在 **18**；与修正后的 `inc/ecat_pdo.h:43-44`、`README_zh.md:157-158` 一致）。
- TxPDO：`status_word@0, fault_code@2, esc_erpm@4, esc_tlm_period_us@12, servo_pos_d10@16, servo_speed_rpm@20, servo_current_ma@24, servo_temp_c@28, servo_error@30, sys_voltage_dv@32, sys_current_ca@34, esc_current_ma@36, loop_counter@40, led_status@42, ecat_link_status@44, reserved0@46`。
- `host_telemetry_t`：0/2/4/12/16/20/24/28/30/32/34/36/40/42/44/46/50（`host_rx_frames@46`，`host_crc_errors@50`）。
- `host_param_desc_t`：与 `HOST_PARAM_DESC_OFF_*` 逐项一致（id@0, type@2, persist@3, readonly@4, len@5, min@6, max@10, def@14, value@18, name@22, desc@38，name 16 B / desc 32 B）。
- 协议常量：`HOST_PROTO_OVERHEAD == 8`；`HOST_PROTO_LIST_PARAMS_MAX(7) × (1+70) = 491 ≤ APP_CFG_HOST_PROTO_MAX_PAYLOAD(512)`。

结论：**契约检查全部通过（COMPILE_EXIT=0）**。

---

## 3. 模块集成审计

### 3.1 主循环顺序（`src/main.c:68-98`）与架构文档一致

```
ecat_app_task() → dshot_task() → servo_task() → adc_mon_task() → led_task()
→ usb_cdc_app_task() → app_task_run()
```
`app_task_run()` 内部按 `g_app_tick_ms` 分频到 1 kHz（`src/app_task.c:349-355`）；初始化顺序 `app_task_init()`（param→adc→led→servo→dshot→param_apply→usb）→ `ecat_app_init()`（`src/main.c:59-62`）符合 `docs/ARCHITECTURE.md`。

### 3.2 进程映像全局变量的写入者（契约检查项）

| 变量 | 定义 | 写入者 | 读取者 |
|---|---|---|---|
| `g_ecat_rxpdo` | `src/ecat_app.c:63` | 仅 `ecat_app.c`（`memset` :150、`APPL_OutputMapping` memcpy :518、`APPL_StopOutputHandler` 清 control_word :477） | `app_task.c:253`（逐字节快照） |
| `g_ecat_txpdo` | `src/ecat_app.c:64` | 仅 `app_task.c:137-203` | `ecat_app.c:505`（`APPL_InputMapping`）、`host_proto_app.c:531`（快照） |
| `g_ecat_op_active` | `src/ecat_app.c:65` | `ecat_app.c:127/152/221/528`（AL 状态驱动）；SSC 桩在 `ECAT_SSC_STUB_TEST_MODE` 下也会写（`SSC/StubSrc/ssc_stub.c:92/141`，默认关闭） | `app_task.c:73/98/281/309`、`host_proto_app.c` |

结论：无模块写其它模块私有全局变量；PDO 双向拷贝路径符合设计。

### 3.3 头文件声明 ↔ 实现 ↔ 调用方（抽查覆盖）

| 头文件 API | 实现 | 调用方（证据） |
|---|---|---|
| `host_proto_app_bench_active` | `host_proto_app.c` | `app_task.c:267` |
| `dshot_enable/set_throttle/send_command/get_telemetry/task/init` | `dshot_bidir.c` | `app_task.c:309/314/316/146`、`host_proto_app.c`、`main.c:75` |
| `servo_set_target_d10/set_enable/clear_fault/get_status/task/init/scan_bus` | `servo_s288.c` | `app_task.c:290-337`、`host_proto_app.c`、`main.c:78` |
| `led_set_code/get_status_word/task/init/set_brightness/set_rgb/test_pattern` | `led_rgb.c` | `app_task.c:104-126/171`、`host_proto_app.c`、`param.c:662` |
| `adc_mon_get/task/init/apply_params` | `adc_mon.c` | `app_task.c:159/210`、`param.c:665`、`main.c:81` |
| `param_get/set/save/load/reset_defaults/apply/get_desc/count` | `param.c` | 全工程（`ecat_app.c`、`host_proto_app.c`、各模块） |
| `ecat_app_get_al_state/get_link_status` | `ecat_app.c` | `app_task.c:74/142/165`、`host_proto_app.c:575` |
| `usb_cdc_app_init/task/read/write/tx_free/get_stats` | `usb_cdc_app.c` | `main.c:87`、`host_proto_app.c` |
| `am32_4way_begin/xfer/end` | `am32_4way.c` | `host_proto_app.c` |
| `dshot_line_park/release/set_gpio/read_gpio/timebase_raw/freq` | `dshot_bidir.c` | `am32_4way.c` |

链接成功（§1）本身证明“被调用且已实现”的符号完备；未被调用的声明见 §3.5。

### 3.4 资源归属（对照 `docs/PINMAP.md` 第 7 节，代码证据）

| 资源 | 代码使用 | 结论 |
|---|---|---|
| GPTMR0 ch0/ch1 | board 包（delay / `board_timer_create`） | ✅ 无冲突 |
| GPTMR1 ch2/ch3 | `dshot_bidir.c`（`BOARD_DSHOT_CAP_*`，GPTMR1 捕获） | ✅ |
| GPTMR2 ch0 | `ecat_config.h:65-68`（`ECAT_TIMER_GPTMR`，EtherCAT 1 ms tick） | ✅（PINMAP 已补） |
| PWM0 计数器1 + CMP4–7 | `dshot_bidir.c:131-135`（输出 2/3） | ✅ |
| PWM0 CMP16 → pwm_dma_0 | `dshot_bidir.c:136-138,607-611,671`（TX DMA 周期触发） | ✅（PINMAP 已补） |
| PWM1 计数器2/3 + CMP8–13 | `led_rgb.c`（`BOARD_RGB_LED_*`） | ✅ |
| XDMA ch0 | `dshot_bidir.c:149-154`（`HPM_XDMA`，TRGM DMACFG_0 → MOT_0） | ✅ |
| HDMA ch4–7 | `servo_s288.c:150-153`（`BOARD_APP_DMA0` = `HPM_HDMA`） | ✅ |
| ADC0 IN1/IN8、ADC1 IN3/IN13 | `adc_mon.c`（`BOARD_ADC*_*_CH`） | ✅ |
| Flash EEPROM 0x80000 + 64 KB / 参数 0x7E000 + 4 KB | `ecat_config.h:39-45`、`param.h:41-42` | ✅ 不重叠（§6.4） |

中断优先级（代码证据）：DShot PWM/GPTMR1 = **1**（`dshot_bidir.c` init）；舵机 UART = 2、HDMA = 3（`servo_s288.c:155-156,1120-1122`）；USB = 2（`usb_cdc_app.c:329`）；ECAT RESET = 2、SYNC0/1 = 3、PDI = 4、ECAT 定时器 GPTMR2 = 3（`port/hpm_ecat_hw.c:300-306`）。
审计结论：无同一中断向量被两个模块注册；最高优先级留给 DShot 时序；SYNC0/1 与舵机 DMA 同为 3、低于舵机 UART/USB(2)，存在 µs 级 SYNC 抖动，1 ms 周期下可接受（见 §8-L5）。

### 3.5 未接线/冗余 API（低风险，建议交付前清理）

- `ecat_app_outputs_enabled()`（`ecat_app.c`）无调用者（app_task 直接用 `g_ecat_op_active`）。
- `ecat_led_set_run()` / `ecat_led_set_err()`（`ecat_app.c`）除自身外无调用者；ESC 侧 RUN/ERR 由 `ecat_write_led_overrides()` 写 ESC 寄存器，SSC 桩不回调这两个函数（接真实协议栈时需确认是否需要挂接）。
- `ecat_ssc_stop()`（`ecat.c`）无调用者。
- `ecat_app_get_sync0_period_us()`/`get_sync1`/`get_dc_shift_ns`/`get_dc_mode`/`get_watchdog_ms`（`ecat_app.c`）未被任何代码调用（DC 默认值在 `ecat_app_apply_params()` 内直接写 ESC 寄存器）。
- `s_last_input_tick_ms`（`ecat_app.c:74`）只写不读（`APPL_InputMapping` 中更新），属诊断冗余。

---

## 4. EtherCAT PDO / CoE 一致性

### 4.1 进程映像拷贝实现

- `APPL_GenerateMapping()`（`src/ecat_app.c`）固定返回 `*pInputSize=APP_ECAT_TXPDO_SIZE(48)`、`*pOutputSize=APP_ECAT_RXPDO_SIZE(22)`；`ecat.c` 在 `MainInit()` 后调用并校验，尺寸不符会打印错误。
- `APPL_InputMapping()`：`memcpy(pData, &g_ecat_txpdo, min(nPdInputSize,48))`，小端 packed 结构逐字节拷贝安全。
- `APPL_OutputMapping()`：`memcpy(&g_ecat_rxpdo, pData, min(nPdOutputSize,22))`。
- PDO 尺寸来自 SSC 配置/ESI，与本工程的编译期结构一致（§2）。

### 4.2 ESI `SSC/ESI/HPM_ECAT_DRONE.xml` PDO 映射（#x1600 / #x1A00）

RxPDO（`Sm="2"`，`#x1600`）：**10 个 Entry，2+2+2+2+2+2+2+2+2+4 = 22 B**

| # | ESI 对象 | 类型/位长 | 字节偏移 | `ecat_pdo.h` 字段 |
|---|---|---|---|---|
| 1 | 0x7000 | UINT/16 | 0 | control_word |
| 2 | 0x7001 | UINT/16 | 2 | esc_cmd_us[0] |
| 3 | 0x7002 | UINT/16 | 4 | esc_cmd_us[1] |
| 4 | 0x7003 | INT/16 | 6 | servo_angle_d10[0] |
| 5 | 0x7004 | INT/16 | 8 | servo_angle_d10[1] |
| 6 | 0x7005 | UINT/16 | 10 | servo_ctrl_word |
| 7 | 0x7006 | UINT/16 | 12 | dshot_cmd[0] |
| 8 | 0x7007 | UINT/16 | 14 | dshot_cmd[1] |
| 9 | 0x7008 | UINT/16 | 16 | reserved0 |
| 10 | 0x7009 | UDINT/32 | 18 | cycle_counter |

TxPDO（`Sm="3"`，`#x1A00`）：**24 个 Entry，合计 48 B**，顺序/长度与 `ecat_txpdo_t` 逐字段吻合：
0x6000 status_word(16@0) → 0x6001 fault_code(16@2) → 0x6002/0x6003 esc_erpm(32@4/8) → 0x6004/0x6005 esc_tlm_period_us(16@12/14) → 0x6006..0x600B servo_pos/speed/current(16@16/18/20/22/24/26) → 0x600C..0x600F servo_temp/error(8@28/29/30/31) → 0x6010 sys_voltage_dv(16@32) → 0x6011 sys_current_ca(16@34) → 0x6012/0x6013 esc_current_ma(16@36/38) → 0x6014 loop_counter(@40) → 0x6015 led_status(@42) → 0x6016 ecat_link_status(@44) → 0x6017 reserved(@46)。
总长校验：**48 B** ✅
`SSC/ecat_drone_application.csv` 的对象顺序/类型与 ESI 相同（同一生成器产物）；**已确认与 `inc/ecat_pdo.h` 实际内存布局完全一致**（Rx 的 reserved0/cycle_counter 偏移为 16/18，与修正后的头文件注释、README 表格一致）。

### 4.3 ESI 设备身份 / 端口 / SM / DC

| 项目 | ESI 值 | 与代码一致性 |
|---|---|---|
| Vendor Id | `#x00000E5A` | ✅ `app_config.h:29` |
| Product Code | `#x0000E3A1` | ✅ `app_config.h:30` |
| Revision No | `#x00010001` | ✅ `app_config.h:31` |
| Device Physics | `"YY"` = Port0+Port1 两个端口 | ✅ 两片内 MII PHY |
| MBoxOut/MBoxIn | `0x80` B @ `#x1000`/`#x1080`，ControlByte 0x26/0x22 | ✅ 与 SSC 配置 mailbox 一致 |
| Outputs SM2 | `DefaultSize=22` @ `#x1100` | ✅ RxPDO 22 B |
| Inputs SM3 | `DefaultSize=48` @ `#x1400` | ✅ TxPDO 48 B |
| FMMU | Outputs / Inputs / MBoxState | ✅ |
| DC | `<Dc>` + `<OpMode>` + `AssignActivate #x300` + `CycleTimeSync0/1` | ✅ `DC_SUPPORTED=1` |

### 4.4 CoE 对象 0x2000..0x203B（60 项）类型复核 —— 原 11 项缺陷已修复

- 映射规则：对象索引 `0x2000 + PARAM_ID` ↔ `param_id_t`（`ecat_app.c` `ECAT_COE_PARAM_OBJECT_BASE` / `ecat_coe_param_id()`）；`ecat_coe_param_id()` 用 `param_count()` 做边界检查，**不会越界**。
- 读写宽度校验：`ecat_coe_param_width()` 由描述符类型得到 1/2/4/16 B，SDO 的 `size` 必须相等，否则返回 `ABORTIDX_DATA_TYPE_MISMATCH`；`readonly` 拒绝写；范围错误映射 `ABORTIDX_VALUE_EXCEEDED`；I16 写入先符号扩展。
- 32 位对象访问：当前 `ecat_coe_obj_read/write()` 使用**两次 16 位访问**（`ecat_app.c:627-628`、`:674-675`，`*pData` / `*(pData + 1)`），**没有** `UINT32` 指针强转（复核结论：不存在我此前担心的非对齐 32 位访问问题，M3 不适用）。
- 复验方法（脚本：`.work\build_verify\check_coe_types.py`）：从 `src/param.c` 的 `s_param_desc[]` 提取 60 个描述符类型（权威）→ 与 `SSC/ecat_drone.xlsx`（sheet1 列 E）、`SSC/ecat_drone_application.csv`（DataType 列）、`SSC/ESI/HPM_ECAT_DRONE.xml`（Dictionary 中 `<Type>`）逐对象比对。输出：

```
enum entries: 60 table entries: 60 csv: 60 xlsx: 60 esi: 60
mismatches: 0 extra ids: none
RESULT: all 60 CoE objects match src/param.c descriptors (csv+xlsx+esi)
```

- 历史问题（已修复，记录备查）：此前 xlsx/CSV/ESI 中有 11 个对象类型错误（0x200D/0x200F/0x2016/0x2018/0x201A/0x201C/0x201E/0x2020/0x2023 错误声明为 UDINT(4B)、0x2039/0x203B 有符号性错误），会导致真实 SSC 协议栈下这些参数的 SDO 读写被 `ecat_app.c` 的宽度校验拒绝。根因是 `SSC/tools/gen_ssc.py` 原先对 `inc/param.h` 中无类型注释的第二个通道参数默认 `u32`；ecat-agent 已修复：生成器现在从 `src/param.c` 描述符读取权威类型（`gen_ssc.py:217`）并与 `param.h` 注释交叉校验（:219-228），重新生成后 60/60 一致。
- 配置文件 `SSC/Config/HPM_ECAT_DRONE_Config.xml`：VendorID/ProductCode/RevisionNo/DEVICE_NAME 与 `app_config.h` 一致；`COE_SUPPORTED=1`、`ESC_EEPROM_EMULATION=1`、`ESC_EEPROM_SIZE=0x800`、`ECAT_TIMER_INT=1`、`MAX_PD_INPUT_SIZE/OUTPUT=0x40`；应用回调名 `ecat_coe_obj_read/write` 与 `ecat_app.c` 实现一致。`SSC/README.md` 与 `docs/ECAT_SSC.md` 给出 SSC Tool 生成步骤（本轮未实际执行 SSC Tool，见 §9）。

### 4.5 AL 状态 / 看门狗

- `g_ecat_op_active` 由 AL 状态驱动：`ecat_set_al_state()`、`ecat_app_task()`、`APPL_Application()` 统一按 `s_al_state == OP` 计算；`APPL_StartOutputHandler/StopOutputHandler` 在进入 OP 时置位、退出 OP 时清零并清 RxPDO control_word。
- `ecat_app_watchdog_expired()` 优先读 ESC 硬件 `WDG_STAT_PDAT`，否则以 `s_last_output_tick_ms`（`APPL_OutputMapping` 更新）为基准。
- **app_task 看门狗（Lead 已修复，复验通过）**：`src/app_task.c:276-293` 现在
  - `g_ecat_op_active == 0` 时：刷新 `s_last_ecat_cycle`、重装超时、并 `app_state_clear_fault(APP_FAULT_ECAT_OP_TIMEOUT)` 自动恢复；
  - 仅在 OP 内且 `cycle_counter` 停止变化超过 `PARAM_ECAT_WATCHDOG_MS` 时置 `APP_FAULT_ECAT_OP_TIMEOUT`。
  这消除了“进入 OP 前/离开 OP 后误锁存 ECAT_OP_TIMEOUT、后续 OP 无法使能输出”的行为风险。

---

## 5. 上位机与 Host 协议验证

### 5.1 构建与自检（实测命令与输出）

```bat
cd /d D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\tools\ecat_drone_host
build.cmd
::  compiler: C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
::  sources : 29 file(s)
::  [BUILD OK] ...\build\ecat_drone_host.exe  size: 216576 bytes
build.cmd -Selftest
::  自检结果: 通过 43 项，失败 0 项
::  结论: 全部通过 (SELFTEST PASS)（uitest 同批 PASS）
```

`build.cmd` / `build.ps1` / `run.cmd` / `selftest.cmd` / `README_zh.md` 均已存在。

### 5.2 `inc/host_proto.h` ↔ `src/host_proto_app.c` ↔ C# 工具 交叉核对

| 项目 | 固件实现 | C# 实现 | 文档 | 结果 |
|---|---|---|---|---|
| 帧格式 | `A5 5A CMD SEQ LEN_L LEN_H payload CRC16`；`host_emit_response` 先写 status | `HostProtocol.cs` 一致 | `docs/USB_PROTOCOL.md` | ✅ |
| CRC16-CCITT-FALSE 覆盖 CMD..payload | `host_proto_crc16`，覆盖 `[2, 6+payload)` | `Crc16.CcittFalse(frame,2,4+payloadLen)` | 一致 | ✅ |
| PING | 应答 body = u32 magic + u16×3 = 10 B，加 status = **LEN 11** | `BoardClient.Ping()` 解析 magic@0、版本@4/6/8 | `USB_PROTOCOL.md` PING 应答 LEN=11 | ✅ |
| GET_TELEMETRY | 54 B `host_telemetry_t` 逐字段填充并快照 `g_ecat_txpdo` | `TelemetryData.Parse` 偏移 0/2/4/8/12/14/16/18/20/22/24/26/28/29/30/31/32/34/36/38/40/42/44/46/50，`WireSize=54` | 一致 | ✅ |
| 参数描述符 70 B | `host_fill_param_desc` + packed struct | `ParamDesc.cs` 偏移/长度与 `HOST_PARAM_DESC_OFF_*` 完全一致 | 一致 | ✅ |
| LIST_PARAMS 分块 | 每应答 ≤ `HOST_PROTO_LIST_PARAMS_MAX=7` 条，带 start/count | 按返回条数迭代 | `USB_PROTOCOL.md` | ✅ |
| SERVO_SCAN 异步重试 | 扫描运行中返回 `ERR_BUSY`，完成后 OK+32 B bitmap | 收到 `ErrBusy` 重试，最长 12 s（`BoardClient.cs`） | 一致 | ✅ |
| SERVO_RAW 布局 | 请求 `u8 bus,u8 id,u16 reg,u8 wr_len,u8 rd_len,u8[wr_len] data`；应答 `u8 rd_len_actual,u8[rd_len_actual] data` | 请求/应答构造与固件一致；S288 不支持寄存器访问时返回 `ERR_UNSUPPORTED`，界面显示“不支持” | `USB_PROTOCOL.md` | ✅ |
| AM32_STATUS | 请求 `u8 esc`；应答 `u8 state,u8 last_error` | `Am32BridgeStatus.Parse` 读 `body[0],body[1]` | `USB_PROTOCOL.md` | ✅ |
| Bench 租约刷新 | `DSHOT_TEST`/`DSHOT_CMD`/`SERVO_MOVE_TEST` 均调用 `host_bench_refresh()`；超时/进 OP 自动释放并停机 | 上位机每次测试命令刷新；文档要求 ≥2 Hz 刷新 | `USB_PROTOCOL.md` §6 | ✅ |

### 5.3 交付的回归测试（`projects\ecat_drone_powertrain\tests\`）

```powershell
$py = D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\tools\python3\python.exe
$t  = D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\projects\ecat_drone_powertrain\tests
& $py $t\test_host_proto.py      # All 238 checks OK
& $py $t\test_servo_frames.py    # RESULT: 58 passed, 0 failed — ALL TESTS PASSED
& $py $t\dshot_sim.py            # ALL PASSED
```

三套脚本覆盖：28 条 `HOST_CMD_*`、60 个参数、54 B 遥测、70 B 描述符、帧 CRC/长度/错误码；舵机 20 B/26 B 帧与 CRC32、扫描位图语义、厂商 CRC 随机帧比对；DShot 4096 组遥测载荷解码、±5% ESC 时钟+抖动下 0 错误、损坏帧全部拒收。测试使用说明见 `tests/README.md`。

---

## 6. 参数系统

### 6.1 表完整性

- `param.h` 枚举 60 项（`PARAM_ECAT_NODE_ALIAS=0` … `PARAM_SERVO_KD2=59`，`PARAM_COUNT=60`），编译期实测 `PARAM_COUNT=60`（§2.1）。
- `src/param.c` 使用 **designated initializer** `[PARAM_xxx] = {...}` 填满 60 个 id，脚本复核：`enum entries: 60  table entries: 60`（无缺失 id、无 id 0x2000..0x203B 缺项）。
- 编译期守护：`_Static_assert(APP_ARRAY_SIZE(s_param_desc)==PARAM_COUNT)`；对 11 组双通道 `PARAM_xxx1+1==PARAM_xxx2` 及 `PARAM_xxx(ch)` 宏展开做了静态断言；配合 `param_init()` 运行期对空描述符的检查。
- Lead 给出的 id 映射（KP1/2=56/57、KD1/2=58/59）与枚举一致。
- min/max/default 抽查：`PARAM_DSHOT_RATE` 额外限定 150/300/600/1200；`PARAM_SERVO_BAUD` 上限 6,000,000；`PARAM_DSHOT_MIN/MAX_THROTTLE` 默认 48/2047；`PARAM_ADC_ENABLE_PROTECTION` 默认 0（未标定前关闭保护，与 bring-up 文档一致）；`PARAM_ADC_VOLT_MIN/MAX_DV` 默认 0=禁用。默认值满足 min≤def≤max；**未强制校验** `PARAM_SERVO_ANGLE_MINn < MAXn` 的成对关系（低风险，见 §8-L4）。

### 6.2 校验与类型

- `param_check_type()`：U8/U16/U32 负值与上界、I16 范围、I32 直接接受；`param_check_value()` 再做 min/max 与 DShot rate 枚举校验。
- `param_clamp()`：从 Flash 读出的值先按类型/范围钳位，异常值回退默认值；`persist=0` 的参数（`PARAM_USB_AUTO_TELEMETRY_HZ`）加载时强制默认值。

### 6.3 Flash 存储安全性

- 存储位置：XIP 地址 = `BOARD_FLASH_BASE_ADDRESS + PARAM_FLASH_OFFSET(0x7E000)`，4 KB 扇区（`param.h:41-42`）。
- 数据块 `param_flash_blob_t`：头部 16 B（`magic=0x41524950`、`version=1`、`count=60`、`crc16`、reserved、pad）+ 60×i32 值，**静态断言 = 恰好 256 B = 一个 NOR page**，且 `values` 偏移 = 16、CRC 2 字节对齐。
- CRC：CRC16-CCITT-FALSE 覆盖整个 blob（计算时 crc 字段置 0xFFFF），保存/加载用同一算法；校验 magic + version + count + CRC，任一不符 → `PARAM_ERR_FLASH`。
- 擦写流程（`param_flash_store`）：关全局中断 → 首次调用 `rom_xpi_nor_auto_config()` 并缓存配置 → 擦除 `PARAM_FLASH_OFFSET..+0x1000` 一个扇区 → 编程 256 B → `fencei()` → **回读 memcmp + 再次校验**；失败计数并返回错误。空片（全 0xFF）或损坏块均走“使用默认值 + 告警”路径，不会写坏代码区（擦除地址固定为参数扇区）。
- 与代码区/EEPROM 区关系：固件 FLASH 占用 **156,332 B**，远小于参数区起点 0x7E000（516,096 B），占上限 **30.3%**；bin 约 162 KB。参数区 0x7E000-0x7EFFF 与 EtherCAT EEPROM 模拟区 0x80000-0x8FFFF（16×4 KB，`ecat_config.h:39-45`）**不重叠**，中间 0x7F000-0x7FFFF 空闲。链接器未提供 0x7E000 的链接期硬上限（`param.h` 注释提及“linker script 中有检查”，实际 SDK `flash_xip.ld` 未包含该检查）——当前 30% 余量足够，建议后续在链接脚本/CMake 加 `ASSERT(_flash_end < 0x7E000)` 硬保护（低风险）。

### 6.4 运行期风险（M1 已修复）

- **已修复**：`HOST_CMD_PARAM_SAVE` 现在在 EtherCAT OP 中直接返回 `HOST_STATUS_ERR_NOT_READY`（`host_proto_app.c:688-689`），避免 Flash 擦除/编程期间关全局中断（典型数 ms~数十 ms）导致主站通信看门狗触发。非 OP 时保存行为不变（单扇区擦除 + 回读校验）。

---

## 7. 文档一致性

| # | 文档位置 | 文档说法 | 代码/实际 | 处理 |
|---|---|---|---|---|
| 1 | `inc/ecat_pdo.h` 注释、`README_zh.md` §5 | reserved0@14、cycle_counter@16 | 实际 packed 布局 **16/18**（dshot_cmd[2] 4 B） | **已修复**（Lead；`ecat_pdo.h:43-44`、`README_zh.md:157-158`） |
| 2 | `SSC/ecat_drone.xlsx` / CSV / ESI | 11 个 CoE 对象类型错误（UDINT 等） | 实际 60 项类型见 `src/param.c` | **已修复**（ecat-agent 重新生成；本轮 60/60 复验通过，§4.4） |
| 3 | `docs/PINMAP.md` §7 | “Flash 0x80000 起 16KB” EtherCAT EEPROM 模拟区 | `ecat_config.h:40-45`：`FLASH_EEPROM_SIZE=0x10000`（64 KB = 16×4 KB 扇区） | **已修复**（verifier） |
| 4 | `docs/PINMAP.md` §7 | 资源表缺 GPTMR2 ch0（EtherCAT 1 ms tick）、PWM0 CMP16（DShot TX DMA 触发）、ECAT 中断；列了未使用的 `IRQn_XDMA` | `ecat_config.h:65-68`、`dshot_bidir.c`、`port/hpm_ecat_hw.c:300-306` | **已修复**（verifier 补充/更正资源表） |
| 5 | `docs/ARCHITECTURE.md` §6 / §2 | 参数“约 70 项”；DShot 资源未提 CMP16 | `PARAM_COUNT=60`；CMP16 用作 TX DMA 请求 | **已修复**（verifier：改为 60 项、补充 CMP16） |
| 6 | `inc/dshot.h` | 原来说“PWM0 counter1 reload → PWM DMA request channel 1 → `HPM_TRGM0_DMA_SRC_PWM0_1`”，与实现不符 | 实现用独立比较点 **CMP16** 映射为 `pwm_dma_0` → `HPM_TRGM0_DMA_SRC_PWM0_0`（`dshot_bidir.c:26-31,136-138,153,607-611,671`，与 SDK `pwmv2_enable_dma_at_compare_point()` 用法一致） | **已修复**（Lead：`inc/dshot.h:31` 现为 `HPM_TRGM0_DMA_SRC_PWM0_0`） |
| 7 | 根 `README_zh.md` §1/§4 | 引用 `tools/ecat_drone_host/README_zh.md` | 该文件此前缺失 | **已修复**（Lead/host-agent：`README_zh.md` 已交付） |
| 8 | 工程 `README_zh.md` §1 引脚表 | “PC08/09 + PC16；PC14/15 + PD00” 顺序易误读 | 实际 pinmux/board.h：PC14=UART3_RXD、PC15=UART3_TXD（`boards/ecat_drone_5e3y/pinmux.c:182-184`，与 `docs/PINMAP.md:50-51` 一致） | **已修复**（Lead：已明确 TX/RX 表述） |
| 9 | `docs/USB_PROTOCOL.md` | PING LEN=11、遥测 54 B（LEN=55）、描述符 70 B、LIST≤7、SERVO_RAW/AM32_STATUS/bench 租约语义 | 与 `inc/host_proto.h` + `host_proto_app.c` + C# 实现一致（§5.2） | ✅ 一致 |
| 10 | `docs/PINMAP.md` §1-§6 | 引脚/外设映射 | 与 `board.h`/`pinmux.c` 一致（UART2 PC08/09、UART3 PC14/15、DShot PF02/03、RGB PF12-14、ADC PF26/PF16/PF20/PF19） | ✅ 一致 |
| 11 | 板级 `README_zh.rst` | EEPROM 0x80000、应用镜像最大 512 KB、UART3 PC14 RXD/PC15 TXD、UART2/3 时钟 48 MHz | `ecat_config.h`、`clock.c`、`pinmux.c` 一致 | ✅ |
| 12 | 根 `README_zh.md` §4 / 工程 README | 指向 `docs/VERIFICATION.md`、`docs/DSHOT_AM32.md`、`docs/SERVO_S288.md`、`SSC/README.md` | 这些文件均已存在 | ✅（本文件即 VERIFICATION.md） |

verifier 本轮只重写本文件（write 工具，UTF-8）；此前仅修改过文档：`docs/PINMAP.md`（§7 资源表）、`docs/ARCHITECTURE.md`（60 项 / CMP16）。**未修改任何源码/契约头文件。**

---

## 8. 代码审查发现（按严重级别）

> 审查范围：`src/*.c`、`port/*.c`、`SSC/StubSrc`、`inc/*.h` 的常见嵌入式缺陷模式（ISR 共享数据、volatile、除零、外部数据索引、无界循环、ISR 中 printf、栈占用、中断优先级）；并用 `grep`/脚本做了系统性检索。

**Critical：0 项。**

**中等（中）**

| ID | 位置 | 发现 | 状态/影响/建议 |
|---|---|---|---|
| M1 | `host_proto_app.c` `host_cmd_param_save` | `PARAM_SAVE` 在 EtherCAT OP 中也会执行；Flash 擦除/编程期间关全局中断（典型数 ms~数十 ms），主站可能判定通信中断 | **FIXED（Lead）**：OP 中现在返回 `HOST_STATUS_ERR_NOT_READY`（`host_proto_app.c:688-689`），复验通过 |
| M2 | `am32_4way.c:252-317` | 文档称最坏阻塞 ~150 ms；实际最坏 ≈ TX（208 B@19200 ≈ 108 ms）+ RX 上限 150 ms ≈ **260 ms**，期间主循环（含 EtherCAT 栈）被独占 | **保留为已知限制**：AM32 4-way 调参仅在非 OP 使用且 XFER 只允许在桥已 BEGIN（非 OP）时执行；文档已注明最坏阻塞时间。若主站在调参过程中启动，仍可能出现 EtherCAT 通信中断，需在硬件联调时确认/收紧 `AM32_4WAY_MAX_RESPONSE` 或总超时 |
| M3 | `ecat_app.c` CoE 读写 | 曾担心 32 位 CoE 对象通过 `*(UINT32*)pData` 访问导致 RISC-V 非对齐异常 | **复核结论：不适用（NOT APPLICABLE）**。当前实现为两次 16 位访问 `*pData` / `*(pData + 1)`（`ecat_app.c:627-628`、`:674-675`），无 `UINT32*` 强转，不存在该问题 |

**低（低）**

| ID | 位置 | 发现 | 建议 |
|---|---|---|---|
| L1 | `usb_cdc_app.c` USB RX 溢出路径 | USB 中断上下文调用 `LOGM_W`（UART0 printf 阻塞） | 改为计数/置标志，主循环打印 |
| L2 | `ecat_app.c`、`ecat.c` | 未接线/冗余 API 与只写变量：`ecat_app_outputs_enabled`、`ecat_led_set_run/err`、`ecat_ssc_stop`、5 个 DC getter、`s_last_input_tick_ms` | 交付前清理或在真实协议栈 glue 中挂接（尤其 LED 回调） |
| L3 | `ecat_app.c` vs `app_task.c` | 两套独立看门狗（ESC `WDG_STAT_PDAT` + 软件 cycle_counter）；app_task 未使用 `ecat_app_watchdog_expired()` | 保留其中一套或明确主从关系，避免行为分叉 |
| L4 | `param.c` / `servo_s288.c` | 未强制 `PARAM_SERVO_ANGLE_MINn < MAXn`；若配置反了，目标角会被钳到 max | 在 `param_set()`/`servo_set_target_d10()` 增加成对校验 |
| L5 | `port/hpm_ecat_hw.c:300-306` | ECAT SYNC0/1 与舵机 HDMA/ECAT 定时器同为优先级 3，低于 USB/舵机 UART（2） | 1 ms 周期下可接受；若 DC 抖动敏感可把 SYNC 提到 1~2 |
| L6 | `usb_cdc_app.c` RX 路径 | USB 中断写 / 主循环读 CSTD 环形缓冲，依赖 CherryUSB `usb_ringbuffer` 自身内存序保证，未见显式屏障 | 常规用法可接受；静态分析无法证明，建议硬件长跑验证 |
| L7 | `am32_4way.c:118-124` 等 | 位翻转 bit-bang 使用忙等（19200 baud 下每 bit 52 µs） | 属维护路径且 BEGIN 已限制 OP，可接受；文档已说明 |

**正面结论（重点抽查未发现问题）**

- 无 `malloc/calloc/free` 等动态分配（全工程检索）。
- 外部数据索引均做边界检查：`dshot_set_throttle`、`dshot_get_telemetry`、`dshot_send_command`、`dshot_line_*`、`servo_*`（ch/bus）、`host_proto_app` 的 esc/bus/param id/长度、`ecat_coe_param_id()`（以 `param_count()` 为界）。
- 除零防护：`dshot_telemetry_to_erpm`（period==0）、`dshot_decode_edges`（slot==0/边缘数）、LED 呼吸 `period<2`、舵机波特率计算（baud 下限）；`app_task_cmd_us_to_dshot` 除数为编译期常量 1000。
- ISR 中无 `printf`（唯一例外 L1 的溢出告警）；DShot PWM/GPTMR1 ISR 只做短操作与标志更新；无大局部数组（最大 28 B `uint32_t delta[24]` 在 `dshot_decode_edges` 主循环路径，ISR 无大数组）。
- DMA 缓冲：DShot `s_tx_dma_buf` 32 字节对齐 + non-cacheable，舵机 TX/RX 帧 non-cacheable，USB 端点缓冲对齐 + non-cacheable。
- ISR 与主循环共享标志均 `volatile`：`rx_armed/rx_edges/rx_edge_ts`、舵机 `tx_done/rx_done/rx_idle/dma_error`、USB `s_tx_busy` 等；共享 ring 的入队/出队有临界区。
- 栈：默认 `.stack` 16 KB（单线程裸机，ISR 共用），无递归、无大局部数组；未做 `-fstack-usage` 实测（见 §9）。

---

## 9. 未验证项与硬件测试建议

| 项目 | 状态 | 建议 |
|---|---|---|
| **真实 Beckhoff SSC 协议栈** | 未验证：`SSC/Src` 不存在（授权原因），本固件链接的是 `SSC/StubSrc`，**没有真正的 EtherCAT 通信/CoE/SM watchdog/DC** | 按 `SSC/README.md` + `docs/ECAT_SSC.md` 用 SSC Tool 生成 `SSC/Src` 后重新构建；生成时注意 `HPM_ECAT_DRONE_Config.xml` 的 `APPLICATION_FILE=#include "ecat_drone.h"` 需要与实际应用头文件对应（生成后首次编译需确认）；随后在 TwinCAT 下验证 PDO 映射、AL 状态、SM 看门狗与 DC |
| DShot 双向时序（PF02/PF03、GPTMR1 捕获、21bit→GCR→eRPM） | 仅软件仿真（`tests/dshot_sim.py` ALL PASSED），无硬件波形 | 示波器验证 idle 高、16 bit 帧、反相 CRC、ESC 回传窗口；接 AM32 实测 eRPM |
| AM32 4-way 桥（19200 8N1 位翻转、半双工） | 无硬件验证；已知限制见 §8-M2（最坏阻塞约 260 ms） | 用 AM32 电调实测 EEPROM 读/写；确认最长阻塞对主循环的影响，必要时收紧响应上限 |
| S288 6 Mbps 单总线 + SPDT 方向切换 | 单元测试通过（帧构造/解析 58 项），无波形 | 示波器验证 6 Mbps 波形、TX→RX 切换时序（2 µs setup）、CRC32 覆盖范围；先单舵机再双舵机 |
| ADC 标定（系统电压/电流、ESC 电流）与保护阈值 | 代码逻辑审计通过，无实机标定 | 万用表对比标定 `PARAM_ADC_*`；确认量程/偏置/极性；标定后再开 `PARAM_ADC_ENABLE_PROTECTION` |
| 片内 MII PHY（MDIO 地址 1/2、复位、NMII 链路极性） | 代码依据《片内 MII PHY 指南》实现，无硬件验证 | 上电看 `ecat_phy_mdio_probe()` 日志、插拔网线看 link 位；必要时调整 `BOARD_ECAT_PHY_*` / `*_LINK_INVERT` |
| USB CDC 枚举与 PC 工具联调 | 自检使用 FakeFirmwareTransport（模拟设备），非真实 USB | 实板接 PC，验证 VID/PID 0x0A4D:0xE3A2、PING/遥测/参数/AM32 页面 |
| 栈深度实测 | 未做 `-fstack-usage`/填充测试 | 建议对 ECAT 真实协议栈加入后测最坏栈深（当前 16 KB 栈） |
| Flash 擦写寿命/掉电安全 | 逻辑审计通过（单扇区、回读校验），无掉电测试 | 反复 param_save / EEPROM 模拟区写入，验证掉电时 magic+CRC 回退默认值 |
| 整机 EMC/功耗/热、ETG 一致性测试 | 不具备条件 | 硬件阶段补充 |

> 交付的回归测试脚本位于 `projects\ecat_drone_powertrain\tests\`（`test_host_proto.py`、`test_servo_frames.py`、`dshot_sim.py`，用法见 `tests/README.md`）；上述“未验证项”均需在实物板上执行，不能由现有自动化测试覆盖。

---

## 10. 附：验证脚本与证据文件

| 文件 | 说明 |
|---|---|
| `projects\ecat_drone_powertrain\tests\test_host_proto.py` | 交付的 host 协议回归测试：**238/238 OK** |
| `projects\ecat_drone_powertrain\tests\test_servo_frames.py` | 交付的舵机帧测试：**58/58 passed** |
| `projects\ecat_drone_powertrain\tests\dshot_sim.py` | 交付的 DShot 收发算法仿真：**ALL PASSED** |
| `projects\ecat_drone_powertrain\tests\README.md` | 测试脚本使用说明 |
| `.work\build_verify\build_flash_xip.log` | 全量构建日志（0 warning，内存表） |
| `.work\build_verify\contract_check.c` | 编译期契约检查源（`_Static_assert`） |
| `.work\build_verify\contract_check.obj` | 目标工具链编译产物（`nm -S` 提取大小） |
| `.work\build_verify\check_coe_types.py` | CoE 60 对象类型 ↔ `src/param.c` 交叉核对脚本 |
| `sdk_env_v1.13.0\user_template\tools\ecat_drone_host\build\selftest.log` | 上位机自检 43/43 通过报告 |

**Git 状态：未做任何 commit（按任务要求）。**
