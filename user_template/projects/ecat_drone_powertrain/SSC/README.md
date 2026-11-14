# EtherCAT 从站协议栈（SSC）生成与集成说明 — ecat_drone_powertrain

本目录包含 **Beckhoff SSC Tool 的输入文件**、**手写的 ESI 设备描述**、以及在本工程
**没有 SSC 协议栈源码时**用于编译/链接验证的 API 桩（StubSrc）。

> **重要**：Beckhoff SSC（EtherCAT Slave Stack Code）受许可限制，**不随 hpm_sdk
> 发布**。要让本板真正作为 EtherCAT 从站通信，必须由用户（ETG 会员）用 SSC Tool
> 生成协议栈源码，放入本目录的 `Src/`。未生成时工程使用 `StubSrc/` 编译通过并能
> 运行应用层，但 **没有任何 EtherCAT 通信**（详见第 4 节）。

---

## 1. 文件清单

| 路径 | 作用 | 来源 |
|---|---|---|
| `Config/HPM_ECAT_DRONE_Config.xml` | SSC Tool 的 **Configuration**（协议栈设置：2 端口 MII、32 位访问、2 KB 模拟 EEPROM、DC、CoE、PDO 上限等） | 本工程生成（`tools/gen_ssc.py`） |
| `ecat_drone.xlsx` | SSC Tool 的 **Application** 描述（对象字典：0x2000+ 参数对象、0x6xxx TxPDO 变量、0x7xxx RxPDO 变量；PDO 映射对象自动生成） | 本工程生成（`tools/gen_ssc.py`） |
| `ecat_drone_application.csv` | 与 xlsx 相同内容的可读表格；xlsx 导入失败时的兜底录入依据 | 本工程生成 |
| `ESI/HPM_ECAT_DRONE.xml` | 设备描述文件（TwinCAT 用）：`Device Physics="YY"`（2 端口）、RxPDO 22 B / TxPDO 48 B、CoE 参数对象 | 本工程（以 ecat_io 的 ESI 为模板，由脚本生成） |
| `StubSrc/` | SSC API 桩：`ecat_def.h`、`ecatappl.h`、`ecatslv.h`、`esc.h`、`applInterface.h`、`ecatcoe.h` + `ssc_stub.c` | 本工程手写（编译验证用，见第 4 节） |
| `Src/` | **由 SSC Tool 生成的真实协议栈源码**（当前为空） | 用户生成后拷入 |
| `tools/gen_ssc.py` | 重新生成 Config XML / ESI / xlsx / CSV 的脚本（Python **标准库**，无 openpyxl、无网络），并自带参数类型自检 | 本工程 |
| `../ecat.c`、`../ecat_config.h`、`../user_config.h` | SSC 启动胶水（`MainInit`/`MainLoop`/EEPROM 回调/映射长度）+ 工程级 ESC 配置 | 本工程 |
| `../src/ecat_app.c` | **手写的 SSC 应用层**：APPL_* 回调、PDO 拷贝、AL 状态、CoE 参数对象读写钩子 | 本工程 |
| `../port/` | ESC HAL：`hpm_ecat_hw.c/.h`、`hpm_ecat_phy.c/.h`、`hpm_ecat_e2p_emulation.c/.h` | 由 `hpm_sdk/samples/ethercat/port` 适配 |

---

## 2. 准备

1. **SSC Tool 5.13**（Config File Vers 1.5.3.0）
   - 从 ETG 下载页 <https://www.ethercat.org/en/downloads.html> 使用 ETG 会员账号 +
     有效 Vendor ID 下载；本文件记录的版本与 hpm_sdk v1.13.0 的 ecat_io 示例一致。
2. **TwinCAT 3.1**（建议 Build 4024.56 或更新）用于主站测试。
3. 本工程 ESC 使用 **HPM5E3Y 片内 2 个 MII PHY**（SIP），所以：
   - SSC 配置为 **2 端口**（`BOARD_ECAT_SUPPORT_PORT1=1`、`PORT2=0`）；
   - ESI 的 `Device Physics="YY"`；
   - `BOARD_ECAT_PHY_ADDR_OFFSET=1`、port0 PHY=0、port1 PHY=1（见 `boards/ecat_drone_5e3y/board.h`）。
4. ESC EEPROM 用**内部 Flash 模拟**（无外挂 EEPROM）：Flash 0x80000 起 64 KB 区域，
   对应 2 KB EEPROM 镜像；参数区在 0x7E000（见 `ecat_config.h` / `inc/param.h`）。

---

## 3. 用 SSC Tool 生成协议栈（精确步骤）

> 前置：先建好空目录 `SSC/Src/`。SSC Tool 只负责生成文件，目录不存在会报错。

