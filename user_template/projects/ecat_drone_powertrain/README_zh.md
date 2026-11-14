# ecat_drone_powertrain — EtherCAT 无人机矢量动力总成从站固件

> 目标芯片：**HPM5E3YIGN1**（先楫半导体，内置 1MB XPI NOR Flash + 2 路片内 MII PHY）
> 板级包：`user_template/boards/ecat_drone_5e3y`（SoC 名称 `HPM5E31`，同 IP）
> SDK：hpm_sdk v1.13.0（`sdk_env_v1.13.0`）
> 上位机：`user_template/tools/ecat_drone_host`（C# WinForms）

---

## 1. 功能概述

本工程实现一个 EtherCAT 从站动力总成控制器：

| 功能 | 实现方式 | 引脚 |
|---|---|---|
| EtherCAT 从站（2 个网口） | ESC0 + 片内 MII PHY0/PHY1，从站协议栈（Beckhoff SSC，需自行生成） | PV/PW 内部 MII，PA30/31 MDIO |
| 双无刷电机控制 | 双向 DShot（BDShot）600 kbps，PWM0 输出 + GPTMR1 捕获回传 eRPM | PF02 = PWM0.P2，PF03 = PWM0.P3 |
| 双总线舵机（宇树 J288/S288） | UART2 / UART3，6.0 Mbps 8N1，半双工单总线，SPDT 方向切换 | 舵机1：PC08=TX / PC09=RX + PC16 方向；舵机2：PC15=TX / PC14=RX + PD00 方向 |
| 状态指示 | 1 个 RGB 三色灯（绿/红/蓝），PWM1 三通道混合颜色 + 动画 | PF12/PF13/PF14 = PWM1.P4/P5/P6 |
| 系统电压/电流监控 | ADC0 IN1 = 系统电流，ADC0 IN8 = 系统电压 | PF26 / PF16 |
| 双电调电流检测 | ADC1 IN3 = 电调1 电流，ADC1 IN13 = 电调2 电流 | PF20 / PF19 |
| 调试日志 | UART0 115200 8N1 | PA00/PA01 |
| PC 调参 | USB0 虚拟串口（CDC-ACM） | 专用 USB 引脚 |
| AM32 电调调参 | 读：4-way interface（19200 8N1 位操作）经 USB 串口桥接；写：AM32 DShot 编程模式 | 复用 DShot 信号线 |
| EtherCAT 参数可改 | 参数表 + Flash 持久化 + CoE 对象 0x2000+ + 上位机修改 | — |

主控（EtherCAT 主站）下发的指令：
- 两个电机的油门指令：**1000 ~ 2000**（对应 DShot 油门 48 ~ 2047）；
- 两个总线舵机的期望角度（0.1° 单位）；
- 控制字（使能/急停/清故障/舵机使能）。

从站回传：
- 两路双向 DShot 回传的电机转速（eRPM 原始值）+ 遥测周期；
- 每个舵机的监控状态（位置、速度、电流估算、温度、错误码）；
- 系统电压/电流、两路电调电流；
- 状态字、故障码、EtherCAT 链路/AL 状态、状态灯状态。

---

## 2. 目录结构

```
sdk_env_v1.13.0/user_template/
├── boards/ecat_drone_5e3y/          # 板级支持包
│   ├── board.h                      # 板级宏定义（所有引脚/外设契约）
│   ├── board.c                      # 板级初始化（时钟/串口/引脚/延时/定时器/ECAT 硬件）
│   ├── clock.c / clock.h            # 时钟树
│   ├── pinmux.c / pinmux.h          # 引脚复用（由 pin_mux/pinmux.c 修改而来）
│   ├── ecat_drone_5e3y.yaml         # 板级描述（SoC/Flash/特性）
│   ├── ecat_drone_5e3y.cfg          # OpenOCD 下载配置
│   ├── CMakeLists.txt
│   └── README_zh.rst                # 板级说明
├── projects/ecat_drone_powertrain/  # 应用工程
│   ├── CMakeLists.txt
│   ├── app.yaml
│   ├── ecat.c                       # EtherCAT 协议栈启动胶水（参照 ecat_io）
│   ├── ecat_config.h / user_config.h
│   ├── inc/                         # 公共头文件（PDO、参数、协议、各模块 API）
│   ├── src/                         # 应用源码（main/app_task/dshot/servo/led/adc/usb/param/ecat_app）
│   ├── port/                        # EtherCAT 硬件抽象层（来自 samples/ethercat/port）
│   ├── SSC/                         # SSC Tool 输入文件 + ESI + 离线编译 stub
│   ├── tests/                       # 回归测试脚本（协议/舵机/DShot 算法）
│   ├── docs/                        # 设计文档（架构/PDO/协议/舵机/DShot/AM32/验证等）
│   └── README_zh.md                 # 本文件
├── tools/ecat_drone_host/           # 上位机（C#，csc.exe 编译）
└── scripts/                         # build.cmd / build.ps1 / check_syntax.py 等
```

