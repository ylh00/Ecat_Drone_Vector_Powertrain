# ecat_drone_host — ecat_drone_powertrain 上位机（PC 调参工具）

面向 `ecat_drone_powertrain` EtherCAT 从站板（HPM5E3Y）的 Windows 图形化调参/测试工具。
C# WinForms，**不依赖 dotnet SDK / NuGet / 网络**，使用 Windows 自带的 .NET Framework
`csc.exe` 编译，生成单个 `build\ecat_drone_host.exe`。

固件接口契约以项目头文件与文档为准：

* `projects/ecat_drone_powertrain/inc/host_proto.h` — 帧格式 / 命令号 / 状态码
* `projects/ecat_drone_powertrain/inc/param.h` — 参数表（60 项）
* `projects/ecat_drone_powertrain/docs/USB_PROTOCOL.md` — 固件侧协议全文
* `projects/ecat_drone_powertrain/docs/HOST_TOOL.md` — 本工具的工程化说明

---

## 1. 构建与运行

### 1.1 一键构建（推荐）

```text
build.cmd
```

等价于：

```text
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
```

`build.ps1` 会自动查找 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
（找不到时回退 32 位 Framework），编译 `src\**\*.cs` 到 `build\ecat_drone_host.exe`。
构建脚本不会依赖当前工作目录（`build.cmd` 先捕获 `%~dp0`）。

### 1.2 构建 + 自检

```text
build.cmd -Selftest
```

### 1.3 直接调用 csc（手工方式）

```text
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
%CSC% /nologo /target:winexe /warn:4 /optimize+ /utf8output ^
  /out:build\ecat_drone_host.exe ^
  /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /r:System.Core.dll /r:Microsoft.CSharp.dll ^
  src\*.cs src\App\*.cs src\Protocol\*.cs src\Transport\*.cs src\Ui\*.cs
```

> **关于 `System.IO.Ports.dll`**：.NET Framework 中 `System.IO.Ports.SerialPort`
> 位于 `System.dll`，没有独立的 `System.IO.Ports.dll`。`build.ps1` 在
> `csc.exe` 同目录发现该 facade 时也会自动引用，以兼容其它 Framework 安装。

### 1.4 运行

```text
run.cmd                 启动图形界面
run.cmd --simulate      使用内置模拟设备启动（无需硬件，用于演示/自检）
build\ecat_drone_host.exe --port=COM7     启动后自动连接该串口
```

### 1.5 命令行参数

| 参数 | 说明 |
|---|---|
| *(无)* | 启动 GUI |
| `--simulate` | 使用进程内模拟固件（`FakeFirmwareTransport`）启动，演示全部功能 |
| `--port=COMx` | 启动后自动连接指定串口 |
| `--selftest` | 无硬件自检：CRC/帧编解码/解析器重同步/参数描述符/遥测/AM32/模拟固件端到端/超时重试/日志/界面构建 |
| `--uitest` | 界面运行检查：显示真实主窗口并遍历 8 个标签页（模拟设备），验证 20 Hz 遥测链路；需要桌面会话 |
| `--selftest-log=<文件>` | 自检报告另存路径（默认 exe 同目录 `selftest.log`） |
| `--help` | 帮助 |

自检退出码：`0` = 全部通过，`1` = 存在失败。报告同时写入 `build\selftest.log`。

---

## 2. 功能总览（8 个标签页）

### 2.1 连接

* COM 口枚举/刷新、115200 8N1 连接（USB CDC-ACM 虚拟串口，波特率无关）。
* 连接后自动 `PING`（校验 magic `PING` = 0x474E4950）+ `GET_VERSION`（版本/编译日期）。
* 设备信息、链路统计（请求数/超时/重试/CRC 错误/重同步/应答帧数）。
* 设备操作：PING、读版本、重读参数、保存参数到 Flash、恢复参数默认值、固件复位、进入 DFU。
* “使用内置模拟设备”复选框：无硬件演示全部页面。

### 2.2 遥测（默认 20 Hz）

