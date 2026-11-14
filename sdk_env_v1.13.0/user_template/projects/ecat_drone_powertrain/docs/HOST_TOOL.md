# HOST_TOOL — PC 上位机（ecat_drone_host）

本文档面向固件/系统工程师，说明 `ecat_drone_powertrain` 配套 PC 上位机的实现、
与固件的接口契约、构建/验证方法和当前限制。

* 工具目录：`sdk_env_v1.13.0/user_template/tools/ecat_drone_host/`
* 可执行文件：`build/ecat_drone_host.exe`（C# WinForms，.NET Framework 4.x，单文件）
* 用户手册：同目录 `README_zh.md`
* 固件协议权威文档：`docs/USB_PROTOCOL.md`（帧格式、命令、bench 租约、AM32 桥）
* 固件接口头文件：`inc/host_proto.h`、`inc/param.h`、`inc/app_config.h`、`inc/ecat_pdo.h`

---

## 1. 设计目标

1. **零依赖构建**：目标 PC 只有 Windows 自带 .NET Framework（无 dotnet SDK / MSBuild /
   NuGet / 网络）。全部源码用一个 `csc.exe` 命令即可编译；WinForms 界面全部**代码布局**
   （无 `.designer.cs`、无资源文件），保证任何环境可重现构建。
2. **协议严格对齐固件**：帧格式/CRC/命令/响应布局以 `inc/host_proto.h` 与
   `docs/USB_PROTOCOL.md` 为准，PC 侧不发明命令。
3. **可无硬件验证**：`FakeFirmwareTransport` 在进程内模拟固件，`--selftest` 覆盖协议栈与
   UI 逻辑层；`--uitest` 进一步用模拟设备跑真实窗口和 20 Hz 遥测链路。
4. **中文界面**：面向国内客户，8 个标签页全部中文标签/提示/错误信息。

---

## 2. 构建与验证

```text
cd sdk_env_v1.13.0\user_template\tools\ecat_drone_host

build.cmd                # 构建 -> build\ecat_drone_host.exe
build.cmd -Selftest      # 构建 + 运行 --selftest
build.cmd -Clean         # 清空 build\ 后重建
run.cmd                  # 启动 GUI
run.cmd --simulate       # 模拟设备启动（无硬件演示）
selftest.cmd             # 对已构建 exe 运行 --selftest 并打印报告
```

`build.ps1` 自动查找 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
（回退 32 位 Framework 4.x / 3.5），引用 `System.dll`、`System.Windows.Forms.dll`、
`System.Drawing.dll`、`System.Core.dll`、`Microsoft.CSharp.dll`（在 .NET Framework 中
`SerialPort` 位于 `System.dll`；若存在 `System.IO.Ports.dll` facade 则一并引用）。
构建参数：`/target:winexe /warn:4 /optimize+ /utf8output`。

### 验证结果（本次交付）

* 构建：`[BUILD OK] build\ecat_drone_host.exe`，**224768 字节**，零编译警告。
* `--selftest`：**43 项全部通过（43/43 PASS，0 失败）**，耗时约 1 s；
  报告写入 `build/selftest.log`。
* `--uitest`：真实主窗口 + 8 个页签 + 模拟设备连接 + 94 次遥测更新，PASS；
  报告写入 `build/uitest.log`。

---

## 3. 代码结构

