.. _ecat_drone_5e3y:

ecat_drone_5e3y 开发板
======================

概述
----

ecat_drone_5e3y 是一款基于 HPM5E3Y（HPM5E00 系列，RISC-V 内核）的 EtherCAT 从站控制板，
用于无人机 / 矢量动力总成（"vector powertrain"）控制：

- 2 路双向 DShot 电调（AM32）接口，可回读 eRPM 遥测；
- 2 路 S288/J288 总线舵机（单总线半双工，带 SPDT 方向切换电路）；
- EtherCAT 从站（ESC0，2 个端口，使用片内合封的 2 颗百兆 MII PHY）；
- ADC 监测（系统电压 / 系统电流 / 2 路电调电流）；
- RGB 状态指示灯；
- USB0 CDC-ACM 虚拟串口，用于上位机调试工具；
- UART0 调试日志串口。

.. note::

   HPM5E3Y 与 HPM5E31 的 SoC 头文件（``hpm_soc.h`` / ``hpm_iomux.h`` /
   ``hpm_soc_ip.h`` 等）在 HPM SDK v1.13.0 中内容一致，但 SDK v1.13.0 未提供
   HPM5E3Y 的构建支持文件（system.c / hpm_clock_drv.c / 链接脚本 / boot header），
   因此本板级包的 ``ecat_drone_5e3y.yaml`` 选择 ``soc: HPM5E31`` 进行构建，
   而 ``device: HPM5E3YIGN1`` 仅用于调试器识别芯片。
   Flash 为 HPM5E3Y 合封的 1 MB XPI NOR（P25Q80SU）。

板上硬件资源
------------

- HPM5E3YIGN1 / HPM5E3YIVK1 微控制器
  - 480 MHz RISC-V CPU，544 KB SRAM
  - 内置 1 MB XPI NOR Flash（合封，XPI0，映射地址 0x8000_0000）
  - 内置 2 颗百兆 MII PHY（PHY0 / PHY1，100BASE-TX 全双工，支持自协商与 Auto-MDIX）
  - EtherCAT 从站控制器 ESC0（3 端口能力，本板使用 2 个端口）
- EtherCAT
  - RJ45 x2（端口 0 = 内部 PHY0，端口 1 = 内部 PHY1）
  - EtherCAT EEPROM 使用 Flash 模拟（偏移 0x80000，见下）
- 电机 / 舵机
  - 2 路双向 DShot 电调信号（PWM0.P2 / PWM0.P3）
  - 2 路 S288/J288 总线舵机（UART2 / UART3，6.0 Mbps 单总线半双工）
- 模拟采样
  - 系统电压、系统电流、ESC1 电流、ESC2 电流（ADC0 / ADC1）
- 其他
  - RGB 状态指示灯（PWM1.P4/P5/P6，共阳 / 低电平点亮）
  - USB0 设备口（CDC-ACM 虚拟串口）
  - UART0 调试串口（115200-8N1）
  - JTAG 调试接口（PA04 ~ PA08）
  - 预留 CAN0/CAN1/CAN2、I2C3、SPI1、PWM0.P4 ~ P7 引脚

时钟
----

.. list-table::
   :header-rows: 1

   * - 时钟
     - 频率
     - 说明
   * - cpu0
     - 480 MHz
     - PLL0 960 MHz / 2
   * - ahb0
     - PLL1 clk0 / 2
     - 总线时钟
   * - xpi0
     - -
     - 片内 NOR Flash 接口
   * - mchtmr0
     - 24 MHz
     - OSC24M
   * - uart0
     - 时钟预置默认值
     - 调试控制台 115200-8N1
   * - uart2
     - 48 MHz
     - PLL0 clk0 480 MHz / 10，舵机 6.0 Mbps
   * - uart3
     - 48 MHz
     - 同 uart2
   * - gptmr0
     - 时钟预置默认值
     - ch0 延时定时器 / ch1 1 ms 软件定时器
   * - gptmr1
     - 时钟预置默认值
     - ch2/ch3 双向 DShot 遥测捕获
   * - esc0
     - 100 MHz
     - ESC 功能时钟（由时钟预置 2 提供，供 ESC 及 PHY 的 25 MHz REFCK 使用）