* 系统电压/电流、ESC1/ESC2 电转速 + 机械转速 + 遥测周期 + 电流（条形指示）。
* 舵机 1/2 位置/速度/电流/温度/错误码。
* EtherCAT AL 状态、端口 0/1 Link、控制循环计数、RGB 状态码。
* `fault_code` / `status_word` 位解码（中文故障名，如“ESC1 无遥测”“系统过压”）。
* 上位机接收帧数、设备 CRC 错误数、数据完整性；原始 54 字节十六进制。
* 顶部状态条实时显示电压/电流/AL/Link/故障。

### 2.3 参数（设备参数表 60 项）

* 分块 `LIST_PARAMS`（每帧 ≤7 条描述符）自动拼接，按模块分组（ECAT/DSHOT/SERVO/ADC/LED/USB/AM32）。
* 分组筛选 + 关键字搜索；列表显示名称、中文名、当前值、范围、单位、说明。
* 双击“当前值”直接编辑：解析十进制/带单位/`0x` 十六进制，越界/只读/非法输入被拦截。
* 详情面板：类型、范围、默认值、说明、新值输入 + 写入按钮。
* “保存到 Flash”（`PARAM_SAVE`，含 `ERR_FLASH` 自动重试一次）、“恢复默认值”（RAM）。
* 参数写入后设备立即 `param_apply()` 生效。

### 2.4 无刷电机（DSHOT）

* ESC1/ESC2 独立：油门滑块 1000–2000 µs、数值显示、发送油门、停止（1000 µs / 0 = 停止）、
  **解锁 1150 µs**、读取单路遥测（`DSHOT_GET_TLM` 26 字节）。
* 实时显示电转速/机械转速/遥测周期/遥测状态/OK-ERR-超时计数/驱动状态。
* DShot 命令按钮：蜂鸣 1–5、保存设置 12、正转 7、反转 8、3D 关/开 9/10、停止 0、
  扩展遥测 13、设置请求 11；可设重复次数（AM32 设置类命令需连续 ≥6 帧），可勾选 0xFF 广播到两个 ESC。
* **台架租约**：固件 `DSHOT_TEST`/`DSHOT_CMD` 会续租 1000 ms，超时自动停止电机；
  “保持台架租约”默认开启，每 400 ms 重发当前油门（不写日志避免刷屏）。
* 安全提示：必须卸桨、EtherCAT 非 OP。

### 2.5 总线舵机

* 总线选择（UART2 舵机1 / UART3 舵机2）、ID 范围、扫描。
* `SERVO_SCAN` 为**异步**命令：固件先回 `ERR_BUSY`，工具自动重试直到 `OK`（位图）或 `ERR_TIMEOUT`（约 10 s）。
* 角度滑块 -360.0°…360.0°（0.1°）+ 数值输入、发送角度（`SERVO_MOVE_TEST`，续租）。
* 读取状态（`SERVO_READ` 20 字节）：位置/速度/电流/电压/温度/错误/在线/使能/计数。
* 修改 ID（`SERVO_SET_ID`）：**当前固件返回 `ERR_UNSUPPORTED`**（S288 总线无寄存器访问），
  工具会给出明确提示；请使用厂家配置工具修改 ID。
* 注意：舵机 ID 必须与参数 `SERVO_ID1/ID2` 一致，否则固件返回 `ERR_PARAM_ID`。
* 20 Hz 遥测中的舵机反馈同步显示。

### 2.6 AM32 调参

AM32 ESC 参数（192 字节 EEPROM）的读取、解码、编辑、写入：

* **读取（权威）**：`HOST_CMD_AM32_BEGIN` 占用 ESC 信号线 → 4-way 原始字节桥接读 EEPROM
  （容器帧 `ESC+CMD+LEN+PARAM+CRC16-XMODEM`，CMD 0x04）→ `AM32_END` 释放。
  支持三种帧模式：引导命令容器（默认）/ BLHeliSuite 4way 接口帧（0x3D/0x3E）/ 原始字节。
