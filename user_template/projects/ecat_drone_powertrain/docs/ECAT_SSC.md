# EtherCAT 应用层（SSC 集成）说明 — ecat_drone_powertrain

本文档说明本工程 EtherCAT 从站的软件结构：应用层文件、过程数据契约、AL 状态机、
看门狗、CoE 参数对象、与 Beckhoff SSC 协议栈的集成方式，以及当前“无协议栈
（StubSrc）”构建的行为与验证方法。

---

## 1. 软件分层与文件

```
                    ┌──────────────────────────────────────────────────────────┐
   EtherCAT 主站 ──▶│ ESC0 + 片内 MII PHY0/PHY1（port0/port1，2 端口）           │
                    │   ↓ PDI/片上总线                                           │
                    │ SSC 协议栈（SSC/Src，由 SSC Tool 生成；无源码时用 StubSrc） │
                    │   · MainInit / MainLoop / PDI_Isr / Sync0/1_Isr            │
                    │   · 邮箱 + CoE/SDO + SM/FMMU + DC + 看门狗                  │
                    │   ↓ APPL_InputMapping / APPL_OutputMapping（22 B / 48 B）  │
                    │ src/ecat_app.c（手写应用层）                                │
                    │   · 过程映像 g_ecat_rxpdo / g_ecat_txpdo                    │
                    │   · AL 状态机回调、链路状态、看门狗、LED 钩子               │
                    │   · CoE 参数对象钩子 0x2000+n → param_get/param_set         │
                    └───────────────┬──────────────────────────▲───────────────┘
                                    │ RxPDO 22 B               │ TxPDO 48 B
                                    ▼                          │
                    src/app_task.c（1 kHz 控制任务，Lead 维护）    │
                    · 控制字/油门/舵机角度 → dshot/servo          │
                    · 遥测（eRPM、舵机、ADC、状态）→ g_ecat_txpdo ┘
```

| 文件 | 职责 |
|---|---|
| `ecat.c`（工程根目录） | SSC 启动胶水：`ecat_ssc_init()`（`MainInit()` + EEPROM 回调 + `APPL_GenerateMapping()` + `bRunApplication=TRUE`）、`ecat_ssc_loop()`、`ecat_ssc_stop()`；与 ecat_io 的 `main()` 顺序一致，只是拆成函数供裸机超级循环调用 |
| `ecat_config.h` | 工程级 ESC 配置：Flash EEPROM 布局、**SSC 1 ms 定时器 = GPTMR2 ch0**、ESC 复位策略、stub 测试模式开关、`ecat_ssc_*()` 原型、DC 参数 getter 原型 |
| `user_config.h` | `eeprom_emulation` 组件的配置（与 ecat_io 相同） |
| `port/hpm_ecat_hw.c/.h` | ESC HAL（见 `docs/ECAT_HAL_PORT.md`） |
| `port/hpm_ecat_phy.c/.h` | 片内 MII PHY 复位/配置/MDIO 诊断 |
| `port/hpm_ecat_e2p_emulation.c/.h` | Flash 模拟 EEPROM |
| `SSC/StubSrc/` | 无协议栈时的 SSC API 桩（编译/链接/冒烟测试） |
| `SSC/Config/HPM_ECAT_DRONE_Config.xml` | SSC Tool Configuration（协议栈设置） |
| `SSC/ecat_drone.xlsx` / `.csv` | SSC Tool Application（对象字典/PDO 变量） |
| `SSC/ESI/HPM_ECAT_DRONE.xml` | TwinCAT 用 ESI（`Physics="YY"`，22/48 B PDO） |
| `src/ecat_app.c` | **手写 SSC 应用层**（详见下文） |

---

## 2. 过程数据契约（必须与 inc/ecat_pdo.h 完全一致）

### 2.1 RxPDO — 主站 → 从站，对象 0x1600，22 字节

