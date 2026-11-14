# EtherCAT HAL / 硬件适配说明 — ecat_drone_5e3y (HPM5E3Y 片内 MII PHY)

本文档说明 `port/` 下的 EtherCAT 硬件抽象层（HAL）相对 hpm_sdk 示例
`hpm_sdk/samples/ethercat/port/` 做了哪些适配、为什么、以及上板时需要验证什么。

参考的 SDK 示例（代码来源，行号对 hpm_sdk v1.13.0）：

| 文件 | 示例路径 |
|---|---|
| ESC HAL | `hpm_sdk/samples/ethercat/port/hpm_ecat_hw.c/.h` |
| PHY | `hpm_sdk/samples/ethercat/port/hpm_ecat_phy.c/.h` |
| EEPROM 模拟 | `hpm_sdk/samples/ethercat/port/hpm_ecat_e2p_emulation.c/.h`（配合 `components/eeprom_emulation`） |
| 工程配置 | `hpm_sdk/samples/ethercat/ecat_io/ecat_config.h`、`user_config.h`、`ecat.c` |
| 参考板 | `hpm_sdk/boards/hpm5e00evk/`（`board_init_ethercat()`、`init_esc_pins()`） |

另参考（本板硬件事实，PDF 页码见 `.work/pdf_text/FACTS.md`）：

- `HPM5E00 系列微控制器片内 MII PHY 使用指南 v1.1`（PHY 章节）
- `HPM5E00 用户手册` 第 55 章（ESC）、`HPM5E00 数据手册` 表 2/3/4（引脚）

---

## 1. 本板 ESC 硬件连接（与 EVK 的差异）

| 项目 | HPM5E00EVK 示例 | ecat_drone_5e3y（本板） |
|---|---|---|
| ESC 端口 | 3 端口（P0 外部 PHY + P1 外部 PHY + P2 扩展板） | **2 端口**：P0 = 片内 PHY0，P1 = 片内 PHY1 |
| PHY | 外部 JL1111（MDIO 管理） | **片内合封 2× 百兆 MII PHY**（HPM5E3Y SIP） |
| P0 MII 引脚 | 外部引脚 PA16–PA29 | PV00–PV15（封装内部，未引出） |
| P1 MII 引脚 | 外部引脚 PF02–PF14 | PW00–PW21（封装内部，未引出） |
| PHY 复位 | GPIOB24（一个管脚复用） | **PV12 = PHY0 resetn，PW12 = PHY1 resetn**（低有效） |
| MDIO / MDC | PA30 / PA31 | PA30 / PA31（不变，需外部上拉） |
| REFCK 25 MHz | PW20/PW21 | PW20/PW21（不变，25 MHz = ESC 100 MHz / 4） |
| NMII link 输入 | PA25/PA28 → CTR 索引 2/5 | **PA28 = ESC0_CTR_1 ← PHY0 LED1（port0 link），PA25 = ESC0_CTR_0 ← PHY1 LED1（port1 link）** → CTR 索引 1 / 0 |
| EEPROM | Flash 模拟（0x80000） | Flash 模拟（0x80000）——本板没有外挂 EEPROM，UM 写明 ESC 默认模拟 EEPROM 模式 |
| RUN/ERR LED | 无（示例未接） | 无独立 LED；状态由 RGB LED（`app_task.c`）显示，同时写入 ESC LED override 寄存器 |

对应 `boards/ecat_drone_5e3y/board.h` 的宏（**这些值不要在本目录里硬编码**）：

```c
BOARD_ECAT_SUPPORT_PORT1            (1)         /* 2 端口 */
BOARD_ECAT_SUPPORT_PORT2            (0)
BOARD_ECAT_NMII_LINK0_CTRL_INDEX    (1)         /* PHY0 LED1 = PA28 = CTR_1 */
BOARD_ECAT_NMII_LINK1_CTRL_INDEX    (0)         /* PHY1 LED1 = PA25 = CTR_0 */
BOARD_ECAT_PORT0_LINK_INVERT        false
BOARD_ECAT_PORT1_LINK_INVERT        false
BOARD_ECAT_PHY0_RESET_GPIO          GPIO_V12    /* PV12 */
BOARD_ECAT_PHY1_RESET_GPIO          GPIO_W12    /* PW12 */
BOARD_ECAT_PHY_RESET_LEVEL          (0)         /* 低电平复位 */
BOARD_ECAT_PHY_RESET_HOLD_TIME_MS   (10)
BOARD_ECAT_PHY_RESET_WAIT_TIME_MS   (30)
BOARD_ECAT_PHY_ADDR_OFFSET          (1)
BOARD_ECAT_PORT0_PHY_ADDR           (0)         /* 实际 MDIO 地址 = 1 */
BOARD_ECAT_PORT1_PHY_ADDR           (1)         /* 实际 MDIO 地址 = 2 */
BOARD_ECAT_FLASH_EMULATE_EEPROM_ADDR (0x80000)
```