* **写入（DShot 编程模式）**：`HOST_CMD_AM32_PROG_WRITE`(0x54) 逐字节写 192 字节
  （每字节约 9 ms，队列满时固件返回 `ERR_BUSY`，工具按 ~12 ms 节奏重试），
  然后 `HOST_CMD_AM32_PROG_SAVE`(0x55) 写 ESC Flash，最后自动用 4-way 回读**逐字节校验**。
  进度条 + 日志显示 `已写 N/192`，可取消（取消后不保存）。
  *原因：ESC 擦写 Flash 的 ACK 可超过 1 s，超出 4-way 桥 ~150 ms 预算，因此写路径必须走 DShot 编程模式。*
* 设置网格：偏移 / 字段名 / 中文名 / 值（可编辑 0..255）/ 解码文本 / 说明；字段来自 AM32
  `Inc/eeprom.h`（`dir_reversed`@17、`motor_poles`@27、`bi_direction`@18、`demag_comp`@14…），
  `tune[128]` 与保留区只读。
* 原始 EEPROM 十六进制视图、导入/导出 `.bin`、退出引导、查询桥接状态。
* 原始 4way 交换终端：输入任意十六进制字节，显示 ESC 原始应答（调试用）。
* 前置条件：ESC 必须电池供电、电机停转、EtherCAT 非 OP（否则 `ERR_NOT_READY`）。

### 2.7 EtherCAT

* 从站状态：AL 状态、端口 0/1 Link、故障码、控制循环计数、ECAT 周期丢失计数、状态字/故障。
* 从站身份（Vendor ID / Product Code / Revision / Serial，来自 `app_config.h`）。
* RxPDO 0x1600（22 B，主站→从站）布局说明表。
* TxPDO 0x1A00（48 B，从站→主站）逐字段当前值（由 54 字节遥测按同一布局重组）+ 48 字节十六进制预览。
* ECAT 参数编辑：节点别名、DC 模式、SYNC0/SYNC1 周期、DC 偏移、看门狗、SDO 超时、CoE 使能、
  状态灯取反；写入后立即生效，可保存到 Flash。
* 说明：过程数据只能由 EtherCAT 主站读写；本页用于核对布局与从站状态，主站配置在 TwinCAT/ESI 侧。

### 2.8 日志

* 带时间戳的协议/操作日志（连接、收/发帧、参数、DShot、舵机、AM32、错误）。
* “显示十六进制原始帧”开关（每帧附带原始字节）、“自动滚动”、“暂停刷新”。
* 保存到文件（`.log`/`.txt`，UTF-8）、复制全部、清空。缓冲上限 20000 条。

---

## 3. 协议参考（PC 侧实现）

### 3.1 帧格式

```text
+------+------+-----+-----+-------+-------+===============+-------+-------+
| 0xA5 | 0x5A | CMD | SEQ | LEN_L | LEN_H |    PAYLOAD    | CRC_L | CRC_H |
+------+------+-----+-----+-------+-------+===============+-------+-------+
```

* 多字节字段小端；`LEN` 0..512；设备回显 `CMD` + `SEQ`。
* `CRC16-CCITT-FALSE`：poly `0x1021`，init `0xFFFF`，不反射，无最终异或，
  覆盖 `CMD..PAYLOAD`（即同步字节之后、CRC 之前）。参考值：`"123456789"` → `0x29B1`。
* 响应 `PAYLOAD[0]` 为状态码；错误时仅 1 字节状态。
* PC 侧：后台接收线程 + 序列号匹配 + 300 ms 超时 + 2 次重试（共 3 次），
  解析器支持 `A5 5A` 重同步（坏 CRC/坏长度只丢 1 字节后重新扫描，统计 CRC 错误与重同步次数）。

### 3.2 状态码

`0x00 OK`、`0x01 未知命令`、`0x02 长度错误`、`0x03 CRC 错误`、`0x04 参数 ID 无效`、
`0x05 参数越界`、`0x06 只读`、`0x07 忙（重试）`、`0x08 未就绪（安全拒绝）`、
`0x09 超时`、`0x0A 不支持`、`0x0B Flash 错误`。