---

## 3. 构建

### 3.1 一键构建（推荐）

```bat
:: 在任意 cmd 窗口（无需先运行 start_cmd.cmd）
cd /d D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\scripts
build.cmd                 :: debug + flash_xip
build.cmd clean           :: 清理后重新构建
build.cmd release         :: release
build.cmd ram             :: 下载到 RAM 调试
```

产物：`projects\ecat_drone_powertrain\build_ecat_drone_5e3y_flash_xip_debug\output\ecat_drone_powertrain.elf`（以及 `.bin`）。

### 3.2 start_gui 图形化构建

1. 双击 `sdk_env_v1.13.0\start_gui.exe`；
2. 勾选 **Enable User Board Path**，选择 `sdk_env_v1.13.0\user_template\boards`；
3. 勾选 **Enable User Application Path**，选择 `sdk_env_v1.13.0\user_template\projects`；
4. Board 选 `ecat_drone_5e3y`，Sample 选 `ecat_drone_powertrain`；
5. Build Type 选 `flash_xip`，点击 Generate / Compile。

### 3.3 手动 CMake

```powershell
# 先执行 start_cmd.cmd 或手动设置环境（见 scripts/build.ps1 开头）
cmake -G Ninja -S projects\ecat_drone_powertrain -B build_ecat_drone_5e3y `
      -DBOARD=ecat_drone_5e3y `
      -DBOARD_SEARCH_PATH=D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\boards `
      -DHPM_BUILD_TYPE=flash_xip -DCMAKE_BUILD_TYPE=debug
cmake --build build_ecat_drone_5e3y
```

### 3.4 烧录 / 调试

```bat
:: 使用 SDK 自带 openocd（FT2232 调试器，需先安装 FTDI 驱动）
tools\openocd\bin\openocd -f boards\openocd\hpm5e00_all_in_one.cfg ^
    -c "program user_template\projects\ecat_drone_powertrain\build_ecat_drone_5e3y_flash_xip_debug\output\ecat_drone_powertrain.elf verify reset exit"