```text
src/
├─ Program.cs                 入口：GUI / --simulate / --selftest / --uitest / --help
├─ Protocol/
│  ├─ Crc16.cs                CRC16-CCITT-FALSE / XMODEM / ARC
│  ├─ HostProtocol.cs         帧格式、命令/状态枚举、编码器、重同步解析器、字节工具
│  ├─ TelemetryData.cs        54 B 遥测解码、fault/status 位表、ECAT 状态、版本信息
│  ├─ ParamDesc.cs            70 B 参数描述符编解码
│  ├─ BoardData.cs            DShot 遥测 26 B / 舵机状态 20 B / ADC 24 B / AM32 状态
│  ├─ Am32Eeprom.cs           AM32 EEPROM 192 B 字段表 + 解码/编辑/hex dump
│  ├─ FourWay.cs              AM32 4-way 帧（容器/接口/BLHeli 引导层）编解码
│  └─ Events.cs               日志/跟踪事件参数
├─ Transport/
│  ├─ IByteTransport.cs       半双工字节传输抽象
│  ├─ SerialTransport.cs      System.IO.Ports.SerialPort（USB CDC-ACM）
│  └─ FakeFirmwareTransport.cs 进程内固件模拟器（协议/参数/遥测/AM32/舵机/租约行为）
├─ App/
│  ├─ BoardClient.cs          请求/响应（SEQ 匹配、300 ms 超时、2 次重试、后台接收线程）
│  ├─ Am32Bridge.cs           4-way 桥接会话（读 EEPROM/校验/原始终端）
│  ├─ ParamCatalog.cs         固件参数表离线副本（60 项、中文名/单位/分组/默认值）
│  ├─ LogBuffer.cs            带时间戳/十六进制/落盘的日志缓冲
│  ├─ ShellEvents.cs          遥测/ECAT 状态事件
│  ├─ IMainShell.cs           标签页访问应用核心的接口
│  └─ Tests.cs                --selftest（43 项）与 --uitest 实现
└─ Ui/
   ├─ MainForm.cs             主窗口：状态条、页签、连接管理、20 Hz 遥测轮询、日志接线
   ├─ ConnectionTab.cs        连接/设备信息/链路统计/设备操作
   ├─ TelemetryTab.cs         遥测仪表盘（条形指示/故障位/原始字节）
   ├─ ParamTab.cs             参数表格编辑（分组/搜索/校验/详情）
   ├─ DshotTab.cs             双 ESC 油门/解锁/停止/命令/实时 eRPM/租约保持
   ├─ ServoTab.cs             总线扫描（异步重试）/角度/状态/改 ID 提示
   ├─ Am32Tab.cs              AM32 设置网格 + 4way 读取 + DShot 编程模式写入/校验 + 原始终端
   ├─ EcatTab.cs              AL/Link/PDO 布局/TxPDO 预览/ECAT 参数
   ├─ LogTab.cs               日志窗口（十六进制/暂停/保存）
   └─ UiKit.cs                主题、状态灯、条形表、Kv 面板、控件工厂
```

分层：`Ui → App → Protocol/Transport`。协议层纯函数无 IO；传输层只做字节搬运；
`BoardClient` 负责请求/响应与重试；UI 通过 `MainForm` 的事件在 UI 线程接收数据。

---

## 4. 协议实现契约（与固件对齐）

### 4.1 帧与错误处理

* `A5 5A | CMD | SEQ | LEN_LO LEN_HI | PAYLOAD | CRC_LO CRC_HI`
* CRC16-CCITT-FALSE（poly 0x1021 / init 0xFFFF / 不反射 / 无最终异或），覆盖 `CMD..PAYLOAD`
* 响应 `PAYLOAD[0]` = 状态码；`OK` 时其后为命令数据
* PC：每请求 300 ms 超时、2 次重试（共 3 次）、SEQ 匹配、未匹配帧仅记日志
* 解析器：`A5 5A` 重同步；坏 CRC/非法长度只丢 1 字节后继续扫描
  （`FrameParser.CrcErrors / Resyncs / FramesOk` 统计）

### 4.2 关键响应布局（状态字节之后）

| 命令 | 长度 | 布局 |
|---|---|---|
| PING 0x01 | 10 | u32 magic `0x474E4950`（"PING"）+ u16 major/minor/patch |
| GET_VERSION 0x02 | 22 | u16 major/minor/patch + char[16] build date |
| GET_TELEMETRY 0x03 | 54 | `host_telemetry_t`（见 4.3） |
| GET_ECAT_STATUS 0x04 | 8 | u8 al, u8 link, u16 fault, u32 ecat_cycle_miss |
| GET_PARAM_DESC | 70 | `host_param_desc_t` |
| LIST_PARAMS | N×70, N≤7 | 分块拼接；PC 用 GET_COUNT 后按 7 条循环 |
| DSHOT_GET_TLM | 26 | `host_dshot_tlm_t`（erpm/period/valid/state/计数） |
| SERVO_READ | 20 | `host_servo_status_t` |
| ADC_GET | 24 | `host_adc_blob_t`（alarm@16，sample_count@18，last_update@20） |
| AM32_XFER | 1+N | u8 out_len + raw 4-way 应答字节 |
| AM32_STATUS | 2 | u8 state（0 idle/1 active/2 error）, u8 last_error |