### 3.3 命令与响应（状态字节之后）

| CMD | 命令 | 请求 | 响应 |
|---|---|---|---|
| 0x01 | PING | – | u32 magic(0x474E4950) + u16 major/minor/patch |
| 0x02 | GET_VERSION | – | u16 major/minor/patch + char[16] 日期 |
| 0x03 | GET_TELEMETRY | – | 54 B 遥测（见 3.4） |
| 0x04 | GET_ECAT_STATUS | – | u8 al, u8 link, u16 fault, u32 cycle_miss |
| 0x10/0x11 | GET/SET_PARAM | u16 id（+i32 值） | i32 值 / – |
| 0x12 | GET_PARAM_DESC | u16 id | 70 B 描述符 |
| 0x13/0x14 | PARAM_SAVE/RESET | – | – |
| 0x15/0x16 | PARAM_GET_COUNT / LIST_PARAMS | – / u16 start+u8 count | u16 count / N ≤7 × 70 B |
| 0x20 | DSHOT_TEST | u8 esc, u16 throttle_us（0 或 1000..2000） | – |
| 0x21 | DSHOT_GET_TLM | u8 esc | 26 B DShot 遥测 |
| 0x22 | DSHOT_CMD | u8 esc（0xFF=广播）, u16 cmd | – |
| 0x30/0x31/0x32 | SERVO_SCAN / MOVE_TEST / READ | 见 USB_PROTOCOL.md | 位图 / – / 20 B |
| 0x33/0x34 | SERVO_SET_ID / SERVO_RAW | – | `ERR_UNSUPPORTED` |
| 0x40 | ADC_GET | – | 24 B ADC |
| 0x41/0x42 | LED_SET / LED_TEST | – | – |
| 0x50/0x51/0x52/0x53 | AM32_BEGIN/XFER/END/STATUS | – | 原始 4way 字节 / u8 state+last_error |
| 0x54 | AM32_PROG_WRITE | u8 esc, u8 addr(0..191), u8 value | –（`ERR_BUSY` 时 10–15 ms 后重试同一请求） |
| 0x55 | AM32_PROG_SAVE | u8 esc | – |
| 0x60/0x61 | BOOTLOADER（magic 0x424F4F54）/ RESET | u32 magic / – | 应答后 ~30 ms 复位 |

### 3.4 遥测 54 字节（`host_telemetry_t`）

偏移：status_word@0、fault_code@2、esc_erpm[2]@4（**原始电转速，0=停转**）、
esc_tlm_period_us[2]@12、servo_pos_d10[2]@16、servo_speed_rpm[2]@20、servo_current_ma[2]@24、
servo_temp_c[2]@28、servo_error[2]@30、sys_voltage_dv@32、sys_current_ca@34、
esc_current_ma[2]@36、loop_counter@40、led_status@42、ecat_link_status@44
（bit0/1 端口 Link，**bit4..7 AL 状态**）、host_rx_frames@46、host_crc_errors@50。

机械转速 = `esc_erpm / PARAM_DSHOT_POLE_PAIRS`（默认 7 对极）。

### 3.5 参数描述符 70 字节

`u16 id; u8 type; u8 persist; u8 readonly; u8 len; i32 min; i32 max; i32 def; i32 value;
char name[16]; char desc[32];`（name@22、desc@38，ASCII/UTF-8 NUL 结尾）。

### 3.6 AM32 EEPROM 192 字节

字段来自 AM32 `Inc/eeprom.h`，工具内置完整字段表（偏移/中文名/枚举/说明）。
默认值上游未公开，工具以“模拟默认镜像”演示；**实机必须先读取再修改**。

---

## 4. 典型使用流程