1. 打开 SSC Tool → **Tool → Options → Configurations → Add**，
   选择 `SSC/Config/HPM_ECAT_DRONE_Config.xml`，确认列表出现
   **`HPMicro ECAT Drone Powertrain`**。
2. **File → New → Custom**，在下拉框中选择 **`HPMicro ECAT Drone Powertrain`**。
3. 保存 SSC 工程文件（例如 `SSC/HPM_ECAT_DRONE.ssc`）。
4. **Tool → Application → Import**，选择 `SSC/ecat_drone.xlsx`，点击 **OK**。
   - 本工程约定应用名为 `ecat_drone`，因此生成的**应用头/源文件名为
     `ecat_drone.h` / `ecat_drone.c`**；`Config/HPM_ECAT_DRONE_Config.xml` 中的
     `APPLICATION_FILE` 已设置为 `#include "ecat_drone.h"`。
5. **Project → Create new Slave Files**：
   - **Src 输出目录**：`<工程目录>/SSC/Src`（若输出到临时目录，把协议栈源码手工拷入
     `SSC/Src`）；
   - **ESI 输出目录**：任意（建议 `SSC/ESI/generated`）。仓库中的
     `SSC/ESI/HPM_ECAT_DRONE.xml` 已按 `inc/ecat_pdo.h` 的字段顺序手写/生成，
     **测试请优先使用它**；SSC Tool 生成的 ESI 可用来交叉检查 PDO 条目。
   - 点击 **Start**，生成协议栈代码 + ESI + `eeprom.h`（EEPROM 内容）。
6. **不需要**打 `ssc_pdi_mask.patch`：
   - `ecat_io` 示例需要该补丁，是因为旧芯片 SYNC0/SYNC1 事件会映射到 PDI 中断；
   - HPM5E00 支持 `HPM_IP_FEATURE_ESC_SYNC_IRQ_MASK`，`port/hpm_ecat_hw.c` 已调用
     `esc_enable_sync_irq_to_pdi_irq(esc, false, false)`，因此无需补丁。

### 3.1 把生成结果集成到本工程（与 ecat_io 示例不同，必读）

本工程的应用层是**手写**的（`src/ecat_app.c`），不是 SSC Tool 生成的
`ecat_drone.c`。这样做的原因：

- PDO 数据是固定 22 B / 48 B 的打包结构体（`inc/ecat_pdo.h`），直接 `memcpy`，
  不需要生成代码里的对象变量映射；
- 参数对象 0x2000+ 需要在读写时调用 `param_get()/param_set()`，由手写钩子
  `ecat_coe_obj_read()/ecat_coe_obj_write()` 完成（xlsx 的 `CoeRead`/`CoeWrite`
  列已把两个对象绑定到这两个函数）。

集成步骤：

1. 把 SSC Tool 生成的 **协议栈源码** 拷入 `SSC/Src/`：
   `coeappl.c/h`、`ecatappl.c/h`、`ecatcoe.c/h`、`ecatslv.c/h`、`mailbox.c/h`、
   `objdef.c/h`、`sdoserv.c/h`、`ecat_def.h`、`esc.h`、`eeprom.h`，以及
   `applInterface.h`（若生成）。
2. 把生成的**应用头文件** `ecat_drone.h` 也拷入 `SSC/Src/`。
   - 协议的 `ecatappl.c` 通过 `APPLICATION_FILE` 引用它；
   - 它包含应用对象字典（`0x2000+` 参数对象 + `0x6xxx/0x7xxx` PDO 变量），其中
     0x2000+ 对象的读写函数在 xlsx 中已指定为 `ecat_coe_obj_read/ecat_coe_obj_write`。
3. **不要**拷贝生成的 `ecat_drone.c`。它与 `src/ecat_app.c` 重复定义
   `APPL_*` 回调（CMake 只编译 `src/ecat_app.c` 和 `SSC/Src` 下固定的协议栈源文件，
   见 `CMakeLists.txt`）。
   - 如需保留生成的 `ecat_drone.c` 作参考，可改名为 `ecat_drone.c.reference`。
4. 若生成的 `ecat_drone.h` 里出现本项目不存在的对象变量或额外 `APPL_*` 原型声明，
   按编译器提示删掉对应行即可；PDO 拷贝不依赖这些变量。
5. 重新执行 CMake configure：
   - 检测到 `SSC/Src/ecatappl.c` 后 CMake 自动定义 `APP_ECAT_SSC_REAL_STACK=1`、
     编译真实协议栈，并**不再**编译 `SSC/StubSrc/ssc_stub.c`；`SSC/Src` 在 include
     路径中优先于 `SSC/StubSrc`，同名头文件自动切换。
6. 重新编译、烧录。上电日志应出现：
   ```
   [I][APP/ECAT] EtherCAT slave init (ESC + SSC stack)
   EEPROM loading successful, no checksum error.
   [ECAT] PHY port0: MDIO address ...
   [I][APP/ECAT] process data: TxPDO 48 bytes, RxPDO 22 bytes
   [I][APP/ECAT] AL state -> 2 ...
   ```

