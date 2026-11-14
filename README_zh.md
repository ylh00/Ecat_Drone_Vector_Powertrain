# Ecat_Drone_Vector_Powertrain — 交付说明

本目录包含一套完整的 **HPM5E3Y EtherCAT 无人机矢量动力总成从站** 软件交付：
板级支持包（BSP）、应用工程、PC 上位机、设计文档与验证脚本。

> 芯片：**HPM5E3YIGN1**（内置 1MB Flash + 2× 片内 MII PHY）
> SDK：hpm_sdk v1.13.0（`sdk_env_v1.13.0/hpm_sdk`）
> 板级引脚来源：`pin_mux/pinmux.c`（已按《HPM5E00 片内 MII PHY 使用指南》修正并固化到板级包）

---

## 1. 交付内容（全部位于 sdk_env_v1.13.0\user_template）

| 路径 | 内容 |
|---|---|
| `user_template\boards\ecat_drone_5e3y\` | **板级支持包**：board.h/.c、clock.c/h、pinmux.c/h、yaml、OpenOCD cfg、CMakeLists、README_zh.rst |
| `user_template\projects\ecat_drone_powertrain\` | **EtherCAT 从站工程**：EtherCAT 协议栈胶水、SSC 配置文件/ESI/离线编译桩、DShot、舵机、LED、ADC、USB、参数系统、上位机协议、回归测试脚本（`tests/`） |
| `user_template\tools\ecat_drone_host\` | **PC 上位机**（C# WinForms，免安装、免依赖，`csc.exe` 编译）+ 源码 + 自检 |
| `user_template\scripts\` | `build.cmd`（一键构建）、`build.ps1`、`check_syntax.py`、`add_hpm5e3y_soc_support.ps1` |
| `user_template\README_zh.md` | user_template 使用说明 |
| `user_template\projects\ecat_drone_powertrain\README_zh.md` | 工程说明（构建/烧录/PDO/上位机/bring-up） |
| `user_template\projects\ecat_drone_powertrain\docs\` | 架构、引脚表、PDO/ECAT/SSC、舵机协议、DShot/AM32、USB 协议、上位机、**硬件事实摘要（PDF 提取）**、**验证报告** |
| `.work\`（工作目录，非交付） | PDF 全文/提取工具、构建目录等分析中间产物 |

## 2. 快速开始

```bat
:: 1) 构建固件
cd /d D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\scripts
build.cmd

:: 2) 构建上位机
cd /d D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\tools\ecat_drone_host
build.cmd
run.cmd
```

烧录、EtherCAT 主站配置（TwinCAT + SSC Tool 生成协议栈）、bring-up 步骤见工程 README。

## 3. 关键设计决策（请先阅读）

1. **EtherCAT 协议栈授权**：hpm_sdk 不含 Beckhoff SSC 协议栈源码。工程默认用 `SSC/StubSrc`
   的 API 桩保证可编译/可链接/可运行，**真正 EtherCAT 通信需要按 `SSC/README.md` 用 SSC Tool
   生成一次协议栈代码**（生成后构建系统自动切换，无需改代码）。
2. **片内 MII PHY**：本板的 ESC0 使用 HPM5E3Y 内置的两路 MII PHY（PV/PW 内部 IO），
   PV12/PW12 为 PHY 复位，PA16–PA29 为 PHY 模拟信号，PA25/PA28 为链路状态输入。
   ECAT HAL 已按《片内 MII PHY 使用指南》适配，PHY 地址沿用 EVK 约定（1/2）。
3. **HPM5E3Y SoC 构建**：hpm_sdk v1.13.0 未提供 HPM5E3Y 目录的构建文件（system.c/链接脚本等），
   板级包使用 `soc: HPM5E31`（同 IP，头文件逐字节一致），器件名标注为 HPM5E3YIGN1。
   如需严格使用 HPM5E3Y 目录，运行 `scripts/add_hpm5e3y_soc_support.ps1` 并改 yaml。
4. **舵机协议**：宇树 J288/S288 为 6 Mbps 单总线半双工，20B 控制帧 / 26B 反馈帧，CRC32；
   无寄存器/改 ID 指令（上位机相应功能返回“不支持”）。方向切换由 PC16/PD00 控制，有效电平可参数配置。
5. **PD00 复用**：PD00 原为 MCAN0_TXD，本板用作 UART3 舵机方向切换，MCAN0 不可用。
6. **双向 DShot**：PWMV2 输出（反相 idle 高、反相 CRC）+ GPTMR1 边沿捕获回传，
   解码链：21bit 差分线序 → 20bit GCR → 16bit（12bit eRPM+4bit 反相 CRC）→ eRPM。
7. **AM32 调参**：读参数走 4-way interface（19200 8N1 位操作桥）；写参数走 AM32 DShot
   编程模式（0x54/0x55，~9 ms/字节 + 保存），写完用 4-way 读回校验（4-way 桥实际只读，
   因为 ESC 擦写 ACK 可能 >1 s，超出桥超时预算）。

## 4. 验证状态

| 项目 | 状态 |
|---|---|
| 固件编译（全部模块，`-Wall -Wundef`） | ✅ 通过，0 warning / 0 error（`scripts\build.cmd clean` 全新构建） |
| 固件链接（SSC 桩 + 完整应用） | ✅ 通过：`output/ecat_drone_powertrain.elf`，FLASH 156,332 B（占 1MB 的 14.91%，远低于 0x7E000=504KB 上限） |
| 上位机编译 + 协议自检 | ✅ 通过：`build\ecat_drone_host.exe`（0 编译警告），`--selftest` 43/43 PASS，`--uitest` PASS |
| 双向 DShot 收发算法仿真 | ✅ 通过：4096/4096 遥测载荷解码，±5% ESC 时钟+抖动下 0 错误解码，损坏帧全部拒收 |
| 舵机帧构造/解析单元测试 | ✅ 通过：58/58（CRC32 与厂商实现 600 随机帧比对一致、20B/26B 帧字节级校验） |
| USB 协议一致性测试 | ✅ 通过：238/238 检查（30 条命令/60 个参数/帧 CRC/长度/AM32 编程命令覆盖） |
| EtherCAT PDO/CoE 一致性 | ✅ 静态校验通过（22B/48B、60 个 CoE 对象类型与参数表一致）；真实协议栈需 SSC Tool 生成后联调 |
| 硬件实机验证 | ⚠️ **未进行**（无实物板）；bring-up 步骤见工程 README 第 8 节、各模块 docs 的“硬件验证”章节 |

详细的验证证据（命令、输出、代码审查发现）见
`user_template\projects\ecat_drone_powertrain\docs\VERIFICATION.md`；
硬件事实（来自数据手册/用户手册/PHY 指南/舵机手册，逐条页码引用）见
`user_template\projects\ecat_drone_powertrain\docs\HARDWARE_FACTS.md`；
回归测试脚本见 `user_template\projects\ecat_drone_powertrain\tests\`。

## 5. 后续工作（按优先级）

1. 用 Beckhoff SSC Tool 生成协议栈 → 实测 EtherCAT 通信与 TwinCAT PDO 映射；
2. 硬件实测并标定 ADC 参数（电压/电流比例）、确认 PHY MDIO 地址与链路极性；
3. 硬件实测双向 DShot 时序（示波器）与 AM32 回传解码，必要时微调 `PARAM_DSHOT_*`；
4. 硬件实测 S288 6 Mbps 波形、方向切换时序与 CRC 覆盖范围；
5. 上位机根据实测补充 AM32 EEPROM 参数解码（当前按 AM32 官方字段表实现，出厂值需实机读取）。