1. **连接**：插 USB → 连接页刷新串口 → 选择 COMx → 连接（自动 PING/版本/参数表）。
2. **看状态**：遥测页确认电压/电流/AL/舵机/ESC 遥测正常，无故障位。
3. **调参数**：参数页读取（自动）→ 修改 → 验证 → “保存到 Flash”。
4. **电机台架测试**：卸桨 → DShot 页解锁 1150 → 滑块加油门 → 观察 eRPM → 停止/全部停止。
   （保持租约默认开启；EtherCAT 主站在 OP 时固件会拒绝）。
5. **舵机测试**：选总线 → 扫描 → 选 ID → 发送角度 → 读取状态。
6. **AM32 配置**：ESC 电池供电、电机停转、ECAT 非 OP → 开始桥接 → 读取 EEPROM →
   在设置网格中修改（如转向/极数/进角）→ **结束桥接** → “写入设置 (DShot 编程模式)” →
   等待进度 192/192 + SAVE + 自动回读校验（约 2–3 秒，勿断电）。
7. **EtherCAT**：确认 AL=OP、双端口 Link；核对 TxPDO 值与主站数据；按需修改 ECAT 参数并保存。
8. **排查**：日志页开启十六进制，保存日志用于分析。

---

## 5. AM32 写入流程细节（DShot 编程模式，命令 0x54 / 0x55）

> 4way 桥接**只能读** EEPROM（197 B ≈ 103 ms 在 150 ms 预算内）；ESC 擦写 Flash 的 ACK
> 可能超过 1 s，所以**写入必须走 DShot 编程模式**（固件把命令转成 ESC 信号线上的
> AM32 编程模式帧）。

**前置条件**（全部满足，否则固件拒绝）：

1. EtherCAT 不在 OP、主站输出关闭；
2. ESC 已上电，且处于“已解锁 + 电机停转”状态（油门 = 0）；
3. 参数 `AM32_ENABLE_PASSTHROUGH`（id 55）= 1；
4. 未占用 4way 桥接（桥接会独占信号线）。

**工具执行序列**：

```text
1) 读取/备份: AM32_BEGIN -> AM32_XFER(4way CMD 0x04 读 192 B) -> AM32_END
   （工具要求先“读取 EEPROM”，并把镜像自动备份到 build\backup\am32_escN_<时间>.bin）
2) addr = 0..191: AM32_PROG_WRITE(0x54) {u8 esc, u8 addr, u8 value}
   - 一次一条请求，每 4 字节让出约 10 ms（每字节约 9 ms）
   - ERR_BUSY(0x07): 命令队列满，等 5..10 ms 后重试**同一请求**（有界重试）
   - ERR_PARAM_RANGE(0x05): esc 或 addr 非法
   - ERR_NOT_READY(0x08): ECAT OP / 输出使能 / 电机未停转 / 4way 占用 → 终止
   - ERR_UNSUPPORTED(0x0A): passthrough 未启用或固件未编译 AM32 调参 → 提示改参数
3) AM32_PROG_SAVE(0x55) {u8 esc}，每 20..50 ms 轮询一次直到 OK
   （BUSY = 最后几字节仍在移位；满 192 B 约 1.8 s）
4) OK 后等待约 50 ms（ESC Flash 写入）
5) 4way 回读 192 B 与写入镜像**逐字节比对**（权威校验）
```

* 进度条显示 `写入 N/192`，日志记录各阶段；可点“取消写入”（已写字节不 SAVE，重新上电恢复）。
* 全过程约 2–3 秒，**期间不要断电**。
* 写入过程中请勿启动 EtherCAT 主站或操作 4way 桥接。
## 6. 自检（无硬件）

```text
build.cmd -Selftest
```

或：

```text
build\ecat_drone_host.exe --selftest
```

覆盖 8 组共 **43 项**检查：

