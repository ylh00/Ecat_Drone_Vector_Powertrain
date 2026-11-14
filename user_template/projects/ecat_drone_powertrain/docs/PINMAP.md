# 板级引脚分配表 — ecat_drone_5e3y

> 依据：`board.h`、`pinmux.c`（由用户提供的 `pin_mux/pinmux.c` 修改而来）、
> 《HPM5E00 数据手册 Rev0.4》、《HPM5E00 片内 MII PHY 使用指南 v1.1》。
> 芯片：HPM5E3YIGN1（BGA196，内置 1MB Flash + 2× 片内 MII PHY）。

## 1. 网络 / EtherCAT

| 信号 | 引脚 | IOC 功能 | 说明 |
|---|---|---|---|
| ESC0_MDC | PA31 | `ESC0_MDC` | 片内 PHY MDIO 时钟，外部需上拉 |
| ESC0_MDIO | PA30 | `ESC0_MDIO` | 片内 PHY MDIO 数据，外部需上拉 |
| ESC0_REFCK | PW20, PW21 | `ESC0_REFCK` | ESC 100MHz/4 = 25MHz 参考时钟输出至片内 PHY |
| ESC0 P0 MII | PV00–PV15 | `ESC0_P0_*` | 片内 MII PHY0（内部未引出 IO），P0 = RXDV/RXD0-3/RXCK/RXER/TXCK/TXD0-3/TXEN |
| ESC0 P1 MII | PW00–PW15 | `ESC0_P1_*` | 片内 MII PHY1（内部未引出 IO） |
| PHY0 复位 | PV12 | `GPIO_V_12` | resetn，低有效 |
| PHY1 复位 | PW12 | `GPIO_W_12` | resetn，低有效 |
| PHY0 模拟线 | PA20–PA23 | ANALOG | PHY0 的 RD_N/RD_P/TD_N/TD_P，接网络变压器 |
| PHY1 模拟线 | PA16–PA19 | ANALOG | PHY1 的 RD_N/RD_P/TD_N/TD_P，接网络变压器 |
| PHY0 RBIAS | PA27 | ANALOG | 2.49k 1% 对地 |
| PHY1 RBIAS | PA24 | ANALOG | 2.49k 1% 对地 |
| PHY0 LED0 / 地址位0 | PA29 | ANALOG | 地址 strap，勿作数字功能 |
| PHY1 LED0 / 地址位0 | PA26 | ANALOG | 地址 strap，勿作数字功能 |
| PHY0 LED1 / 地址位1 | PA28 | `ESC0_CTR_1` + 上拉 | 也是 ESC0 端口0 链路状态输入（NMII LINK0） |
| PHY1 LED1 / 地址位1 | PA25 | `ESC0_CTR_0` + 上拉 | 也是 ESC0 端口1 链路状态输入（NMII LINK1） |
| PHY LDO | PW16, PW17 | ANALOG | PHY1/PHY0 内部 LDO 去耦脚，必须配置为模拟功能 |

> 说明：`BOARD_ECAT_NMII_LINK0_CTRL_INDEX = 1`（port0 用 CTR_1 → PA28），
> `BOARD_ECAT_NMII_LINK1_CTRL_INDEX = 0`（port1 用 CTR_0 → PA25），与 PHY 指南一致。
> PHY MDIO 地址沿用 HPM5E00EVK 约定：端口0 = `PHY_ADDR_OFFSET(1) + 0 = 1`，端口1 = 2。

## 2. 无刷电机（双向 DShot）

| 信号 | 引脚 | IOC 功能 | PWM 资源 |
|---|---|---|---|
| ESC1 DShot | PF02 | `PWM0_P_2`（接收时切 `TRGM_P_02`） | PWM0 计数器1，CMP[4]/CMP[5] |
| ESC2 DShot | PF03 | `PWM0_P_3`（接收时切 `TRGM_P_03`） | PWM0 计数器1，CMP[6]/CMP[7] |
| 回传捕获 | — | TRGM → GPTMR1 捕获通道2/3 | GPTMR1 CH2 (ESC1) / CH3 (ESC2) |

> GPIO 端口：GPIO_F 的 bit2/bit3（`BOARD_DSHOT_GPIO_*`），用于 AM32 4-way 位操作与调试。
> PWMV2 映射：CMP[0..3]→计数器0（输出0/1），CMP[4..7]→计数器1（输出2/3），CMP[8..11]→计数器2（输出4/5），CMP[12..15]→计数器3（输出6/7）。

## 3. 总线舵机（宇树 J288/S288，单总线 6 Mbps 8N1）

| 信号 | 引脚 | IOC 功能 | 说明 |
|---|---|---|---|
| 舵机1 TX | PC08 | `UART2_TXD` | 经 SPDT 模拟开关接总线 |
| 舵机1 RX | PC09 | `UART2_RXD` | 经 SPDT 模拟开关接总线 |
| 舵机1 方向 | PC16 | `GPIO_C_16` | 收发切换控制，TX 有效电平可配（`PARAM_SERVO_DIR_TX_LEVEL1`，默认高） |
| 舵机2 TX | PC15 | `UART3_TXD` | 经 SPDT 模拟开关接总线 |
| 舵机2 RX | PC14 | `UART3_RXD` | 经 SPDT 模拟开关接总线 |
| 舵机2 方向 | PD00 | `GPIO_D_00` | 收发切换控制（注意：PD00 原为 MCAN0_TXD，本板 MCAN0 不可用） |