| 偏移(B) | 类型 | 字段 | 说明 |
|---|---|---|---|
| 0 | u16 | `control_word` | bit0 RUN、bit1 清故障、bit2 急停（0=停止）、bit3 舵机输出使能、bit4 DShot 3D 请求 |
| 2 | u16 | `esc_cmd_us[0]` | ESC1 油门 1000..2000 µs |
| 4 | u16 | `esc_cmd_us[1]` | ESC2 油门 1000..2000 µs |
| 6 | i16 | `servo_angle_d10[0]` | 舵机1目标角，0.1° |
| 8 | i16 | `servo_angle_d10[1]` | 舵机2目标角，0.1° |
| 10 | u16 | `servo_ctrl_word` | bit0/1 舵机使能、bit2 清故障 |
| 12 | u16 | `dshot_cmd[0]` | ESC1 原始 DShot 命令（0=无） |
| 14 | u16 | `dshot_cmd[1]` | ESC2 原始 DShot 命令（0=无） |
| 16 | u16 | `reserved0` | 保留 |
| 18 | u32 | `cycle_counter` | 主站写计数（通信看门狗调试） |

对应对象：0x7000 … 0x7009（xlsx/ESI 中 `rx` 变量，PDO 映射顺序 = 索引递增）。
xlsx 中 PDO 映射对象 0x1600 与 SM2 分配对象 0x1C12 由 SSC Tool 自动生成。

### 2.2 TxPDO — 从站 → 主站，对象 0x1A00，48 字节

| 偏移(B) | 类型 | 字段 | 说明 |
|---|---|---|---|
| 0 | u16 | `status_word` | bit0 OP、bit1 输出使能、bit2 故障、bit3/4 ESC1/2 遥测 OK、bit5/6 舵机在线、bit7 ADC 告警、bit8 LED 测试 |
| 2 | u16 | `fault_code` | 故障位域（见 `inc/ecat_pdo.h` `app_fault_t`） |
| 4 / 8 | u32 ×2 | `esc_erpm[2]` | 双向 DShot 电转速 |
| 12 / 14 | u16 ×2 | `esc_tlm_period_us[2]` | 原始遥测周期（0=无遥测） |
| 16 / 18 | i16 ×2 | `servo_pos_d10[2]` | 舵机实际角度，0.1° |
| 20 / 22 | i16 ×2 | `servo_speed_rpm[2]` | 舵机转速 rpm |
| 24 / 26 | i16 ×2 | `servo_current_ma[2]` | 舵机电流 mA |
| 28 / 29 | u8 ×2 | `servo_temp_c[2]` | 舵机温度 °C |
| 30 / 31 | u8 ×2 | `servo_error[2]` | 舵机错误/状态字节 |
| 32 | u16 | `sys_voltage_dv` | 系统电压 0.1 V |
| 34 | i16 | `sys_current_ca` | 系统电流 0.01 A |
| 36 / 38 | u16 ×2 | `esc_current_ma[2]` | ESC1/2 电流 mA |
| 40 | u16 | `loop_counter` | 1 kHz 控制循环计数 |
| 42 | u16 | `led_status` | RGB 灯状态字 |
| 44 | u16 | `ecat_link_status` | bit0 port0 link、bit1 port1 link、bit4..7 AL 状态 |
| 46 | u16 | `reserved0` | 保留 |

对应对象：0x6000 … 0x6017（xlsx/ESI 中 `tx` 变量）。TxPDO 映射对象 0x1A00 与
SM3 分配对象 0x1C13 由 SSC Tool 自动生成。

> **PDO 拷贝**：`APPL_InputMapping()` 把 `g_ecat_txpdo` memcpy 到 ESC 过程数据区，
> `APPL_OutputMapping()` 把 ESC 过程数据区 memcpy 到 `g_ecat_rxpdo`。两侧都是
> 小端、packed 结构体，长度用 `nPdInputSize/nPdOutputSize`（由
> `APPL_GenerateMapping()` 返回固定值 48/22），并做了 `APP_ECAT_*PDO_SIZE` 上限保护。

### 2.3 映射对象的 ESI/xlsx 编码约定

PDO 映射条目的 32 位值（ESI `<DefaultData>`、xlsx 自动生成的映射对象）为：

```
bit[7:0]   = 数据位宽（bit）
bit[15:8]  = 子索引（本工程全部为 0）
bit[31:16] = 对象索引
```