### 4.3 `host_telemetry_t`（54 B）与派生值

offset：status_word@0、fault_code@2、esc_erpm[2]@4、esc_tlm_period_us[2]@12、
servo_pos_d10[2]@16、servo_speed_rpm[2]@20、servo_current_ma[2]@24、servo_temp_c[2]@28、
servo_error[2]@30、sys_voltage_dv@32、sys_current_ca@34、esc_current_ma[2]@36、
loop_counter@40、led_status@42、ecat_link_status@44（bit0/1 Link、**bit4..7 AL**）、
host_rx_frames@46、host_crc_errors@50。

* 机械转速 = `esc_erpm / PARAM_DSHOT_POLE_PAIRS`（默认 7；与 `docs/USB_PROTOCOL.md` §3 一致）
* `fault_code` 位名来自 `inc/ecat_pdo.h` `app_fault_t`；`status_word` 位名来自 `ECAT_STS_*`
* 遥测短包（旧固件）容错：缺失尾部字段按 0 处理并记录日志

### 4.4 参数系统

* 参数表 60 项（含新增 `SERVO_KP1/KP2/KD1/KD2` id 56..59，默认 25600000/128000000）
* 描述符 70 B：`id@0, type@2, persist@3, readonly@4, len@5, min@6, max@10, def@14, value@18, name@22[16], desc@38[32]`
* 名称固件为小写（`dshot_rate`、`servo_kp1`…），分组按前缀（ecat_/dshot_/servo_/adc_/led_/usb_/am32_）
* 写入后固件立即 `param_apply()`；`PARAM_SAVE` 掉电保持，`ERR_FLASH` 自动重试一次
* `PARAM_DSHOT_RATE` 仅接受 150/300/600/1200（设备端校验）
* `PARAM_SERVO_BAUD` 范围 9600..6000000，默认 6000000（J288/S288）
* `PARAM_USB_PROTO_TIMEOUT_MS` = bench 租约超时（默认 1000 ms）

### 4.5 DShot / 舵机 bench 行为

* `DSHOT_TEST`：`throttle_us` = 0（停止）或 1000..2000；`DSHOT_CMD`：`esc=0xFF` 广播
* `DSHOT_TEST` / `DSHOT_CMD` / `SERVO_MOVE_TEST` 会刷新 1000 ms bench 租约；
  `GET_TELEMETRY` **不**续租。租约到期或 EtherCAT 进入 OP 时固件自动停止电机/舵机
  → 上位机 DShot 页提供“保持台架租约（每 400 ms 重发油门）”开关
* `SERVO_SCAN` **异步**：第一次请求启动扫描并返回 `ERR_BUSY`，PC 用相同请求重试，
  直到 `OK` + 32 B 位图，或 `ERR_TIMEOUT`（约 10 s）；PC 侧总等待上限 12 s
* `SERVO_SET_ID` / `SERVO_RAW` 返回 `ERR_UNSUPPORTED`（S288 无寄存器访问）；UI 明确提示
* 舵机 `id` 必须等于 `PARAM_SERVO_IDn`，否则 `ERR_PARAM_ID`

### 4.6 AM32 配置（读/写两条路径）