1. CRC16 已知向量（CCITT-FALSE 0x29B1 / XMODEM 0x31C3 / ARC 0xBB3D）与分段一致性
2. 帧编码/解码往返、512 B 边界、拆包/粘包、重同步、非法长度
3. 参数描述符序列化/反序列化、UTF-8 截断、68/70 字节容错、参数表 60 项与 firmware 一致性
4. 54 B 遥测全字段偏移、短包容错、ECAT 状态/版本、DShot/舵机/ADC 数据
5. AM32 EEPROM 字段表覆盖 0..191、编解码、4way 容器帧/接口帧/引导帧
6. 模拟固件端到端：连接/PING/版本、遥测、参数分块读取与读写、DShot、舵机扫描（异步 BUSY 重试）、
   ADC/LED、AM32 4way 读取/写入/接口模式/原始终端/**DShot 编程模式 192 字节 + SAVE + 回读校验**
7. 超时与重试（丢应答、CRC 损坏、无重试超时、未连接拒绝）
8. 日志缓冲与界面主窗口构建

另可运行 `--uitest`（需要桌面）验证真实窗口 + 8 个页签 + 20 Hz 遥测链路。

---

## 7. 故障排查

| 现象 | 处理 |
|---|---|
| 刷新串口后没有 COMx | 换 USB 数据线；检查设备管理器是否有未知设备；安装 CDC 驱动（Windows 10+ 自带 usbser） |
| 连接超时 | 确认板子已上电、串口未被其它工具占用；尝试降低自动遥测频率 |
| `ERR_NOT_READY` | EtherCAT 主站在 OP，或输出使能：先让主站退出 OP；台架测试需电机停止 |
| `ERR_BUSY`（舵机扫描/AM32 写入） | 正常：等待/自动重试；扫描最长约 10 s，写入约 2–3 s |
| 参数保存 `ERR_FLASH` | 工具自动重试一次；仍失败检查 Flash 参数区（0x7E000）是否被程序占用 |
| AM32 回读校验不一致 | 检查 ESC 供电与信号线；重新读取后重写；必要时重启 ESC |
| 遥测 CRC 错误增长 | USB 线质量/干扰；重新插拔；查看日志十六进制定位 |
| `--selftest` 失败 | 查看 `build\selftest.log` 的“失败明细”，属于工具自身缺陷，请反馈 |

---

## 8. 文件结构

```text
tools/ecat_drone_host/
├─ build.cmd              # 一键构建（cmd 包装，调用 build.ps1）
├─ build.ps1              # csc.exe 构建脚本（-Clean / -Selftest）
├─ run.cmd                # 启动 GUI（可传 --simulate 等参数）
├─ selftest.cmd           # 构建好的 exe 上运行 --selftest 并打印报告
├─ README_zh.md           # 本文件
├─ build/                 # 输出：ecat_drone_host.exe、selftest.log、uitest.log
└─ src/
   ├─ Program.cs          # 入口（GUI / --simulate / --selftest / --uitest）
   ├─ Protocol/           # CRC16、帧编解码/解析、遥测、参数描述符、AM32 EEPROM、4way 协议
   ├─ Transport/          # IByteTransport、SerialTransport、FakeFirmwareTransport（模拟固件）
   ├─ App/                # BoardClient、Am32Bridge、ParamCatalog、LogBuffer、自检
   └─ Ui/                 # MainForm + 8 个标签页 + UiKit 控件（纯代码布局，无 designer）
```

---

## 9. 已知限制

* 遥测/参数/AM32 的字段布局来自固件头文件与 `docs/USB_PROTOCOL.md`；
  固件改动布局时需同步更新 `Protocol/` 下的解码代码。
* `SERVO_SET_ID` / `SERVO_RAW` 固件返回 `ERR_UNSUPPORTED`（S288 总线无寄存器访问）。
* 4way 桥接**不能**编程 ESC Flash，AM32 写入只能走 DShot 编程模式（0x54/0x55）。
* `AM32_XFER` 单次阻塞最长约 150 ms；`AM32_PROG_WRITE` 每次约 9 ms，写满 192 B 约 2–3 s。
* 固件参数表（60 项）之外的参数不会显示；参数表版本变化以设备返回的描述符为准。
* 界面使用“Microsoft YaHei UI”字体（缺失时自动回退系统字体）。
