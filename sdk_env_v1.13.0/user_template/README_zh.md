# HPM SDK 用户模板 — ecat_drone_powertrain 交付说明

本目录是在原版 `sdk_env_v1.13.0/user_template`（board / project 自定义模板）基础上的完整交付，
包含：

```
user_template/
├── boards/ecat_drone_5e3y/            # 新板级支持包（HPM5E3Y 动力总成板）
├── projects/ecat_drone_powertrain/    # EtherCAT 从站应用工程
├── tools/ecat_drone_host/             # PC 上位机（C# WinForms）
├── scripts/                           # 构建/检查/补丁脚本
└── README_zh.md                       # 本文件
```

## 1. 快速开始

### 1.1 构建固件

```bat
cd /d <本目录>\scripts
build.cmd
```

脚本自动设置 `HPM_SDK_BASE` / `GNURISCV_TOOLCHAIN_PATH` / `PATH`（与 `start_cmd.cmd` 相同），
不需要先打开 SDK prompt。产物在
`projects\ecat_drone_powertrain\build_ecat_drone_5e3y_flash_xip_debug\output\ecat_drone_powertrain.elf`。

如果 PowerShell 执行策略限制，`build.cmd` 内部已使用 `-ExecutionPolicy Bypass`，可直接运行。

### 1.2 构建上位机

```bat
cd /d <本目录>\tools\ecat_drone_host
build.cmd            :: 生成 build\ecat_drone_host.exe（使用系统自带 csc.exe，无需 .NET SDK）
build\ecat_drone_host.exe --selftest    :: 协议自检
```

### 1.3 start_gui 使用

1. 启动 `sdk_env_v1.13.0\start_gui.exe`；
2. 勾选 **Enable User Board Path** → 选择 `<本目录>\boards`；
3. 勾选 **Enable User Application Path** → 选择 `<本目录>\projects`；
4. Board 选择 `ecat_drone_5e3y`，Sample 选择 `ecat_drone_powertrain`，Build Type 选择 `flash_xip`；
5. Generate → Compile / Open with IDE / Flash 下载。

> 板级包 `ecat_drone_5e3y/` 目录内包含与目录同名的 `.cfg`（`ecat_drone_5e3y.cfg`），
> start_gui 会自动把它作为 OpenOCD 板级配置文件；同时包含了与目录同名的 `.yaml` 和 `CMakeLists.txt`，
> 满足自定义 board 的识别条件。

## 2. 板级包说明

`boards/ecat_drone_5e3y/` 与 SDK 自带 `boards/hpm5e00evk/` 结构一致：

| 文件 | 作用 |
|---|---|
| `ecat_drone_5e3y.yaml` | SoC（`HPM5E31`，见注意事项）、器件名、OpenOCD SoC/probe、片内 Flash 1MB、板级特性 |
| `CMakeLists.txt` | 把 pinmux/clock/board 加入 `hpm_sdk_lib`，并定义片内 MII PHY 编译宏 |
| `board.h` | **板级契约**：所有外设/引脚宏 + 初始化函数原型 |
| `board.c` | 板级初始化：时钟、串口、引脚、延时、1ms 定时器、ECAT 硬件（PHY 复位） |
| `clock.c/.h` | 时钟树：CPU 480MHz、UART0 48MHz、UART2/3 48MHz（舵机 6Mbps 精确分频）、PWM/ADC/USB/ESC 时钟 |
| `pinmux.c/.h` | 引脚复用（来自用户 `pin_mux/pinmux.c`，按片内 MII PHY 指南修正） |
| `ecat_drone_5e3y.cfg` | OpenOCD 下载/复位配置（片内 XPI NOR 1MB） |
| `README_zh.rst` | 板级说明（引脚表、时钟、EtherCAT 注意事项） |

### 2.1 注意事项：HPM5E3Y 与 HPM5E31