.. note::

   - S288/J288 舵机的单总线波特率固定为 **6.0 Mbps**（8N1），无小数分频误差：
     UART2 / UART3 时钟 = PLL0 clk0（480 MHz）/ 10 = 48 MHz，
     48 MHz / (OSR 8 × DIV 1) = 6.0 Mbps 精确成立。
     因此 **请勿修改** ``clock.c`` 中 ``init_uart2_clock()/init_uart3_clock()`` 的分频值（10）。
   - ESC 模块和片内 PHY 必须使用同一时钟源：ESC 100 MHz 功能时钟经 4 分频得到
     25 MHz REFCK 输出给 PHY（见 HPM5E00 用户手册 ESC 章节），时钟预置 2 已保证该频率。

启动模式
--------

HPM5E3Y 通过 BOOT_MODE[1:0] = PA03:PA02 选择启动模式：

.. list-table::
   :header-rows: 1

   * - PA03:PA02
     - 启动模式
   * - 00
     - XPI NOR 启动（本板默认，从合封的 1 MB NOR 启动）
   * - 01 / 10
     - 在系统编程（ISP）/ 串行启动（UART0 / USB0）
   * - 11
     - 保留

.. note::

   PA02 / PA03 不在 pinmux 表中，板级代码不会改动它们。正常使用时应保持
   ``00``，即从片内 NOR Flash 启动。

引脚描述
--------

- UART0 调试串口

  UART0 用作调试控制台，115200 8N1。

  .. list-table::
     :header-rows: 1

     * - 功能
       - 引脚
     * - UART0.TXD
       - PA00
     * - UART0.RXD
       - PA01

- S288/J288 总线舵机接口

  两路舵机接口为单总线（1-Wire）半双工，使用 UART2 / UART3 加外部 SPDT
  模拟开关切换 TX/RX 方向。方向开关默认电平（TX 电平）由
  ``BOARD_SERVO_DIR_TX_LEVEL_DEFAULT`` 定义，运行时由舵机驱动按参数
  ``PARAM_SERVO_DIR_TX_LEVELn`` 覆盖。

  .. list-table::
     :header-rows: 1

     * - 功能
       - 引脚
       - 说明
     * - UART2.TXD
       - PC08
       - 舵机 #1（SERVO1）
     * - UART2.RXD
       - PC09
       - 舵机 #1（SERVO1）
     * - SERVO1_DIR
       - PC16 (GPIO_C_16)
       - 舵机 #1 方向开关控制
     * - UART3.RXD
       - PC14
       - 舵机 #2（SERVO2）
     * - UART3.TXD
       - PC15
       - 舵机 #2（SERVO2）
     * - SERVO2_DIR
       - PD00 (GPIO_D_00)
       - 舵机 #2 方向开关控制（PD00 同时是 MCAN0.TXD 的复用脚，
         本板不使用 MCAN0）

- 双向 DShot 电调接口

  .. list-table::
     :header-rows: 1

     * - 功能
       - 引脚
       - 说明
     * - PWM0.P2 / ESC1
       - PF02
       - 双向 DShot 输出 + GPTMR1 CAPT_2 遥测输入（TRGM 内部切换）
     * - PWM0.P3 / ESC2
       - PF03
       - 双向 DShot 输出 + GPTMR1 CAPT_3 遥测输入（TRGM 内部切换）

- RGB 状态指示灯

  .. list-table::
     :header-rows: 1

     * - 颜色
       - 引脚
       - PWM 通道
     * - 绿色 (G)
       - PF12
       - PWM1.P4
     * - 红色 (R)
       - PF13
       - PWM1.P5
     * - 蓝色 (B)
       - PF14
       - PWM1.P6