> 注：板级初始化（ESC 时钟、MII/MDIO 引脚、PHY 复位 GPIO 方向、NMII link 引脚）
> 由板级包 `board_init_ethercat()` 完成；`ecat_app_init()` 先调它，再调
> `ecat_hardware_init()`（顺序与 ecat_io 示例的 `board_init_ethercat()` →
> `ecat_hardware_init()` 一致）。

---

## 2. 定时器 / 中断资源分配（重要）

`port/hpm_ecat_hw.c` 里 SSC 的 1 ms 定时器**没有**沿用示例的 GPTMR0 ch0，原因：

| 定时器/通道 | 归属 | 用途 |
|---|---|---|
| GPTMR0 ch0 | 板级包 | `board_delay_us()/board_delay_ms()` |
| GPTMR0 ch1 | 板级包 | 1 ms 软件定时器（`board_timer_create` → `g_app_tick_ms`） |
| GPTMR1 ch2 | DShot 模块 | ESC1 双向 DShot 遥测捕获 |
| GPTMR1 ch3 | DShot 模块 | ESC2 双向 DShot 遥测捕获 |
| **GPTMR2 ch0** | **EtherCAT HAL** | **SSC 1 ms tick（`HW_GetTimer()` / `ECAT_CheckTimer()`）** |

配置在 `ecat_config.h`：

```c
#define ECAT_TIMER_GPTMR        HPM_GPTMR2
#define ECAT_TIMER_GPTMR_CH     (0U)
#define ECAT_TIMER_GPTMR_CLK    (clock_gptmr2)
#define ECAT_TIMER_GPTRM_IRQ    IRQn_GPTMR2
```

中断与优先级（`ENABLE_ESC_INT()`，与示例相同）：

| 中断 | IRQn | 优先级 | 处理 |
|---|---|---|---|
| ESC SYNC0 | `IRQn_ESC_SYNC0` | 3 | 读 0x098E 应答 → `Sync0_Isr()` |
| ESC SYNC1 | `IRQn_ESC_SYNC1` | 3 | 读 0x098F 应答 → `Sync1_Isr()` |
| ESC PDI | `IRQn_ESC` | 4 | `PDI_Isr()` |
| ESC RESET | `IRQn_ESC_RESET` | 2 | PHY 保持复位 + MCU 软复位 |
| GPTMR2 ch0 | `IRQn_GPTMR2` | 3 | 1 ms tick → `ECAT_CheckTimer()` |

> 1 ms tick 优先级 ≥ SYNC0/SYNC1 的原因（示例注释）：避免 `DC_CheckWatchdog()`
> 被 `Sync0_Isr()/Sync1_Isr()` 打断。

中断服务函数通过 `SDK_DECLARE_EXT_ISR_M()` 定义在 `port/hpm_ecat_hw.c`，调用
SSC（或 StubSrc）的 `PDI_Isr/Sync0_Isr/Sync1_Isr/ECAT_CheckTimer`。

ESC 复位请求的处理（`ECAT_RESET_ESC_WITH_MCU=1`，`ecat_config.h`）：ESC 收到总线
复位命令时先 `ecat_phy_keep_reset()`，再 `ppor_sw_reset(HPM_PPOR, 2)` 软复位整颗
MCU（最安全）。若要只复位 ESC 外设，可改 `ECAT_RESET_ESC_PERIPHERAL=1`（不推荐，
示例也默认复位 MCU）。

---

## 3. 片内 MII PHY 的软件处理