**读（权威）**：`AM32_BEGIN(esc)` → `AM32_XFER` 交换 4-way 帧（PC 构造
`ESC+CMD+LEN+PARAM+CRC16-XMODEM`）→ `AM32_END`。
支持三种帧模式：引导命令容器（0x04 READ_EEPROM，默认）、BLHeliSuite 4way 接口帧
（0x3D/0x3E，含 CRC 校验）、原始字节。EEPROM 读取 192 B（应答约 197 B ≈ 103 ms）。
`AM32_BEGIN` 前置条件：ECAT 非 OP、输出关闭、电机停止，否则 `ERR_NOT_READY`。

**写（DShot 编程模式，新增命令）**：

| CMD | 请求 | 响应 | 说明 |
|---|---|---|---|
| 0x54 `AM32_PROG_WRITE` | `u8 esc, u8 addr(0..191), u8 value` | – | 队列满 `ERR_BUSY`（约 10–15 ms 后重试同一请求）；ECAT OP/输出使能 `ERR_NOT_READY` |
| 0x55 `AM32_PROG_SAVE` | `u8 esc` | – | 提交到 ESC Flash |

**前置条件**（固件侧强制）：EtherCAT 非 OP 且输出关闭；ESC 已上电、已解锁、电机停转
（throttle=0）；`PARAM_AM32_ENABLE_PASSTHROUGH`=1；4-way 桥未占用。

**上位机执行序列**：

1. **备份**：要求先执行 4-way 读取（`AM32_BEGIN` → `AM32_XFER` CMD 0x04 → `AM32_END`），
   工具把镜像自动保存到 `build/backup/am32_escN_<时间>.bin`；未读取时禁止写入。
2. **逐字节写**：`addr=0..191` 依次发 `AM32_PROG_WRITE(0x54)`；一次一条请求、每 4 字节
   让出约 10 ms（每字节约 9 ms）。`ERR_BUSY(0x07)` = 命令队列满 → 等 5..10 ms 重试同一请求
   （有界）；`ERR_PARAM_RANGE(0x05)` = esc/addr 非法；`ERR_NOT_READY(0x08)` = OP/输出使能/
   电机未停/4-way 占用 → 终止；`ERR_UNSUPPORTED(0x0A)` = passthrough 未启用或固件未编译
   AM32 调参 → 提示用户改 `AM32_ENABLE_PASSTHROUGH`。
3. **保存**：`AM32_PROG_SAVE(0x55)` 每 20..50 ms 轮询直到 OK（BUSY = 末尾字节仍在移位；
   192 B 最坏约 1.8 s + 保存）。
4. **等待**：SAVE OK 后约 50 ms（ESC Flash 写入完成）。
5. **校验**：重新 `AM32_BEGIN` → 4-way 回读 192 B → **逐字节比对** → `AM32_END`。
   不一致时报告首个不一致偏移（期望/实际）。

全程约 2–3 s，进度条 + 日志显示阶段；可取消（取消后不执行 SAVE，ESC RAM 内容重新上电恢复）。
原因：ESC 擦写 Flash 的 ACK 可 >1 s，超出 4-way 桥 ~150 ms 预算
（见 `docs/USB_PROTOCOL.md` §8.2/§12），因此写入只能走 DShot 编程模式。
命令总数：30（含 0x54/0x55）。

AM32 EEPROM 192 B 字段表（`Inc/eeprom.h`，见 `.work/refs/DSHOT_AM32_NOTES.md` §7）：
关键偏移 `eeprom_version@1`、`version.major@3/.minor@4`、`dir_reversed@17`、
`bi_direction@18`、`comp_pwm@20`、`advance_level@23`、`pwm_frequency@24`、
`motor_poles@27`、`demag_comp@14`、`input_type@46`、`tune[128]@48..175`、
`can.*@176..183`。默认值上游未公开（模拟器使用标注的合成镜像，实机必须先读取）。

### 4.7 设备操作

* `BOOTLOADER`（magic `0x424F4F54`）/`RESET`：设备先应答后约 30 ms 重启；
  PC 以 500 ms、无重试发送，成功后主动断开连接（避免把重启当超时）。

---

## 5. 功能矩阵