例：`0x7000`（u16）→ `0x70000010` → ESI 小端 hex `10000070`；
`0x7009`（u32）→ `0x70090020` → `20000970`。生成脚本
`SSC/tools/gen_ssc.py` 的 `pdo_mapping_entries()` 按此规则生成。

---

## 3. AL 状态机、输出使能与看门狗

### 3.1 回调与 AL 状态

`inc/ecat_app.h` 定义的 AL 状态枚举：`INIT=1, PREOP=2, BOOT=3, SAFEOP=4, OP=8`。
协议栈在状态迁移时调用应用回调，`src/ecat_app.c` 中的处理：

| SSC 回调 | AL 迁移 | 应用处理 |
|---|---|---|
| `APPL_StartMailboxHandler()` | INIT → PREOP | 记录 PREOP；检查 CoE 使能参数 |
| `APPL_StartInputHandler(pIntMask)` | PREOP → SAFEOP | 记录 SAFEOP（TxPDO 开始发送） |
| `APPL_StartOutputHandler()` | SAFEOP → OP | 记录 OP，`g_ecat_op_active=1`，允许输出 |
| `APPL_StopOutputHandler()` | OP → SAFEOP | 立即安全状态：`g_ecat_op_active=0`、清 `g_ecat_rxpdo.control_word` |
| `APPL_StopInputHandler()` | SAFEOP → PREOP | 记录 PREOP |
| `APPL_StopMailboxHandler()` | PREOP → INIT | 记录 INIT |
| `APPL_AckErrorInd()` | 主站确认错误 | 清 ERR LED 状态、打印日志 |

另外 `ecat_app_task()` 每个主循环**交叉校验** ESC 的 AL Status 寄存器
（`HPM_ESC->AL_STAT` 的 `AS` 位）：当寄存器报告 PREOP/SAFEOP/OP（说明真实协议栈/
主站已配置 ESC）时以寄存器为准；否则以回调状态为准（Stub 仿真模式必需）。
对外接口：

- `ecat_app_get_al_state()`：当前 AL 状态（偏移已同步到 `g_app_state.ecat_al_state`）；
- `g_ecat_op_active`：OP 且过程数据有效时为 1（`inc/ecat_pdo.h` 契约）；
- `ecat_app_outputs_enabled()`：返回 `g_ecat_op_active`；
- 链路状态：`ecat_app_get_link_status()` 读 `HPM_ESC->ESC_DL_STAT` 的
  `LP0`（bit8）/`LP1`（bit10）。

> 应用层的安全链：`g_ecat_op_active`、控制字 RUN、故障位、主站写看门狗共同决定
> `app_task.c` 里的 `g_app_state.output_enabled`。离开 OP 时 `APPL_StopOutputHandler()`
> 还会清控制字，确保下一轮不会带着旧命令恢复输出。

### 3.2 看门狗

两级：

1. **ESC 硬件看门狗**：协议栈使能的 SM 看门狗，状态位在
   `HPM_ESC->WDG_STAT_PDAT.ST`；`ecat_app_watchdog_expired()` 直接读取；
2. **软件看门狗**：`APPL_OutputMapping()` 记录最近一次收到主站输出的时间
   （`g_app_tick_ms`），超过 `PARAM_ECAT_WATCHDOG_MS`（默认 100 ms，参数可调）。
   `ecat_app_task()` 在 SAFEOP/OP 下检测并在串口打印“watchdog”告警/恢复日志，
   同时累加 `g_app_state.ecat_cycle_miss`。

控制任务（`app_task.c`）另有基于 RxPDO `cycle_counter` 的通信检测，超时置
`APP_FAULT_ECAT_OP_TIMEOUT` 并封锁输出——两层互不替代。

### 3.3 参数应用（station alias / DC / 同步周期 / 看门狗）

`ecat_app_apply_params()` 从参数系统读取：