hpm_sdk v1.13.0 提供了 `soc/HPM5E00/HPM5E3Y/` 的全部**头文件**（与 HPM5E31 逐字节一致），
但**没有**提供 HPM5E3Y 的构建文件（`system.c`、`hpm_clock_drv.c`、链接脚本、boot header、CMakeLists）。
因此板级 yaml 中 `soc: HPM5E31`，芯片实际型号通过 `device: HPM5E3YIGN1` 标注，两者为同一 IP 的合封型号：
- HPM5E3Y = 内置 1MB Flash + **2× 片内 MII PHY**；
- HPM5E31 = 无内置 PHY（EVK 用外置 JL1111）。

如确需构建在 HPM5E3Y 目录下（例如把 HPM5E3Y 目录补全为独立 SoC），运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\add_hpm5e3y_soc_support.ps1
```

脚本会把 HPM5E31 的构建文件复制到 HPM5E3Y 目录（不覆盖已有文件），然后把
`boards/ecat_drone_5e3y/ecat_drone_5e3y.yaml` 的 `soc:` 改为 `HPM5E3Y` 即可。

## 3. 工程说明

`projects/ecat_drone_powertrain/` 参照 `hpm_sdk/samples/ethercat/ecat_io` 的结构：

- `ecat.c`：协议栈启动胶水（SSC 风格，与 ecat_io 相同）；
- `ecat_config.h` / `user_config.h`：EtherCAT 硬件配置（EEPROM 模拟地址、定时器）；
- `port/`：ESC 硬件抽象层（`hpm_ecat_hw.c`、`hpm_ecat_phy.c`、`hpm_ecat_e2p_emulation.c`，来自 `samples/ethercat/port` 并适配片内 MII PHY）；
- `SSC/`：SSC Tool 输入文件（配置 XML、ESI、PDO 描述、README）+ `StubSrc/` 离线编译桩；
- `inc/` + `src/`：应用层（PDO 定义、参数、DShot、舵机、LED、ADC、USB、主机协议）；
- `docs/`：PDO/引脚/舵机/DShot/AM32/USB 协议/上位机等设计文档。

详见 `projects/ecat_drone_powertrain/README_zh.md`。

## 4. 上位机说明

`tools/ecat_drone_host/`：C# WinForms 单文件可执行程序（.NET Framework 4.x，系统自带 csc.exe 编译），
零第三方依赖。功能：连接/遥测/参数编辑/无刷电机 DShot 测试/总线舵机测试/AM32 电调配置/
EtherCAT 状态/日志。详见 `tools/ecat_drone_host/README_zh.md`。

## 5. 脚本说明

| 脚本 | 作用 |
|---|---|
| `scripts/build.cmd` | 一键构建（cmd，推荐） |
| `scripts/build.ps1` | 一键构建（PowerShell，build.cmd 内部调用） |
| `scripts/check_syntax.py` | 单文件语法检查（读 `compile_commands.json`，模块开发用） |
| `scripts/add_hpm5e3y_soc_support.ps1` | 可选：把 HPM5E31 构建文件补到 HPM5E3Y SoC 目录 |

## 6. 修改指南

- **改引脚**：修改 `boards/ecat_drone_5e3y/pinmux.c`，并同步 `board.h` 中对应 `BOARD_*` 宏；
- **改外设实例/通道**：只需改 `board.h` 宏（应用代码不直接写寄存器实例）；
- **改 PDO 布局**：修改 `inc/ecat_pdo.h`（并同步 `SSC/` 的 ESI/Config/Excel 和上位机 `Protocol/BoardData.cs`），
  注意结构体大小断言会在编译期检查；
- **改参数**：修改 `inc/param.h`（id/范围/默认值）+ `src/param.c` 的参数表；
- **换芯片型号**：修改板级 yaml 的 `soc:`/`device:`，并按需调整 pinmux 与 board.c 中的外设；
- **换 PHY 方案（外置 JL1111）**：把 `board.h` 的 `BOARD_ECAT_PHYx_RESET_*`、`BOARD_ECAT_NMII_LINKx_CTRL_INDEX`、
  `BOARD_ECAT_SUPPORT_PORT2` 按 EVK 配置，并在 board yaml 的 CMakeLists 中关闭 `BOARD_ECAT_USE_INTERNAL_PHY`。