> 注：真实的 SSC 生成代码无法在本仓库的 CI/开发环境中验证（SDK 不提供协议栈源码）。
> 若编译器报告的 `APPL_*` 回调集合与 `src/ecat_app.c` 不一致，把缺失的函数按
> `docs/ECAT_SSC.md` 第 5 节的回调清单补齐即可（全部回调都已有实现，个别仅为
> 名称差异）。

---

## 4. 没有 SSC 协议栈时的构建（当前默认）

当 `SSC/Src/ecatappl.c` 不存在时（当前状态），CMake 会打印警告并编译
`SSC/StubSrc/ssc_stub.c`：

- 提供 `MainInit/MainLoop/PDI_Isr/Sync0_Isr/Sync1_Isr/ECAT_CheckTimer`、
  `bRunApplication`、`nPdInputSize/nPdOutputSize`、EEPROM 回调指针等 SSC 符号，
  使整个工程**可编译、可链接、可烧录、可运行**；
- **不实现** EtherCAT 通信：无邮箱、无 SDO/PDO 交换、无 DC、无看门狗；
- 默认 AL 状态停在 INIT（无主站时 ESC 本来就是 INIT）；
- 调试辅助 `ECAT_SSC_STUB_TEST_MODE`（`ecat_config.h`，默认 **0**）：
  置 1 后 stub 会以 500 ms 间隔仿真 `INIT→PREOP→SAFEOP→OP→SAFEOP→…` 的状态迁移并
  调用应用回调、驱动 `g_ecat_op_active`，用于在没有主站时**冒烟测试**
  `app_task.c` 的状态机 / 输出使能逻辑。**台架测试时请断开电机或断开 ESC 供电**。
- 该模式下 PDO 拷贝函数也会被调用（影子缓冲区，不连网络），用于验证 22/48 B 的
  拷贝路径。

---

## 5. TwinCAT 3 快速验证

1. 把 `SSC/ESI/HPM_ECAT_DRONE.xml` 复制到
   `C:\TwinCAT\3.1\Config\Io\EtherCAT\`（或通过 TwinCAT 的 ESI 管理导入），重启
   TwinCAT（或 TwinCAT → EtherCAT → Reload Device Descriptions）。
2. File → New → TwinCAT Project → 选择网卡 → **Scan**（选择与板子 port0 相连的网卡）。
3. 首次扫描通常 EEPROM 内容为空/不匹配：在设备上右键 **EEPROM Update**，选择
   `HPM_ECAT_DRONE` ESI 更新；然后**重新上电**、删除设备重新扫描。
4. 检查 PDO 尺寸：
   - RxPDO（主站输出，0x1600）= **22 字节 / 10 项**：
     control_word、esc_cmd_us1/2、servo_angle1/2、servo_ctrl_word、
     dshot_cmd1/2、reserved0、cycle_counter；
   - TxPDO（主站输入，0x1A00）= **48 字节 / 24 项**（status_word、fault_code、
     eRPM、舵机状态、ADC、loop_counter、link 状态等，见 `inc/ecat_pdo.h`）。
5. 设置 DC（可选）：TwinCAT 从站 Sync Mode 选择 DC-Synchron 或 SM-Synchron；
   本工程默认 1 kHz 控制循环，建议 DC Sync0 周期 1 ms。
6. 进入 OP 后（控制字 bit0 RUN=1、bit2 急停=0）主站可控制 2 路 ESC 与 2 路舵机；
   TxPDO 会持续刷新。CoE 在线参数：对象 **0x2000 … 0x203B**（对应
   `inc/param.h` 的 60 个参数，索引 = 0x2000 + 参数 id，子索引 0，读写；
   数据类型见 `src/param.c` 描述符表）。
7. 诊断：
   - 上电串口日志（UART0 115200）会打印 EEPROM/PHY/AL 状态/看门狗信息；
   - `[ECAT] MDIO scan` 打印每个应答 PHY 的 ID1/ID2/BMSR/link，用于确认 PHY
     strapping（见 `docs/ECAT_HAL_PORT.md` 的已知风险）；
   - 若 AL 停在 INIT：先确认 EEPROM 加载成功、PHY link LED 亮、网线接在 port0；
   - 若 AL 进入 OP 但 RxPDO 不更新：检查 `PARAM_ECAT_WATCHDOG_MS` 与主站任务周期。

---

## 6. xlsx / CSV / ESI 的生成与维护

- 所有 SSC 输入文件由 `tools/gen_ssc.py` 生成（Python 标准库：
  `zipfile` + `xml.etree`，**不需要 openpyxl，不需要网络**）：
  ```powershell
  D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\tools\python3\python.exe `
      SSC\tools\gen_ssc.py
  ```