| 需求（task-7） | 状态 | 说明 |
|---|---|---|
| COM 枚举/连接、PING/GET_VERSION | ✅ | 连接页，自动识别 |
| 遥测 20 Hz + 解码 + 故障位 + ECAT AL/Link | ✅ | 遥测页；频率 1–100 Hz 可调 |
| 参数网格（LIST/GET_DESC/SET/SAVE/RESET，分组+范围校验） | ✅ | 60 项、分块读取、可编辑、详情/搜索/分组 |
| DShot 测试（油门 1000–2000、解锁/停止、实时 eRPM、命令按钮） | ✅ | 双 ESC、广播、重复次数、租约保持 |
| 舵机测试（扫描/角度/读取/改 ID） | ✅/⚠ | 扫描/角度/读取已实现；改 ID 固件返回 `ERR_UNSUPPORTED`，UI 明确提示 |
| AM32 桥（读 EEPROM 192B / 写 / 解码网格 / 原始 hex / 原始终端） | ✅ | 读=4way 权威；写=DShot 编程模式 0x54/0x55 + SAVE + 回读校验 |
| EtherCAT 页（AL、RxPDO/TxPDO、ECAT 参数） | ✅ | TxPDO 由遥测按同布局重组并显示 48 B hex |
| 日志（时间戳、hex 开关、保存文件） | ✅ | 协议收发帧/操作全记录 |
| 固件复位/DFU 按钮 | ✅ | 连接页 |
| `--selftest` 自检 | ✅ | 43 项全部通过；含模拟固件端到端 |
| `--uitest` 界面运行检查（附加） | ✅ | 真实窗口 + 8 页签 + 遥测链路 |
| `README_zh.md` | ✅ | 工具目录 |
| `docs/HOST_TOOL.md` | ✅ | 本文件 |

---

## 6. 无硬件验证方法（供 CI/回归）

1. `build.cmd -Selftest`：编译 → 运行 `--selftest` → 退出码 0 表示 43/43 通过。
2. `build\ecat_drone_host.exe --uitest`：创建真实主窗口、自动连接模拟设备、遍历 8 个页签、
   检查遥测更新次数 >0；退出码 0 = PASS；报告 `build\uitest.log`。
3. `build\ecat_drone_host.exe --simulate`：人工演示（无硬件）。
4. 模拟器支持故障注入（`DropNextResponses`、`CorruptNextResponses`、`ServoScanBusyReplies`、
   `Am32ProgBusyReplies`），用于测试重试/重同步/异步扫描/队列满重试路径。

---

## 7. 已知限制 / 待实机确认

1. 若固件后续修改遥测/参数/瞬态响应布局，需要同步更新 `src/Protocol/` 与 `src/App/` 的解码代码
   （SELFTEST 的布局断言会提前报警）。
2. AM32 写入依赖固件 0x54/0x55 的实现；若固件未启用，则只有 4way 读取可用。
   4way 直写（0x05）在真实 ESC 上不可用（ESC Flash ACK 超时），UI 已改用编程模式。
3. `SERVO_SET_ID`/`SERVO_RAW` 不支持（固件 `ERR_UNSUPPORTED`）。
4. 参数表 60 项为当前固件版本；新增参数会自动出现在网格（描述符来自设备），
   但中文名/单位需要更新 `ParamCatalog` 才更友好。
5. 界面字体假定中文 Windows 有“Microsoft YaHei UI”，缺失时回退。
6. `--uitest` 需要交互桌面；在无桌面会话中运行可能失败（不影响 `--selftest`）。

---

## 8. 参考

* `inc/host_proto.h` — 帧格式/命令/状态/响应尺寸常量
* `docs/USB_PROTOCOL.md` — 固件侧协议全文（含 bench 租约、AM32、复位时序）
* `inc/param.h`、`src/param.c` — 参数表与默认值
* `.work/refs/DSHOT_AM32_NOTES.md` §7 — AM32 EEPROM 布局
* `.work/refs/am32/bf_serial_4way.c`、`bf_serial_4way_avrootloader.c` — 4-way 接口/引导层参考
* `inc/ecat_pdo.h` — PDO 布局与 fault/status 位定义