### 3.1 结论：不需要额外 PHY 寄存器配置

片内 MII PHY 使用指南只在“新建 SIP board”章节要求改 `init_esc_pins()`、PHY 复位
宏、NMII link 宏、`BOARD_ECAT_SUPPORT_PORT2`，**没有要求修改 `hpm_ecat_phy.c`**。
据此判断片内 PHY 与 EVK 外部 JL1111 为同一 JLSemi PHY die，因此
`ecat_phy_config()` 的 JL1111 Page-7 LED 模式 + MDIO broadcast filter 配置逻辑
**原样保留**（与本板 HAL 的差异只在复位管脚/电平/时间宏）。

### 3.2 复位时序

- 低电平复位：`PV12`（PHY0）、`PW12`（PHY1）；
- 复位保持 `BOARD_ECAT_PHY_RESET_HOLD_TIME_MS = 10 ms`（数据手册要求 ≥ 200 ns）；
- 释放后等待 `BOARD_ECAT_PHY_RESET_WAIT_TIME_MS = 30 ms` 再做 MDIO 访问
  （数据手册上电要求 ≥ 5 ms）；
- `ecat_phy_keep_reset()` 用于 ESC/MCU 复位前把 PHY 保持在复位态。

### 3.3 MDIO 地址发现（容错）

片内 PHY 的 MDIO 地址由 **PHY LED0/LED1 管脚在复位时的上下拉**决定：

- PHY0：`PA29 = 地址 bit0`、`PA28 = 地址 bit1`；
- PHY1：`PA26 = 地址 bit0`、`PA25 = 地址 bit1`；
- 这两个管脚在运行时又是 ESC0_CTR（link 输入）/ PHY LED 输出，因此 strapping
  不能用 GPIO 读回，本板原理图也未标注上下拉。

`port/hpm_ecat_phy.c` 因此实现：

1. `ecat_phy_find_mdio_addr(port)`：先试 `BOARD_ECAT_PHY_ADDR_OFFSET + PORTn_PHY_ADDR`
   （board.h 注释的“实际地址”，即 1/2），再试 ecat_io 示例用的 `PORTn_PHY_ADDR`
   （0/1），最后扫描 MDIO 地址 0..7；用 PHY ID1（寄存器 2）判断“有 PHY 应答”
   （0x0000/0xFFFF 视为无效）。
2. 找到后，仅对**应答**的地址做 JL1111 LED/broadcast 配置并打印实际地址；
3. **任何 MDIO 失败都不会让 `ecat_hardware_init()` 失败**：MDIO 只是管理通道，
   ESC↔PHY 的 MII 数据通路是独立连接，PHY 默认自协商即可建链。
4. `ecat_phy_mdio_probe()`（`ecat_app_init()` 调用）扫描 0..7 并打印
   `ID1/ID2/BMSR/link/autoneg_done`，用于上板确认 strapping。

> 上板验证方法：上电看 UART0 日志中 `[ECAT] MDIO scan` 与
> `[ECAT] PHY portN: MDIO address X`。如果期望地址与实际不符，把实际值写进
> `board.h` 的 `BOARD_ECAT_PORTn_PHY_ADDR` / `BOARD_ECAT_PHY_ADDR_OFFSET` 即可；
> 若要完全绕过 MDIO 配置，也可以不修改代码（配置失败已容错）。

### 3.4 REFCK / ESC 时钟

- ESC 需要 100 MHz 功能时钟（`ESC.GPR_CFG0[CLK100_EN]`）与 25 MHz PHY REFCK
  （`ESC.PHY_CFG1[REFCK_25M_OE]`，25 MHz = 100 MHz / 4）；
- `ecat_hardware_init()` 中 `esc_config.core_clock_en = true`、
  `phy_refclk_en = true`，由 `esc_config_eeprom_and_clock()` 设置；
- ESC 100 MHz 时钟源由板级包 `init_esc0_clock()`（`board_init_ethercat()`
  调用）提供，需保证板级时钟预设让 ESC 时钟 = 100 MHz（本板 clock.c 与 EVK 一致）。

### 3.5 未使用的 PHY / 端口