> UART2/UART3 时钟 = 48 MHz（PLL0 480MHz / 10），6.0 Mbps 精确分频（OSR=8，DIV=1）。

## 4. 状态灯（RGB 三色一体）

| 颜色 | 引脚 | IOC 功能 | PWM 资源 |
|---|---|---|---|
| 绿 | PF12 | `PWM1_P_4` | PWM1 计数器2，CMP[8]/CMP[9] |
| 红 | PF13 | `PWM1_P_5` | PWM1 计数器2，CMP[10]/CMP[11] |
| 蓝 | PF14 | `PWM1_P_6` | PWM1 计数器3，CMP[12]/CMP[13] |

> 计数器2/3 需同频率（20 kHz）并同步启动；`BOARD_RGB_LED_ACTIVE_LOW=1`（共阳接法，低电平点亮）。
> 若实板为共阴接法，把该宏改为 0 并重新编译。

## 5. 模拟量（ADC16）

| 信号 | 引脚 | ADC 通道 | 备注 |
|---|---|---|---|
| 系统电流 | PF26 | ADC0_IN01（等效 ADC1_IN01） | 16 位，参数 `PARAM_ADC_SYS_I_*` 标定 |
| 系统电压 | PF16 | ADC0_IN08（等效 ADC1_IN08） | 参数 `PARAM_ADC_SYS_V_*` 标定 |
| 电调1 电流 | PF20 | ADC1_IN03 | 参数 `PARAM_ADC_ESC1_I_SCALE` |
| 电调2 电流 | PF19 | ADC1_IN13 | 参数 `PARAM_ADC_ESC2_I_SCALE` |

> 数据手册确认：只有 PF16–PF31 具备 ADC 通道，且同一通道号在 ADC0/ADC1 上是同一个 pad。
> 上述 4 个 pad 在 pinmux.c 中配置为 ANALOG；PA16–PA29 的 ANALOG 配置属于片内 PHY 模拟信号，不是 ADC。

## 6. 调试 / 其他

| 信号 | 引脚 | IOC 功能 | 说明 |
|---|---|---|---|
| UART0 日志 | PA00 TX / PA01 RX | `UART0_TXD/RXD` | 115200 8N1 |
| JTAG | PA04 TDO / PA05 TDI / PA06 TCK / PA07 TMS / PA08 TRST | `JTAG_*` | 标准 JTAG 调试口 |
| USB | 专用引脚（USB0_DP/DN/VBUS） | — | 不在 IOC 引脚表中；VBUS 耐压 5.5V |
| 启动模式 | PA02/PA03 | BOOT_MODE | 00 = 内部 XPI NOR 启动（默认） |
| 预留：MCAN1 | PD03/PD04/PD05 | `MCAN1_*` | 未使用，保留 |
| 预留：MCAN2 | PF08/PF09/PF10 | `MCAN2_*` | 未使用，保留 |
| 预留：PWM0 P4–P7 | PF04–PF07 | `PWM0_P_4..7` | 未使用，保留 |
| 预留：I2C3 | PD06/PD07 | `I2C3_SDA/SCL` | 开漏 + 上拉，未使用 |
| 预留：SPI1 | PC10–PC13 | `SPI1_*` | 未使用 |

## 7. 资源分配约定（多模块共享时）

| 资源 | 使用者 |
|---|---|
| GPTMR0 ch0 | board_delay_us/ms |
| GPTMR0 ch1 | 1 ms 软件定时器（board_timer_create） |
| GPTMR1 ch2/ch3 | 双向 DShot 回传捕获 |
| PWM0 计数器1 + CMP4–7 | 双向 DShot 输出（CMP4/5 = ESC1，CMP6/7 = ESC2） |
| PWM0 比较点 CMP16（pwm_dma_0） | 双向 DShot TX DMA 周期请求（`DSHOT_DMA_TRIGGER_CMP`） |
| GPTMR2 ch0 | EtherCAT 从站 1 ms 定时器（`ECAT_TIMER_GPTMR`，ecat_config.h） |
| PWM1 计数器2 + CMP8–11 / 计数器3 + CMP12–13 | RGB 状态灯 |
| HPM_XDMA ch0 | DShot TX burst（TRGM DMACFG_0 → MOT_0） |
| HPM_HDMA ch4–7 | UART2/3 舵机 TX/RX DMA |
| IRQn_GPTMR1 / IRQn_PWM0 | DShot（XDMA 不使用中断，查询状态） |
| IRQn_UART2 / IRQn_UART3 / IRQn_HDMA | 舵机 |
| IRQn_GPTMR0 | 板级 1ms 定时器 |
| IRQn_GPTMR2 | EtherCAT 1 ms 定时器 |
| IRQn_ESC / IRQn_ESC_SYNC0 / IRQn_ESC_SYNC1 / IRQn_ESC_RESET | EtherCAT（port/hpm_ecat_hw.c） |
| IRQn_USB0 | USB CDC |
| Flash 0x80000 起 64KB（16 × 4KB 扇区） | EtherCAT EEPROM 模拟区（`FLASH_EEPROM_SIZE`） |
| Flash 0x7E000 起 4KB | 参数存储区（`PARAM_FLASH_OFFSET`）；与 EEPROM 区不重叠 |