```

或使用 start_gui 的 Flash 按钮 / Segger Embedded Studio / IAR 工程（CMake 构建时自动生成）。

---

## 4. EtherCAT 从站配置

### 4.1 重要：从站协议栈（SSC）需自行生成

由于倍福 SSC 协议栈的授权限制，**hpm_sdk 不包含从站协议栈源码**（`samples/ethercat/ecat_io/SSC/Src` 只有一个 README.txt）。
本工程做了以下处理：

- `SSC/Src/`：预留 SSC Tool 生成代码的目录（构建系统检测到 `SSC/Src/ecatappl.c` 后自动使用真实协议栈）；
- `SSC/StubSrc/`：离线编译校验用的 **协议栈 API 桩**，使工程在未生成 SSC 代码时也能编译、链接、运行（仅初始化 ESC 硬件，**不能真正通信**）；
- `SSC/Config/HPM_ECAT_DRONE_Config.xml` + `SSC/ESI/HPM_ECAT_DRONE.xml` + `SSC/ecat_drone.xlsx`：SSC Tool 配置文件、ESI 与 PDO/CoE 描述（与 `inc/ecat_pdo.h`、`inc/param.h` 严格一致，生成器带自检）。

**生成真实协议栈的步骤**（详见 `SSC/README.md`）：

1. 从 [ETG 下载页](https://www.ethercat.org/en/downloads.html) 下载并安装 SSC Tool（需 ETG 会员账号 + Vendor ID）；
2. `Tool -> Options -> Configurations` 导入 `SSC/Config/HPM_ECAT_DRONE_Config.xml`；
3. `File -> New -> Custom`，选择 HPMicro ECAT Drone Powertrain 配置，保存工程；
4. `Tool -> Application -> Import` 导入 `SSC/ecat_drone.xlsx`；
5. `Project -> Create new Slave Files`，输出目录指向本工程 `SSC/Src`；
6. 重新构建，CMake 会自动切换到真实协议栈（日志显示 `Using Beckhoff SSC slave stack from SSC/Src`）。

> HPM5E00 硬件支持 SYNC0/SYNC1 不触发 PDI 中断，无需 `ssc_pdi_mask.patch`。

### 4.2 TwinCAT / 主站准备

1. 把 ESI 文件（`SSC/ESI/HPM_ECAT_DRONE.xml`）复制到 `C:\TwinCAT\3.1\Config\Io\EtherCAT`；
2. 主站扫描从站，首次使用需 `EEPROM Update` 写入 ESI（本工程支持 Flash 模拟 EEPROM，上电自动初始化，见 `ecat_config.h`）；
3. PDO 长度：RxPDO 22 字节 / TxPDO 48 字节（见第 5 节与 `inc/ecat_pdo.h`）。

---

## 5. 过程数据（PDO）定义

完整定义见 `inc/ecat_pdo.h`，摘要：

### RxPDO（主站 → 从站，0x1600，22 字节）

| 偏移 | 类型 | 名称 | 说明 |
|---|---|---|---|
| 0 | u16 | control_word | bit0 运行/使能，bit1 清故障，bit2 急停（=1 停止），bit3 舵机输出使能，bit4 DShot 3D 模式 |
| 2 | u16[2] | esc_cmd_us | 电机 1/2 油门，1000~2000（us 等效值） |
| 6 | i16[2] | servo_angle_d10 | 舵机 1/2 期望角度，单位 0.1° |
| 10 | u16 | servo_ctrl_word | bit0 舵机1 使能，bit1 舵机2 使能，bit2 清舵机故障 |
| 12 | u16[2] | dshot_cmd | 直接下发 DShot 特殊命令（如 12=保存设置），0=不发送 |
| 16 | u16 | reserved0 | 保留 |
| 18 | u32 | cycle_counter | 主站写计数器（用于通信看门狗） |

### TxPDO（从站 → 主站，0x1A00，48 字节）

| 偏移 | 类型 | 名称 | 说明 |
|---|---|---|---|
| 0 | u16 | status_word | bit0 ECAT OP，bit1 输出使能，bit2 故障，bit3/4 电机1/2 遥测有效，bit5/6 舵机1/2 在线，bit7 ADC 告警 |
| 2 | u16 | fault_code | 故障位（见 `ecat_pdo.h` 的 app_fault_t） |
| 4 | u32[2] | esc_erpm | 双向 DShot 回传电转速（eRPM 原始值，0=停转） |
| 12 | u16[2] | esc_tlm_period_us | 遥测周期原始值 |
| 16 | i16[2] | servo_pos_d10 | 舵机实际角度 0.1° |
| 20 | i16[2] | servo_speed_rpm | 舵机速度 |
| 24 | i16[2] | servo_current_ma | 舵机电流（由扭矩换算的估算值，见舵机文档） |
| 28 | u8[2] | servo_temp_c | 舵机温度 |
| 30 | u8[2] | servo_error | 舵机错误码（S288 MError 低 8 位，完整值见上位机） |
| 32 | u16 | sys_voltage_dv | 系统电压 0.1V |
| 34 | i16 | sys_current_ca | 系统电流 0.01A |
| 36 | u16[2] | esc_current_ma | 电调 1/2 电流 mA |
| 40 | u16 | loop_counter | 控制循环计数 |
| 42 | u16 | led_status | 状态灯当前状态码 |
| 44 | u16 | ecat_link_status | bit0/1 端口链路，bit4-7 AL 状态 |
| 46 | u16 | reserved0 | 保留 |

机械转速换算：`机械 RPM = eRPM × 2 / 极对数`（极对数默认 7，可通过参数 `PARAM_DSHOT_POLE_PAIRS` 修改）。

---

## 6. 上位机（PC 调参工具）

位置：`user_template/tools/ecat_drone_host`，预编译 `build/ecat_drone_host.exe`（也可用 `build.cmd` 重新编译，仅需 Windows 自带 `csc.exe`）。

功能：连接/遥测/参数/无刷电机测试/总线舵机测试/AM32 电调配置/EtherCAT 状态/日志。
协议：USB CDC 虚拟串口，二进制帧（CRC16-CCITT），详见 `inc/host_proto.h`、`docs/USB_PROTOCOL.md` 与 `docs/HOST_TOOL.md`。

```bat
cd /d D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\tools\ecat_drone_host
build.cmd              :: 编译（csc.exe）
build.cmd -Selftest    :: 编译并自检（43/43）
run.cmd                :: 运行
```

---

## 7. 状态灯含义

| 颜色/动画 | 状态码 | 含义 |
|---|---|---|
| 白色呼吸 | BOOT | 上电启动 |
| 蓝色闪烁 | SELF_TEST | 外设自检 |
| 黄色慢闪 | ECAT_WAIT | 等待主站 / EEPROM 初始化 |
| 黄色双闪 | ECAT_PREOP | EtherCAT PRE-OP |
| 青色闪烁 | ECAT_SAFEOP | EtherCAT SAFE-OP |
| 绿色慢呼吸 | OP_IDLE | EtherCAT OP，但主站未使能输出 |
| 绿色常亮 | RUN | 运行（输出使能） |
| 红色闪烁 | FAULT | 故障（见故障码） |
| 蓝色脉冲 | USB_ACTIVE | 上位机已连接 |

---

## 8. Bring-up 建议顺序

1. 只烧录固件、接 UART0（115200），确认启动日志、时钟频率、板级初始化成功；
2. 用上位机连接 USB，确认 PING/版本/参数读写/状态灯控制正常；
3. 单独测试状态灯（红/绿/蓝/白）；
4. 测试 ADC：万用表对比系统电压读数，标定 `PARAM_ADC_SYS_V_*` 等参数；
5. 示波器看 PF02/PF03 DShot 波形（idle 高、16 bit 帧、反相 CRC），再接 AM32 电调测试转向/油门/回传 eRPM；
6. 用上位机 AM32 页面读取/写入电调参数（需电机停转、ECAT 未进 OP；写入走 DShot 编程模式并回读校验）；
7. 测试总线舵机（先 1 个舵机，确认 6 Mbps 波形和方向切换时序，再上两个）；
8. 最后接入 EtherCAT 主站：先用 SSC Tool 生成协议栈并确认 EEPROM 初始化日志，再进 OP 测 PDO。
9. 更详细的硬件检查清单见 `docs/VERIFICATION.md` 第 9 节 / 各模块文档的“硬件验证”章节。

---

## 9. 已知限制与注意事项

1. **SSC 协议栈未包含**：不生成 SSC Tool 代码时，EtherCAT 不能真正通信（但其他功能全部可用）。
2. **未在真实硬件上验证**：本仓库为软件交付，DShot/舵机/ADC/AM32 时序需按第 8 节在硬件上验证与微调。
3. **HPM5E3Y SoC 支持**：hpm_sdk v1.13.0 未提供 HPM5E3Y 目录的构建文件（`system.c`/链接脚本等），板级包使用 `soc: HPM5E31`（两者头文件逐字节一致）。如需严格使用 HPM5E3Y，运行 `scripts/add_hpm5e3y_soc_support.ps1` 并把板级 yaml 的 `soc:` 改为 `HPM5E3Y`。
4. **内部 MII PHY**：PA16–PA29 的模拟引脚、PW16/PW17（PHY LDO）、PV12/PW12（PHY 复位）配置依据《HPM5E00 片内 MII PHY 使用指南 v1.1》；PHY MDIO 地址沿用 EVK 约定（1/2），若实板 strap 不同需调整 `board.h` 的 `BOARD_ECAT_PHY_*`。
5. **PD00 复用**：PD00 原为 MCAN0_TXD，本板用作 UART3 舵机方向切换，MCAN0 不可同时使用。
6. **舵机协议**：S288 协议无寄存器读写/改 ID 命令，相关上位机功能显示为“不支持”；协议细节见 `docs/SERVO_S288.md`。
7. **AM32 写入路径**：4-way 桥实际只能读（ESC 擦写 ACK > 1 s，超出桥超时预算）；写入走 AM32 DShot 编程模式，需 ESC 上电、电机停转、ECAT 未进 OP。
