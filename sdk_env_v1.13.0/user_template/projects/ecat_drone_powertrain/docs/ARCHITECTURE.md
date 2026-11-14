# 固件架构说明 — ecat_drone_powertrain

## 1. 总体结构

```
                      ┌────────────────────────────────────────────────────────┐
   EtherCAT 主站 ────▶│ ESC0 + 片内 MII PHY0/1                                 │
   (TwinCAT 等)       │        │ PCIe? 无，PDI=片上总线                          │
                      │        ▼                                               │
                      │  SSC 从站协议栈（SSC/Src，需 SSC Tool 生成）            │
                      │        │ APPL_InputMapping/APPL_OutputMapping           │
                      │        ▼                                               │
                      │  src/ecat_app.c   ←→  g_ecat_rxpdo / g_ecat_txpdo       │
                      └────────┬───────────────────────────▲───────────────────┘
                               │ 22B RxPDO                 │ 48B TxPDO
                               ▼                           │
   ┌──────────────────────────────────────────────────────┴──────────────────┐
   │ src/app_task.c   1 kHz 控制任务                                          │
   │  · 解析控制字/油门/舵机角度     · 通信看门狗                              │
   │  · 保护逻辑（过压/欠压/过流/遥测丢失）                                    │
   │  · 填充遥测（eRPM、舵机状态、ADC、状态灯、链路）                          │
   └───┬──────────┬──────────────┬───────────────┬──────────────┬────────────┘
       ▼          ▼              ▼               ▼              ▼
  dshot_bidir  servo_s288    led_rgb         adc_mon        param / usb_cdc
  PWM0+GPTMR1  UART2/3       PWM1            ADC0/ADC1      Flash + USB CDC
  (2 ESC)      (2 servo)     (RGB)           (4 ch)         + host_proto_app
                                                              ▲
   PC 上位机 ──── USB CDC 虚拟串口 ──────────────────────────────┘
   (C# WinForms)     (二进制帧协议)
```

- **裸机超级循环**（无 RTOS），主循环调度：
  `ecat_app_task()` → `dshot_task()` → `servo_task()` → `adc_mon_task()` →
  `led_task()` → `usb_cdc_app_task()` → `app_task_run()`。
- 所有模块均为**非阻塞状态机**；唯一允许阻塞的是 AM32 4-way 桥（≤150 ms，仅在
  EtherCAT 未进 OP 且电机停转的上位机调参场景）。
- 模块之间不直接互相调用，统一通过 `inc/app_task.c` 的控制任务 + 共享结构体
  （`g_ecat_rxpdo`/`g_ecat_txpdo`/`g_app_state`）耦合。

## 2. 关键设计与资源分配

| 模块 | 文件 | 定时/中断资源 | 周期 |
|---|---|---|---|
| 板级 | `boards/ecat_drone_5e3y/*` | GPTMR0 ch0（延时）、ch1（1ms tick） | — |
| 控制任务 | `src/app_task.c` | 由 1ms tick 计数分频 | 1 kHz |
| 双向 DShot | `src/dshot_bidir.c` | PWM0 计数器1 + CMP4–7；CMP16（pwm_dma_0）作 TX DMA 请求；GPTMR1 ch2/3 捕获中断；XDMA ch0 | 1 kHz 发帧 |
| 舵机 | `src/servo_s288.c` | UART2/3 + HDMA ch4–7 + 串口中断 | 100 Hz 轮询 |
| 状态灯 | `src/led_rgb.c` | PWM1 计数器2/3（20 kHz） | 50 Hz 动画 |
| ADC | `src/adc_mon.c` | ADC0/ADC1 序列+DMA（内部） | 每控制周期 |
| 参数 | `src/param.c` | Flash 0x7E000 扇区（ROM API XPI NOR） | 事件驱动 |
| USB | `src/usb_cdc_app.c` + `src/host_proto_app.c` | USB0 中断 | 事件驱动 |
| EtherCAT | `src/ecat_app.c` + `port/*` | ESC PDI/SYNC 中断、ESC 硬件 | 事件驱动 |

## 3. 数据流

### 3.1 主站 → 电机
```
RxPDO.esc_cmd_us[ch] (1000..2000)
  → app_task_cmd_us_to_dshot(): 线性映射到 48..2047（参数可改），支持方向反转
  → dshot_set_throttle(ch, value)
  → dshot_task(): 每 1ms 组 16bit 帧（含反相 CRC）→ PWM DMA burst 发出
  → 15bit 后切 PF02/PF03 到 TRGM 输入 → GPTMR1 捕获 ESC 回传 21bit 差分/边沿序列
  → GCR/CRC/eRPM 解码 → dshot_get_telemetry() → TxPDO.esc_erpm
```