| 参数 | 处理 |
|---|---|
| `PARAM_ECAT_NODE_ALIAS` | 写 ESC `STATION_ALS`（配置站点别名） |
| `PARAM_ECAT_DC_MODE` | 0=关闭；非 0 时写 ESC `SYNC0_CYC_TIME`/`SYNC1_CYC_TIME` 作为从站侧 DC 默认值 |
| `PARAM_ECAT_SYNC0/1_PERIOD_US` | 写入上述同步周期寄存器（ns） |
| `PARAM_ECAT_DC_SHIFT_NS` | 缓存，供协议栈 DC 调优（`ecat_app_get_dc_shift_ns()`） |
| `PARAM_ECAT_WATCHDOG_MS` | 软件看门狗阈值 |
| `PARAM_ECAT_ENABLE_COE` | Log 提示（真实协议栈 CoE 由 SSC 配置固定开启） |

> **注意**：DC 与同步周期最终由 EtherCAT 主站在 INIT→PREOP→SAFEOP 过程中配置，
> 会覆盖从站默认值；这里的参数仅是“从站默认值”，主站永远有最终决定权。
> `PARAM_ECAT_SDO_TIMEOUT_MS` 为协议栈预留参数（SSC 的 SDO 超时在生成代码里配置）。

---

## 4. CoE 制造商参数对象（0x2000 … 0x203B）

### 4.1 对象映射规则

- 对象索引 = **`0x2000 + param_id`**，`param_id` 即 `inc/param.h` 中
  `param_id_t` 枚举的顺序号（0 起）；子索引固定 0；
- 对象类型取 **`src/param.c` 的 `s_param_desc[]` 描述符表**（`param_desc_t.type`，
  权威来源）：u8→`USINT`（8 bit）、u16→`UINT`（16）、u32→`UDINT`（32）、
  i16→`INT`（16）、i32→`DINT`（32）；
  > 注意：`inc/param.h` 中数组参数的第二个元素（如 `PARAM_SERVO_ANGLE_MIN2`）
  > 没有类型注释，不能只靠 param.h 判断类型；`SSC/tools/gen_ssc.py` 已改为从
  > `src/param.c` 读取类型，并在生成后自检 ESI/CSV/xlsx 与之一致。
- 当前共 **60** 个参数 → 对象 **0x2000 … 0x203B**（参数增删后必须重跑
  `SSC/tools/gen_ssc.py` 并重新生成协议栈，见 `SSC/README.md` 第 6 节）；
- 读写通过 `param_get()/param_set()`，即**主站 SDO 与 PC 上位机共享同一参数存储**：

| `param.h` 分组 | 对象范围 |
|---|---|
| ECAT（9 个） | 0x2000 … 0x2008（node alias、DC mode、SYNC 周期、看门狗…） |
| DShot（11 个） | 0x2009 … 0x2013 |
| 舵机（17 个） | 0x2014 … 0x2024 |
| ADC（12 个） | 0x2025 … 0x2030 |
| LED（2 个） | 0x2031 … 0x2032 |
| USB（2 个） | 0x2033 … 0x2034 |
| AM32（3 个） | 0x2035 … 0x2037 |
| 舵机 KP/KD（4 个） | 0x2038 … 0x203B |

（完整逐项列表见 `SSC/ecat_drone_application.csv` 的前 60 行。）

### 4.2 实现方式

1. `SSC/ecat_drone.xlsx` 中每个参数对象一行，`CoeRead`/`CoeWrite` 列填
   `ecat_coe_obj_read` / `ecat_coe_obj_write` → SSC Tool 生成的应用对象字典会把
   这两个函数挂到对象的读写回调上（SSC 5.13 应用解析器的标准机制）；
2. `src/ecat_app.c` 提供：

```c
UINT8 ecat_coe_obj_read (UINT16 index, UINT8 subindex, UINT32 size,
                         UINT16 MBXMEM *pData, UINT8 bCompleteAccess);
UINT8 ecat_coe_obj_write(UINT16 index, UINT8 subindex, UINT32 size,
                         UINT16 MBXMEM *pData, UINT8 bCompleteAccess);
```

  - 只支持子索引 0、**不支持** Complete Access（返回 `ABORTIDX_UNSUPPORTED_ACCESS`）；
  - 请求字节数必须等于参数类型宽度，否则返回 `ABORTIDX_DATA_TYPE_MISMATCH`；
  - 写只读参数 → `ABORTIDX_UNSUPPORTED_ACCESS`；越界 → `ABORTIDX_VALUE_EXCEEDED`；
  - 写入影响 EtherCAT 的参数（alias/DC/看门狗）后立即调用
    `ecat_app_apply_params()` 生效；