- 数据来源：
  - CoE 参数对象的**对象索引偏移/顺序**来自 `inc/param.h` 的 `param_id_t` 枚举；
  - CoE 参数对象的**数据类型/描述**来自 `src/param.c` 的 `s_param_desc[]` 描述符表
    （`param_desc_t.type`，**权威来源**；`inc/param.h` 里数组参数的第二个元素没有
    类型注释，因此不能只靠 param.h 判断类型）；
  - PDO：`inc/ecat_pdo.h` 的字段顺序/宽度（本脚本内 `TX_PDO_FIELDS` /
    `RX_PDO_FIELDS` 与之一一对应，改 PDO 时两处都要改）；
  - 设备身份：`inc/app_config.h`（Vendor ID / Product Code / Revision）。
- 脚本内置**自检**：生成后重新解析 ESI / CSV / xlsx，逐对象核对 DataType 与
  BitSize 是否与 `src/param.c` 一致，不一致则退出码 1 并打印差异；正常输出
  `self check OK: 60 CoE parameter objects match src/param.c (ESI, CSV, xlsx)`。
- **参数或 PDO 变更后必须重跑脚本、重新用 SSC Tool 生成协议栈**，否则对象字典
  / ESI 与固件不一致。
- xlsx 的写法：复制 `hpm_sdk/samples/ethercat/ecat_io/SSC/digital_io.xlsx` 的
  workbook/theme/styles 部分，只重写 `xl/worksheets/sheet1.xml` 与
  `xl/sharedStrings.xml`（工作表名仍为 `Profile`）。若将来 SSC Tool 版本对
  xlsx 结构有更严格要求而导入失败，可按 `ecat_drone_application.csv` 的列在
  Excel 里手工录入（列：Index / ObjectCode / SI / DataType / Name / Default Value /
  Min Value / Max Value / M/O/C / B/S / Access / rx/tx / CoeRead / CoeWrite /
  Description），另存为 `SSC/ecat_drone.xlsx` 后导入。
- 注意 xlsx 的 Usage Notes：PDO 映射对象（0x1600/0x1A00）与 SyncManager 分配对象
  （0x1C12/0x1C13）**不需要手工定义**，SSC Tool 会根据 `tx`/`rx` 标记的变量自动
  创建；本工程依赖这一机制，因此 xlsx 中不写映射对象。

---

## 7. 已知风险 / 待硬件验证

1. **PHY MDIO 地址 strapping**：片内 PHY 的 MDIO 地址由 PA28/PA29（PHY0）和
   PA25/PA26（PHY1）的上下拉在复位时锁存，本板原理图未给出明确上下拉，因此
   `port/hpm_ecat_phy.c` 的 `ecat_phy_config()` 采用“先试期望地址→再扫 0..7”的
   容错流程，`ecat_phy_mdio_probe()` 会打印实际应答地址。PHY 配置失败不影响
   EtherCAT 数据通路（MII 直连 + PHY 默认自协商）。
2. **NMII link 极性/索引**：`board.h` 中 `BOARD_ECAT_NMII_LINK0_CTRL_INDEX=1`、
   `BOARD_ECAT_NMII_LINK1_CTRL_INDEX=0`（对应 PA28=ESC0_CTR_1 接 PHY0 LED1、
   PA25=ESC0_CTR_0 接 PHY1 LED1），与片内 MII PHY 指南一致；若 link 显示反了，
   修改 board.h 的 `*_LINK_INVERT`。
3. **SSC 生成代码兼容性**：真实协议栈无法在本仓库环境验证（许可限制），集成后
   若出现缺失回调/头文件差异，按 `docs/ECAT_SSC.md` 的清单处理。
4. ESI 中的 DC 参数（0x1C32/0x1C33 最小周期等）沿用 ecat_io 默认值，可按实际
   1 kHz 控制周期调整。
5. EEPROM 内容由 SSC Tool 生成（`eeprom.h` 的 `aEepromData`，含产品码/版本号）；
   HAL 的 `hpm_ecat_hw.c` 里有弱符号兜底，未生成协议栈时也能通过 EEPROM 加载。
   若升级固件时修改了 Product Code/Revision，HAL 会自动把新内容写入模拟 EEPROM
   （见 `hpm_ecat_e2p_emulation.c` 的检查逻辑）。
6. **对象数据类型以 `src/param.c` 为准**：曾经出现过数组参数第 2 个元素
   （如 `PARAM_SERVO_ANGLE_MIN2`）因为 `inc/param.h` 没有类型注释而被生成成
   `UDINT` 的问题；现在生成器从 `src/param.c` 读取类型并自检 ESI/CSV/xlsx，
   修改参数表后请务必重跑并确认输出中的 `self check OK`。