ESC 硬件有 3 个端口，本板只用 port0/port1（`BOARD_ECAT_SUPPORT_PORT2=0`）。
按片内 PHY 指南：未使用 PHY 的端口电路要完整保留（地址 strapping + RBIAS），且
其管脚不可复用为数字功能；本板两个 PHY 均使用，此条仅作说明。`hpm_ecat_hw.c` 中
port2 相关的 `esc_config_ctrl_signal_function()` / `esc_config_nmii_link_source()`
参数按 `BOARD_ECAT_SUPPORT_PORT2=0` 编译。

---

## 4. EEPROM 模拟（内部 Flash）

| 项目 | 值 | 位置 |
|---|---|---|
| Flash 基址 | `0x80000000`（1 MB 片内 XPI NOR，P25Q80SU） | `board.h` `BOARD_FLASH_BASE_ADDRESS` |
| EEPROM 模拟区 | `0x80000` 起 64 KB（16 个 4 KB 扇区做磨损均衡） | `BOARD_ECAT_FLASH_EMULATE_EEPROM_ADDR` |
| EEPROM 镜像大小 | 2 KB（`ESC_EEPROM_SIZE=0x800`） | `ecat_config.h` / SSC 配置 |
| 参数区 | `0x7E000` 起 4 KB（与 EEPROM 区不重叠） | `inc/param.h` |
| 组件 | `hpm_sdk/components/eeprom_emulation`（`e2p_t` + `nor_flash_*`） | CMake `CONFIG_EEPROM_EMULATION=1` |

流程（`port/hpm_ecat_e2p_emulation.c`，与示例一致）：

1. `ecat_flash_eeprom_init()`：初始化 NOR 与 e2p 组件；若工程里有 SSC Tool 生成的
   `eeprom.h`（`aEepromData` 非弱符号），检查 EEPROM 配置区 checksum、Product Code、
   Revision Number，必要时自动写入新内容（升级固件后无需 TwinCAT 手动更新 EEPROM）；
2. ESC 的 EEPROM 读命令 → `ecat_eeprom_emulation_read()` → e2p_read；
3. EEPROM 写命令 → `ecat_eeprom_emulation_write()`（先比对，内容相同不写，减少磨损）；
4. EEPROM reload 命令 → `ecat_eeprom_emulation_reload()`（重算配置区并写回 ESC 寄存器）；
5. 无协议栈时 `aEepromData` 是 `hpm_ecat_hw.c` 里的弱符号（`{0xa5,...}`），
   EEPROM 加载会报 checksum 错误但通信仍可进行，属正常现象（见第 7 节）。

> 注：本板没有外挂 EEPROM，所以示例中“外部 I2C EEPROM”分支已从
> `hpm_ecat_hw.c` 删除，并加了编译期检查（`ESC_EEPROM_EMULATION` 必须为 1）。

---

## 5. HAL 接口速查

`port/hpm_ecat_hw.h`（与示例相同的 SSC 硬件接口，供 SSC/StubSrc 使用）：

| 接口 | 说明 |
|---|---|
| `HW_Init()` / `HW_Release()` | 设置 `pEsc = HPM_ESC_BASE` / 空 |
| `HW_GetTimer()` / `HW_ClearTimer()` | GPTMR2 ch0 的 1 ms 计数 |
| `HW_EscRead*` / `HW_EscWrite*` / `Isr` 变体 | ESC 内存访问宏（HPM5E00 支持字节读，`HPM_IP_FEATURE_ESC_BYTE_READ=1`） |
| `HW_GetALEventRegister()` | 读 AL Event 寄存器（0x0220 低 16 位） |
| `HW_EepromReload()` | 保留接口（实际用 `pAPPL_EEPROM_Reload`） |
| `DISABLE_ESC_INT()` / `ENABLE_ESC_INT()` | 中断开关 |

扩展接口（HPM 适配新增）：

| 接口 | 说明 |
|---|---|
| `ecat_hardware_init(ESC_Type *)` | ESC 完整初始化：EEPROM 模拟、PHY 复位/配置、NMII link、ESC 中断、1 ms 定时器、`HW_Init` |
| `ecat_timer_ms_enable()` | 启动 GPTMR2 ch0 1 ms 中断 |
| `ecat_phy_reset()` / `ecat_phy_keep_reset()` / `ecat_phy_config()` / `ecat_phy_mdio_probe()` | PHY 复位/配置/诊断（见第 3 节） |
| `ecat_calculate_eeprom_config_data_checksum()` | EEPROM 配置区 CRC-8（多项式 0x07，初值 0xFF） |
| `ecat_flash_eeprom_*()` | Flash EEPROM 模拟读写/格式化/flush |