- ADC 模拟输入

  HPM5E3Y 只有 PF16 ~ PF31 这 16 个 pad 带 ADC 通道，且 ADC0 与 ADC1 的
  通道号到 pad 的映射相同（例如 ADC0_IN01 与 ADC1_IN01 都在 PF26）。
  本板使用的 4 路 ADC 如下（pinmux 中已配置为 ANALOG）：

  .. list-table::
     :header-rows: 1

     * - 功能
       - 引脚
       - ADC 通道
     * - 系统电流
       - PF26
       - ADC0_IN01 / ADC1_IN01
     * - 系统电压
       - PF16
       - ADC0_IN08 / ADC1_IN08
     * - ESC1 电流
       - PF20
       - ADC0_IN03 / ADC1_IN03
     * - ESC2 电流
       - PF19
       - ADC0_IN13 / ADC1_IN13

  .. warning::

     PA16 ~ PA29 上的 ANALOG 引脚是片内 MII PHY 的模拟信号
     （RD_N/RD_P/TD_N/TD_P、RBIAS、LED 等），**不是 ADC 通道**，不可用作
     模拟采样。

- EtherCAT / 片内 MII PHY

  .. list-table::
     :header-rows: 1

     * - 功能
       - 引脚 / 位置
       - 说明
     * - 端口 0（RJ45 #1）
       - PV00 ~ PV15
       - 内部 MII PHY0 的 MII 接口（封装内部 IO，未引出）
     * - 端口 1（RJ45 #2）
       - PW00 ~ PW15
       - 内部 MII PHY1 的 MII 接口（封装内部 IO，未引出）
     * - ESC0.MDIO
       - PA30
       - PHY 管理接口（外加上拉）
     * - ESC0.MDC
       - PA31
       - PHY 管理接口（外加上拉）
     * - PHY0 复位
       - PV12 (GPIO_V_12)
       - resetn，低有效
     * - PHY1 复位
       - PW12 (GPIO_W_12)
       - resetn，低有效
     * - REFCK（25 MHz）
       - PW20 / PW21
       - ESC 100 MHz 功能时钟 4 分频后输出给 PHY
     * - PHY0 LED1 / NMII_LINK
       - PA28 (ESC0_CTR_1)
       - PHY0 链路指示，内部上拉
     * - PHY1 LED1 / NMII_LINK
       - PA25 (ESC0_CTR_0)
       - PHY1 链路指示，内部上拉
     * - PHY0 模拟线侧
       - PA20 ~ PA23
       - RD_N/RD_P/TD_N/TD_P，ANALOG
     * - PHY1 模拟线侧
       - PA16 ~ PA19
       - RD_N/RD_P/TD_N/TD_P，ANALOG
     * - PHY0 / PHY1 RBIAS
       - PA27 / PA24
       - 2.49 kΩ 1% 对地，ANALOG
     * - PHY0 / PHY1 LED0
       - PA29 / PA26
       - PHY 地址 strap 位 0，ANALOG
     * - PHY0 / PHY1 LDO
       - PW17 / PW16
       - 片内 LDO 使能，ANALOG

- USB0

  USB0 用作设备口（CDC-ACM 虚拟串口，供上位机调试工具使用）。
  USB0_VBUS / USB0_DP / USB0_DM 为封装专用引脚，不在 pinmux 中；
  USB0_ID / USB0_OC / USB0_PWR 的复用脚（PF21/PF22/PF23/PF18/PF19）
  本板未使用（其中 PF19 用作 ADC1_IN13 电调电流采样），
  ``init_usb_pins()`` 为空实现。

- JTAG

  .. list-table::
     :header-rows: 1

     * - 功能
       - 引脚
     * - JTAG.TCK
       - PA06
     * - JTAG.TDI
       - PA05
     * - JTAG.TDO
       - PA04
     * - JTAG.TMS
       - PA07
     * - JTAG.TRST
       - PA08