### 3.2 主站 → 舵机
```
RxPDO.servo_angle_d10[ch] (0.1°)
  → servo_set_target_d10(): 按参数做 增益/零点/限幅 换算
  → servo_task(): 组 20B PD 控制帧（KF/KD/pos_des/...，CRC32）
  → 方向 GPIO 切到 TX → UART DMA 发送 → 发送完成切回 RX
  → 接收 26B 反馈 → CRC32 校验 → 解析位置/速度/扭矩/温度/错误码
  → servo_get_status() → TxPDO.servo_*
```

### 3.3 调参
```
PC 上位机 ── USB CDC ── host_proto_app.c ── param_get/set() ── 参数表
                                             └─ param_apply() → 各模块
                                             └─ param_save() → Flash
EtherCAT 主站 ── CoE (0x2000+) ── ecat_app.c ── param_get/set()
AM32 上位机调参：PC 组 4-way 帧 → HOST_CMD_AM32_XFER → am32_4way.c 位操作 ESC 信号线
```

## 4. 关键状态机

### 4.1 板级状态（`app_state.h`）
```
BOOT → SELF_TEST → ECAT_WAIT → ECAT_PREOP → ECAT_SAFEOP → OP_IDLE → RUN
                                   └────────── FAULT ◀──────────────┘
```
- 状态决定 RGB 灯显示；FAULT 时输出强制禁用；
- FAULT 可由主站 control_word.bit1（清故障）、上位机参数清故障、或重新上电清除。

### 4.2 DShot 状态机（`dshot.h`）
```
UNINIT → TX → SWITCH_TO_RX → WAIT_ESC(≤70us) → RX(21bit) → DECODE → (下一周期 TX)
                                        └─ 超时 → ERROR → 下一周期
```
- 一帧无遥测不影响下一帧；`dshot_telemetry_t.valid` 超时（`PARAM_DSHOT_TLM_VALID_MS`）后清 0。

### 4.3 舵机状态机（`servo_s288.h`）
```
IDLE → TX(20B) → WAIT_TX_DONE → DIR_RX → RX(26B) → PARSE → IDLE
                                  └─ 超时（无舵机）→ online=0
```

## 5. 错误处理

| 故障 | 检测 | 反应 |
|---|---|---|
| EtherCAT 通信超时 | RxPDO.cycle_counter 不变超过 `PARAM_ECAT_WATCHDOG_MS` | 故障位 `ECAT_OP_TIMEOUT`，输出禁用 |
| 过压/欠压/系统过流/电调过流 | ADC 阈值（带回差，`PARAM_ADC_*`） | 对应故障位，输出禁用（可由主站清） |
| 电机遥测丢失 | eRPM 遥测超时 | 故障位 `ESCn_NO_TLM`（仅输出使能时） |
| 舵机离线/错误 | 26B 反馈超时/错误码 | 状态位、故障位（可配） |
| 参数/Flaш 存储损坏 | magic+CRC 校验 | 自动恢复默认值并告警 |
| USB 协议错误 | 帧 CRC/长度 | 丢弃并计数，不影响控制 |
| AM32 调参异常 | 4-way 超时 | 返回错误码，恢复 DShot |

## 6. 参数系统

- 所有可调量集中在 `inc/param.h`（60 项），含范围/默认值/持久化属性；
- 参数存 `Flash 0x7E000`（4KB 扇区），带 magic/version/CRC，掉电恢复；
- 三类访问入口：EtherCAT CoE（主站）、USB 上位机、固件内部默认值；
- 修改参数后 `param_apply()` 立即下发到各模块（无需重启）。

## 7. 与 hpm_sdk 样例的对应关系

| 本工程部分 | 参照的 SDK 样例 |
|---|---|
| 板级包结构/board_init/clock | `hpm_sdk/boards/hpm5e00evk` |
| EtherCAT 应用结构与协议栈胶水 | `hpm_sdk/samples/ethercat/ecat_io` |
| ESC HAL / EEPROM 模拟 | `hpm_sdk/samples/ethercat/port` |
| PWMV2 输出/多计数器同步 | `hpm_sdk/samples/drivers/pwmv2/*` |
| DMAV2 fixed burst | `hpm_sdk/samples/drivers/dmav2/dmav2_fixed_burst_swap` |
| GPTMR 捕获/事件 DMA | `hpm_sdk/samples/drivers/dma/dma_use_gptmr_event_transfer` |
| ADC16 序列采样 | `hpm_sdk/samples/drivers/adc16/*` |
| UART + DMA | `hpm_sdk/samples/drivers/uart/*` |
| USB CDC-ACM | `hpm_sdk/samples/cherryusb/device/cdc_acm/cdc_acm_vcom` |
| Flash ROM API | `hpm_sdk/samples/drivers/romapi/xpi_nor_flash` |
| 双向 DShot 算法 | HPMicro 知识库《双向 DSHOT（BDShot）收发实现概要设计》 |
| AM32 4-way 协议 | BLHeli/AM32 4way-if 规范（见 docs/DSHOT_AM32.md） |