3. 未使用协议栈（StubSrc）时没有 CoE/SDO 通路，这两个钩子不会被调用，但会参与
   编译/链接（签名与真实协议栈一致；`nm src/ecat_app.c.obj` 可见
   `T ecat_coe_obj_read/ecat_coe_obj_write`）。stub 构建中链接器
   `--gc-sections` 会把无人引用的它们从最终 ELF 中回收，真实协议栈构建时生成的
   对象字典会引用它们，因此会保留在固件中。

> `APPL_GetObjectDictionary` 不在本工程中使用：应用对象字典由 SSC Tool 从 xlsx
> 生成（`ecat_drone.h`），本工程不再手写第二份对象表，避免两处维护不一致。

---

## 5. 与真实 SSC 协议栈的集成（摘要）

完整客户操作步骤见 **`SSC/README.md` 第 3 节**。关键点：

1. 用 SSC Tool 从 `SSC/Config/HPM_ECAT_DRONE_Config.xml` + `SSC/ecat_drone.xlsx`
   生成协议栈（`SSC/Src/`）和 ESI；
2. **只拷协议栈源文件 + 生成的应用头 `ecat_drone.h`** 到 `SSC/Src/`；
   **不要拷生成的 `ecat_drone.c`**（它和手写的 `src/ecat_app.c` 会重复定义
   `APPL_*` 回调）；
3. CMake 自动切换到真实协议栈（`APP_ECAT_SSC_REAL_STACK=1`，StubSrc 不再编译）；
4. 本工程 `src/ecat_app.c` 实现的回调（SSC 5.13 应用接口）：

```
APPL_AckErrorInd(UINT16 stateTrans)
APPL_StartMailboxHandler / APPL_StopMailboxHandler
APPL_StartInputHandler(UINT16 *pIntMask) / APPL_StopInputHandler
APPL_StartOutputHandler / APPL_StopOutputHandler
APPL_GenerateMapping(UINT16 *pInputSize, UINT16 *pOutputSize)
APPL_InputMapping(UINT16 *pData) / APPL_OutputMapping(UINT16 *pData)
APPL_Application(void)
APPL_GetDeviceID(void)            /* EXPLICIT_DEVICE_ID=1，返回 5 */
ecat_coe_obj_read / ecat_coe_obj_write
```

若生成的协议栈还引用其它应用钩子（不同 SSC 版本可能有差异），按编译器提示补齐
即可；本工程已覆盖 ecat_io 示例应用文件（`application/digital_io.c`）的全部回调。

5. 协议栈的 EEPROM 回调在 `ecat.c` 中绑定：
   `pAPPL_EEPROM_Read/Write/Reload = ecat_eeprom_emulation_*`，
   `pAPPL_EEPROM_Store = ecat_app_eeprom_store`（flush Flash 写入）。

---

## 6. StubSrc（无协议栈）构建的行为

SSC 源码受许可限制不随 SDK 发布，因此默认构建使用 `SSC/StubSrc/ssc_stub.c` +
6 个桩头文件。它提供与真实协议栈**同名同签名**的最小 API：

- 全局量：`bRunApplication`、`nPdInputSize`、`nPdOutputSize`、`EepromReadSize`、
  `pAPPL_EEPROM_*`；
- 入口：`MainInit()`、`MainLoop()`、`PDI_Isr()`、`Sync0_Isr()`、`Sync1_Isr()`、
  `ECAT_CheckTimer()`；
- 行为：
  - `MainInit()` 置 `EepromReadSize=8`、AL=INIT、清影子缓冲区，打印
    “SSC API stub active”；
  - `MainLoop()` 在普通模式下只从 ESC 读 AL 状态（无主站时为 INIT），**不驱动**
    `g_ecat_op_active`；当 `ECAT_SSC_STUB_TEST_MODE=1` 时以 500 ms 周期仿真
    `INIT→PREOP→SAFEOP→OP→SAFEOP→PREOP→…`，调用对应 `APPL_Start*/Stop*` 回调，
    并调用 `APPL_InputMapping/OutputMapping` 走影子缓冲区（不连网络）；
  - **无邮箱、无 SDO、无 PDO 网络交换、无 DC**——不能与主站通信。