- 预留接口（pinmux 已配置，当前固件未使用）

  .. list-table::
     :header-rows: 1

     * - 功能
       - 引脚
       - 说明
     * - SPI1.SCLK / CS0 / MISO / MOSI
       - PC10 / PC11 / PC12 / PC13
       - 预留 SPI
     * - I2C3.SCL / SDA
       - PD07 / PD06
       - 预留 I2C（开漏 + 上拉）
     * - MCAN0.RXD / STBY
       - PD01 / PD02
       - 预留 CAN0（TXD 复用脚 PD00 已改作舵机方向开关）
     * - MCAN1.RXD / TXD / STBY
       - PD04 / PD05 / PD03
       - 预留 CAN1
     * - MCAN2.RXD / TXD / STBY
       - PF09 / PF08 / PF10
       - 预留 CAN2
     * - PWM0.P4 ~ P7
       - PF04 ~ PF07
       - 预留 PWM 输出

定时器 / 外设分配
-----------------

.. list-table::
   :header-rows: 1

   * - 外设
     - 用途
   * - GPTMR0 ch0
     - ``board_delay_us()`` / ``board_delay_ms()``（轮询 RLD 标志）
   * - GPTMR0 ch1
     - ``board_timer_create()`` 1 ms 软件定时器（中断回调）
   * - GPTMR1 ch2/ch3
     - 双向 DShot 电调遥测捕获（TRGM 路由）
   * - PWM0.P2/P3
     - 双向 DShot 输出
   * - PWM1.P4/P5/P6
     - RGB 状态灯
   * - ADC0 / ADC1
     - 系统电压/电流、ESC1/ESC2 电流
   * - UART0
     - 调试控制台
   * - UART2 / UART3
     - S288 总线舵机
   * - USB0
     - CDC-ACM 虚拟串口（上位机调试工具）
   * - ESC0
     - EtherCAT 从站 + 2 路片内 MII PHY

片内 MII PHY 注意事项
---------------------

1. **PHY 复位时序**（``board_init_ethercat()`` / ``board_ecat_phy_reset()``）：

   - PV12（PHY0）/ PW12（PHY1）为 resetn，低有效；
   - 拉低复位 ≥ 200 ns（板级代码实际保持 10 ms，
     ``BOARD_ECAT_PHY_RESET_HOLD_TIME_MS``）；
   - 释放复位后至少等待 5 ms 才允许 MDIO 访问（上电到 MDIO 可用的最低要求），
     板级代码等待 30 ms（``BOARD_ECAT_PHY_RESET_WAIT_TIME_MS``）。
   - ``board_init_ethercat()`` 只把两个复位脚配置为输出并保持在复位状态，
     真正的复位/释放由 EtherCAT 端口层在 ``ecat_hardware_init()`` 中调用
     ``board_ecat_phy_reset()`` 完成。

2. **PHY MDIO 地址约定**：SDK EtherCAT 例程按 HPM5E00EVK 的端口地址定义，
   实际 MDIO 地址 = ``BOARD_ECAT_PHY_ADDR_OFFSET`` + ``BOARD_ECAT_PORTx_PHY_ADDR``：

   .. list-table::
      :header-rows: 1

      * - 端口
        - PHY
        - MDIO 地址（默认）
      * - port0（RJ45 #1）
        - PHY0
        - 1
      * - port1（RJ45 #2）
        - PHY1
        - 2

   若实际板级 strap（PA25/PA28 等 LED/地址脚的上拉/下拉电阻）与默认值不同，
   需要修改 ``board.h`` 中的 ``BOARD_ECAT_PHY_ADDR_OFFSET`` /
   ``BOARD_ECAT_PORTx_PHY_ADDR``，并用 MDIO 扫描确认。