应用层（`src/ecat_app.c`）读取 ESC 状态时直接用 SDK 寄存器接口
（`HPM_ESC->AL_STAT`、`HPM_ESC->ESC_DL_STAT`、`HPM_ESC->WDG_STAT_PDAT`、
`HPM_ESC->STATION_ALS`、`HPM_ESC->SYNC0_CYC_TIME`），不依赖 SSC 的
`esc.h` 偏移宏，因此真实协议栈和 Stub 两种构建方式都能编译。

---

## 6. 上板验证清单（硬件）

1. **电源/时钟**：3.3 V、PHY REFCK 25 MHz（PW20/21）；ESC 100 MHz 时钟已在
   `board_init_ethercat()` 中使能。
2. **复位**：示波器/逻辑分析仪看 PV12/PW12 上电时序——低电平 ≥ 10 ms，释放后
   等待 ≥ 5 ms 再访问 MDIO（代码 30 ms）。PHY 复位失败表现为 MDIO 无应答 +
   无 link。
3. **MDIO**：UART0 日志 `[ECAT] MDIO scan` 应列出 1~2 个 PHY；记录实际地址。
   `BMSR.link=1` 表示网线已连接（另一头接主站/交换机）。
4. **Link / NMII**：插网线后 ESC `DL_STATUS.LP0/LP1`（bit8/bit10）应置位，
   应用 TxPDO `ecat_link_status.bit0/bit1` 对应置位；若不置位先检查 PA25/PA28
   的 ESC0_CTR 功能是否被 pinmux 配置、以及 `*_LINK_INVERT`。
5. **EEPROM**：日志应有 `EEPROM loading successful`；首次若报 checksum error 是
   正常的（无 SSC Tool 的 `eeprom.h` 时），用 TwinCAT 做一次 EEPROM Update 后
   重新上电。
6. **中断**：进 OP 后 ESC SYNC0 中断（若配置 DC）或 PDI 中断应有活动；用 GPIO
   翻转/调试器确认 `ecat_app_task()` 在主循环被高频调用。
7. **通信**：TwinCAT 扫描/进 OP 后，主站写 RxPDO control_word bit0、TxPDO
   loop_counter 应以 1 kHz 递增（见 `src/app_task.c`）。

---

## 7. 已知风险 / 待确认项

| # | 风险 | 影响 | 缓解 |
|---|---|---|---|
| 1 | 片内 PHY MDIO 地址 strapping 未知（PA25/26/28/29） | PHY LED/broadcast 配置可能写到错误地址 | 容错扫描 + 日志；不影响 MII 数据通路；量产后把实测地址固化到 board.h |
| 2 | NMII link 索引/极性按 PHY 指南推导（LINK0=CTR_1、LINK1=CTR_0） | 若反了主站看到的 link 状态错误 | 用 TxPDO `ecat_link_status`/日志确认，必要时改 `*_LINK_INVERT` 或索引 |
| 3 | 片内 PHY 与外部 JL1111 是否完全同 die 未 100% 证实 | 若不同，Page-7 LED/broadcast 写入可能无效（但不报错） | 配置为 best-effort；PHY 默认自协商仍可建链 |
| 4 | ESC REFCK/100 MHz 依赖板级 clock presets | 时钟不对则 ESC 完全不工作 | 用示波器量 PW20/PW21 25 MHz；核对 `clock.c` 与 EVK 一致 |
| 5 | EEPROM 模拟占用 Flash 0x80000–0x8FFFF | 固件镜像不能超过 0x80000（512 KB）；参数区在 0x7E000 | 链接脚本/分区表按此规划（当前应用 < 512 KB） |
| 6 | ESC 复位请求会复位整颗 MCU | 主站发起 ESC reset 时电机/舵机会掉电重启 | 属预期保护行为；如需仅复位 ESC 改 `ECAT_RESET_ESC_PERIPHERAL` |