`ECAT_SSC_STUB_TEST_MODE` 的用途：无主站台架验证 `app_task.c` 状态机与输出使能
路径。默认 0（安全）；启用时请断开电机/ESC 供电。

---

## 7. 验证清单

### 7.1 编译期（本仓库已通过）

```powershell
python <sdk>\user_template\scripts\check_syntax.py `
    --build-dir D:\Ecat_Drone_Vector_Powertrain\.work\build_dev `
    ecat.c src/ecat_app.c SSC/StubSrc/ssc_stub.c `
    port/hpm_ecat_hw.c port/hpm_ecat_phy.c port/hpm_ecat_e2p_emulation.c
# -> All 6 file(s) OK（-Wall 无告警）
```

### 7.2 运行时（无协议栈，Stub 构建）

1. 烧录后 UART0 日志应出现：
   `SSC API stub active`、`EEPROM loading ...`、`PHY portN: MDIO address X`、
   `process data: TxPDO 48 bytes, RxPDO 22 bytes`、`AL state now 1`；
2. 使能 `ECAT_SSC_STUB_TEST_MODE=1` 重新编译：日志应周期性出现
   `AL state -> 2/4/8`，`g_app_state.state` 在 ECAT_SAFEOP/ECAT_OP_IDLE 之间切换，
   TxPDO `status_word` 的 OP 位随状态变化（可用上位机/USB 协议观察）；
3. 断言 `g_ecat_rxpdo`/`g_ecat_txpdo` 大小 = 22/48（`inc/ecat_pdo.h` 的编译期
   size check 已保证）。

### 7.3 运行时（真实协议栈 + TwinCAT）

见 `SSC/README.md` 第 5 节；重点关注：

- 扫描/加载 ESI 后：RxPDO 22 B（10 项）、TxPDO 48 B（24 项）；
- 进 OP 后 TxPDO `loop_counter` 1 kHz 递增、`ecat_link_status` link 位正确；
- CoE 在线读写 0x2000…0x203B 与上位机参数一致（例如 0x2000 = node alias）；
- 拔网线/停主站：看门狗触发、输出封锁、日志告警。

---

## 8. 已知风险 / 待确认项

| # | 风险 | 说明/缓解 |
|---|---|---|
| 1 | 真实 SSC 生成代码与手写应用层的兼容性 | 生成代码无法在本环境验证（许可限制）；按 `SSC/README.md` 步骤集成后编译验证，缺失回调按第 5 节清单补齐 |
| 2 | xlsx 由脚本重写（无 openpyxl） | 结构与 hpm_sdk 的 `digital_io.xlsx` 相同（workbook/theme/styles 复用，仅 sheet/sharedStrings 重写）；若 SSC Tool 版本升级导致导入失败，用 `ecat_drone_application.csv` 手工录入（见 `SSC/README.md` 第 6 节） |
| 3 | PDO 映射对象由 SSC Tool 自动生成 | 依赖“0x6xxx/0x7xxx 变量按索引递增进入映射”的自动生成规则；生成后在 SSC Tool 中检查 0x1600/0x1A00 条目顺序，必须与 `inc/ecat_pdo.h` 一致 |
| 4 | 参数增删导致对象字典与固件不一致 | 对象索引 = 0x2000 + `param.h` 枚举顺序，**任何参数增删/换序都要重跑 `gen_ssc.py` + SSC Tool 重新生成** |
| 5 | CoE Complete Access 不支持 | 主站按子索引逐个访问；TwinCAT 默认逐项读写，无影响 |
| 6 | DC 参数为从站默认值 | 主站在启动时覆盖；如需从站强制，需改 SSC 生成代码/主站配置 |
| 7 | Stub 构建无通信 | 默认构建无法做 EtherCAT 测试，只用于编译/应用层冒烟；正式测试必须生成协议栈 |