3. **NMII 链路状态**：PHY 的 LED1 引脚复用为链路指示并接到 ESC0_CTR 信号，
   PA28（PHY0 LED1）→ ``ESC0_CTR_1``、PA25（PHY1 LED1）→ ``ESC0_CTR_0``，
   因此 ``BOARD_ECAT_NMII_LINK0_CTRL_INDEX = 1``、
   ``BOARD_ECAT_NMII_LINK1_CTRL_INDEX = 0``。如链路极性相反，可修改
   ``BOARD_ECAT_PORTx_LINK_INVERT``。

4. 本板只有 2 个 EtherCAT 端口：``BOARD_ECAT_SUPPORT_PORT1 = 1``、
   ``BOARD_ECAT_SUPPORT_PORT2 = 0``。

5. EtherCAT EEPROM 使用 Flash 模拟，位于片内 1 MB NOR 的偏移 0x80000 处
   （``BOARD_ECAT_FLASH_EMULATE_EEPROM_ADDR``），因此应用程序镜像最大 512 KB。

构建
----

依赖 HPM SDK v1.13.0 环境（``sdk_env_v1.13.0``，内含 RISC-V GCC 13.2、
CMake、Ninja、Python3），在 Windows 下执行：

.. code-block:: bat

   rem 方式一：使用构建脚本（默认工程 projects\ecat_drone_powertrain，debug / flash_xip）
   cmd /c D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\scripts\build.cmd

   rem 可选参数：clean（重新构建）/ release / ram / flash_xip
   cmd /c ...\scripts\build.cmd -BuildDir D:\Ecat_Drone_Vector_Powertrain\.work\build_dev

也可以手动调用 CMake：

.. code-block:: powershell

   # 先执行 start_cmd.cmd 或按 scripts\build.ps1 开头设置环境变量
   cmake -G Ninja -S D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\projects\ecat_drone_powertrain `
         -B D:\Ecat_Drone_Vector_Powertrain\.work\build_dev `
         -DBOARD=ecat_drone_5e3y `
         -DBOARD_SEARCH_PATH=D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\boards `
         -DHPM_BUILD_TYPE=flash_xip -DCMAKE_BUILD_TYPE=debug
   cmake --build D:\Ecat_Drone_Vector_Powertrain\.work\build_dev

构建产物位于 ``<build dir>\output\``：``*.elf`` / ``*.bin`` / ``*.map`` / ``*.asm``。

烧录 / 调试
-----------

使用 SDK 自带的 OpenOCD（``sdk_env_v1.13.0\tools\openocd\bin\openocd.exe``）。
本板使用板级配置文件 ``user_template\boards\ecat_drone_5e3y\ecat_drone_5e3y.cfg``
（1 MB XPI NOR，基地址 0x8000_0000）。在 ``sdk_env_v1.13.0`` 目录下执行：

.. code-block:: bat

   tools\openocd\bin\openocd -f hpm_sdk\boards\openocd\probes\ft2232.cfg ^
       -f hpm_sdk\boards\openocd\soc\hpm5e00.cfg ^
       -f user_template\boards\ecat_drone_5e3y\ecat_drone_5e3y.cfg ^
       -c "program user_template\projects\ecat_drone_powertrain\build_ecat_drone_5e3y_flash_xip_debug\output\ecat_drone_powertrain.elf verify reset exit"

- 板载调试器为 FT2232（对应 ``probes\ft2232.cfg``，需先安装 FTDI 驱动）；
  如使用 J-Link，把 ``ft2232.cfg`` 换成 ``probes\jlink.cfg`` 即可。
- 也可以在 ``start_gui.exe`` 中生成工程后用 Segger Embedded Studio / IAR
  下载调试。
- 首次上电时片内 NOR 为空，EtherCAT EEPROM 模拟区会自动初始化；写入
  EtherCAT 从站 EEPROM（ESI）的步骤见工程 README。

版本历史
--------

- 2026-11-14：首个版本，支持 EtherCAT 从站、双向 DShot、S288 总线舵机、
  RGB 状态灯、ADC 监测、USB CDC-ACM。
