# HPM5E3Y / HPM5E00 与 J288/S288 舵机 —— 事实摘要（FACTS）

本文件只记录 **从 PDF 提取出的原文事实**，供嵌入式固件工程（EtherCAT 无人机动力总成）做硬件引脚配置依据。
所有结论均给出「来源 + 页码 + 原文片段」。凡文档中没有的内容一律写 **未找到**，并列出检索词。

## 0. 文档、页码与引用约定

| 缩写 | 文件 | 说明 |
|---|---|---|
| **DS** | `HPM5E00DSV04.pdf` | 《HPM5E00 系列高性能微控制器数据手册 Rev0.4》，PDF 共 98 页 |
| **UM** | `HPM5E00UMV04.pdf` | 《HPM5E00 系列高性能微控制器用户手册 Rev0.4》，PDF 共 1272 页 |
| **PHY** | `HPM5E00xilieMCUpianneiMIIPHYshiyongzhinanv1.1.pdf` | 《HPM5E00 系列微控制器片内 MII PHY 使用指南 v1.1》，PDF 共 15 页 |
| **SERVO** | `无刷数字舵机J288S288使用手册.pdf` | 《无刷数字舵机 J288/S288 使用手册》，PDF 共 11 页 |

* **页码一律是 PDF 解析页序号**，即对应 `.work/pdf_text/*.txt` 中的 `=== page N ===`；文档页脚印刷页码另行标注（如「页脚 59/95」）。
* 引文为提取器输出的**逐字**文本（仅中文与英文/数字之间的空格可能因版面还原而略有差异）。
* 表格类事实（引脚表、订购信息表）由文本坐标重建；凡涉及**合并单元格**的，均在条目中注明判定依据（表格竖线位置），不臆测。

---

# A. HPM5E3Y / HPM5E00 系列

## A1. 型号列表、后缀含义、Flash 与 SRAM

**结论 1.1：文档中不存在裸型号 “HPM5E3Y”/“HPM5E31”，只有带后缀的订货型号；HPM5E3Y 只有 IGN1、IVK1 两个型号。**

> 〔DS p1，页脚 1/95〕「产品型号：」
> 〔DS p1〕「HPM5E31IGN1、HPM5E11IGN1」
> 〔DS p1〕「HPM5E3YIGN1、HPM5E3YIVK1」

（同页该列表共 4 行：`HPM5E31IGN1、HPM5E11IGN1` / `HPM5E31IPB1、HPM5E11IPB1` / `HPM5E31IVK1、HPM5E11IVK1` / `HPM5E3YIGN1、HPM5E3YIVK1`）

同页另有 UM 型号清单：
> 〔UM p1〕「HPM5E3YIGN1、HPM5E3YIVK1」

**结论 1.2：后缀字母 = 封装。** DS 订购信息表按封装分行，可交叉确定：`G`=BGA196、`PB`=eLQFP100、`VK`=BGA121。

> 〔DS p95，页脚 92/95〕「BGA196 封 装 | 有 有 有」「eLQFP100 封装 | 有 有 /」「BGA121 封 装 | 有 有 有」
> 〔DS p95〕「GPIO | 148(BGA196),72(eLQFP100),72(BGA121)」

判定依据：p95 该表三列依次为 HPM5E31 / HPM5E11 / HPM5E3Y（表头「产品型号 | HPM5E31 HPM5E11 HPM5E3Y」）。eLQFP100 行第三列为 “/”，与 p1 型号清单中 HPM5E3Y 无 IPB1 型号一致；BGA196、BGA121 行三列均为“有”，与三个系列都有 IGN1/IVK1 一致。**命名规则图（DS p94 图 27）为矢量图，文字严重残缺**（仅能辨认 “G: … BGA196 …”“V: 9*9 BGA1…”“PB: …”），故字母含义属**交叉推断**，文档无明文定义。

**结论 1.3：片上 Flash = 1 MB，且是“合封（SIP）的串行 NOR”，不是 MCU 片内嵌入式 Flash。**

> 〔DS p1〕「– 1 MB 内置闪存」
> 〔DS p68，页脚 65/95〕「4.2 内置闪存特性」「本产品内置 1MB 闪存。」
> 〔DS p68〕「产品型号HPM5E31IGN1，HPM5E11IGN1，HPM5E31IPB1，HPM5E11IPB1的内置闪存型号为GT25Q80AH-」
> 〔DS p68〕「产品型号 HPM5E3YIGN1，HPM5E3YIVK1，HPM5E31IVK1，HPM5E11IVK1 的内置闪存型号为 P25Q80SU-」
> 〔DS p94，页脚 91/95〕订购信息表表头「片上闪存」，其值行「1MB」（表 46 跨页，表头在 p94 底部）

**结论 1.4：SRAM 合计 544 KB（含 ILM 128KB + DLM 128KB + 通用 SRAM）。**

> 〔DS p1〕「– 共 544 KB 片上 SRAM，包括通用内」
> 〔DS p1〕「– 指令本地存储器 ILM 和数据本地存」
> 〔UM p269，页脚 222/1225〕「0x00000000 | 0x0001FFFF | 128 KBytes | CPU0_ILM | N/A」
> 〔UM p269〕「0x00200000 | 0x0021FFFF | 128 KBytes | CPU0_DLM | N/A」
> 〔UM p269〕「0x01200000 | 0x0123FFFF | 256 KBytes | XRAM0 | N/A」
> 〔DS p94〕「544 KB」（订购信息表「片上总内存」值行）

**结论 1.5：CPU 主频 480 MHz。**

> 〔DS p94〕「MHz 480」（订购信息表「CPU0 主频」值行，表头「CPU0」「主频」同在 p94）
> 〔DS p94〕「RV32-IMAFDCBP」

---

## A2. 片内 MII PHY（EtherCAT 2 端口）与 ESC0 PHY 引脚

**结论 2.1：只有 HPM5E\*Y\* 系列（即 HPM5E3Y）合封 2 个百兆 MII PHY（PHY0/PHY1）；HPM5E31/HPM5E11 没有。**

> 〔DS p62，页脚 59/95〕「表 3列出了主芯片与内置以太网 PHY 连接的 IO，适用于 HPM5E*Y* 等内置以太网 PHY 的产品型号。以太网 PHY 与主芯片的连接使用 MII 接口。」
> 〔DS p95〕「内置以太网 PHY | / 2x 百兆」
> 判定依据：p95 该行在 x=297.6 处**没有竖线**（其余双列行有），故 “/” 位于 HPM5E31+HPM5E11 合并单元格，“2x 百兆”位于 HPM5E3Y 单元格 → E31/E11 无内置 PHY，E3Y 有 2 个。
> 〔PHY p4，页脚 4/15〕表格：「1 | HPM5E3YIGN1 | √」「2 | HPM5E3YIVK1 | √」（列名「合封 MII PHY」）
> 〔DS p68〕「4.3 内置以太网 PHY 特性 … ● 内置 2 个以太网 PHY 分别为 PHY0 和 PHY1 ● 100BASE-TX 全双工 ● 支持 MII 和 RMII 接口与主芯片连接 ● 支持 Auto-Negotiation ● 支持 Auto-MDIX」

**结论 2.2：ESC 是 3 端口 EtherCAT 从站控制器（全部型号都有）。**

> 〔DS p1〕「– EtherCAT 从站控制器，支持 3 个端 口」
> 〔DS p95〕「EtherCAT 从 站控制器 | 3 个端口」（该行无内部竖线 → 三型号共用）
> 〔UM p1098，页脚 1051/1225〕「● 端口数量：3 个 ● FMMU：8 个 ● SyncManager：8 个 ● Process data RAM（PDRAM）：60KB ● Distributed Clocks：64 位 ● PHY 接口：MII/RMII」

**结论 2.3：内置 PHY 的 MII/时钟/复位信号走「封装内部」的 PV、PW 两组未引出 IO；只有模拟线侧信号引到 PA 口。**

> 〔UM p1193，页脚 1146/1225〕「以太网 PHY 的 MII 接口和时钟复位等信号在封装内部直接与主芯片的 IO 相连接，主要使用了 PV 和 PW 两组未引出的内部 IO。」
> 〔UM p1193〕「主芯片与其相连的 IO 在 PINMUX 上支持映射到 ESC 的 Port0 和 Port1，以及 ENET0」
> 〔UM p1193〕表 229 摘录：「PW21 | ENET PHY0 | 时钟输入 XI」「PW17 | ENET PHY0 | LDO 使能 EN_DLDO」「PV12 | ENET PHY0 | 复位（低有效）RSTn」「PV00 | ENET PHY0 | 接收数据有效 RXDV」

**结论 2.4：ESC0 的 port0/port1 也可以引到外部引脚（ALT11），即“外部 PHY 方案”与“内置 PHY 方案”共用同一组 ESC 端口。**

> 〔DS p22，页脚 19/95〕「PA25 | … ESC0_P0_TXD_0(ALT11) ESC0_CTR_0(ALT18)」
> 〔DS p23〕「H4 | D2 | 13 | PA30 | … ESC0_MDIO(ALT11) ETH0_MDIO(ALT18) QEI1_A(ALT20)」
> 〔DS p49〕「PF02 | … ESC0_P1_TXCK(ALT11)」（完整清单见 A5）

**结论 2.5：哪些引脚承载 PHY 的线侧模拟信号（变压器侧）。**

> 〔DS p63，页脚 60/95〕表 4：
> 「PA20 | ENET PHY0 | 数据接收负极 RD_N」「PA21 | ENET PHY0 | 数据接收正极 RD_P」
> 「PA22 | ENET PHY0 | 数据发送负极 TD_N」「PA23 | ENET PHY0 | 数据发送正极 TD_P」
> 「PA16 | ENET PHY1 | 数据接收负极 RD_N」「PA17 | ENET PHY1 | 数据接收正极 RD_P」
> 「PA18 | ENET PHY1 | 数据发送负极 TD_N」「PA19 | ENET PHY1 | 数据发送正极 TD_P」
> 「PA27 | ENET PHY0 | 偏置电阻 RBIAS，应连接 2.49K 欧姆 电阻」
> 「PA24 | ENET PHY1 | 偏置电阻 RBIAS，应连接 2.49K 欧姆 电阻」
> 「PA28 | ENET PHY0 | 100M 网络连接指示灯 (LED1)/PHY 地 址位 1」
> 「PA29 | ENET PHY0 | 10M 网络 (限用于 TSW 或 ENET) 连接 指示灯 (LED0)/PHY 地址位 0」
> 「PA30 | ENET PHY0 | MDIO」「PA31 | ENET PHY0 | MDC」（PHY1 同样复用 PA30/PA31）

**注意（原文）**：
> 〔UM p1193〕「注意，以上除 MDIO 之外的引脚需占用主芯片的 GPIO 引脚，要求在主芯片中将这些引脚的功能选择配置为模拟功能以避免信号干扰。」
> 〔UM p1193〕「注意，即使只使用 1 个内置以太网 PHY，另外一个内置以太网 PHY 所占用的 GPIO 也不能用作普通 GPIO 功能」

---

## A3. USB

**结论 3.1：1 个 USB2.0 OTG 控制器，集成高速 PHY（HS-PHY）。**

> 〔DS p1〕「– 1 个 USB 2.0 OTG，集成 HS-PHY」
> 〔DS p95〕「USB | 1 个, 集成 HS PHY」（该行无内部竖线 → 三型号共用，**HPM5E3Y 有 USB**）
> 〔UM p226〕USB0 资源；〔UM p270，页脚 223/1225〕「0xF1420000 | 0xF1423FFF | 16 KBytes | USB0」

**结论 3.2：DP/DM/VBUS 是专用引脚（不在 PINMUX 数字功能表中）；ID/OC/PWR 是 IO 复用（ALT24）。**

专用（DS p61，页脚 58/95，表 2「SOC IOMUX」末尾的电源/特殊引脚段）：
> 「L12 | F8 | - | USB0_VBUS | - | - | -」
> 「L13 | F10 | 67 | USB0_DN | - | - | -」
> 「L14 | F11 | 68 | USB0_DP | - | - | -」

复用（DS p53，页脚 50/95）：
> 「PF21 | … USB0_ID(ALT24) …」「PF22 | … USB0_ID(ALT24) …」
> 「PF20 | … USB0_OC(ALT24) …」「PF23 | … USB0_OC(ALT24) …」
> 「PF18 | … USB0_PWR(ALT24) …」「PF19 | … USB0_PWR(ALT24) …」

**结论 3.3：USB0_VBUS 耐压 5.5V（VBUS 检测脚按 5V 设计）。**
> 〔DS p68〕「USB0_VBUS        USB0_VBUS 输入        -        -        -        5.0        5.5        V」

---

## A4. ADC16

**结论 4.1：2 个 ADC 实例（ADC0/ADC1），16 位 / 2MSPS（可配 12 位 / 4MSPS），共 16 路模拟输入引脚。**

> 〔DS p1〕「– 2 个 ADC，16 位/2MSPS，可配置为 12 位/4MSPS，共支持 16 路模拟输 入引脚」
> 〔DS p95〕「ADC | 2× 16b」
> 〔UM p1197，页脚 1150/1225〕「本产品支持 2 个 16 位模拟数字转换器 ADC16。… ADC16 的转换精度设置为 16 位时，最大采样率 2MSPS。本产品上，ADC 的高位参考电压引脚是 VREFH，低位参考电压引脚是 VREFL。」
> 〔UM p1197〕「ADC0 和 ADC1 均支持输入通道 ina0∼15，全部来自 IO. … ADC 输入通道与 IO 的具体连接，请参考章 19。」
> 〔UM p1197〕「59.1.5 ADC 差分模式 相邻的两个 ADC 可以配置成差分模式，ADC0 和 ADC1 组成一组。」

**结论 4.2：差分模式是“同一通道号由 ADC0 与 ADC1 同时采样相减”，不是独立的正/负引脚。**
> 〔UM p1197，页脚 1150/1225〕「59.1.5  ADC 差分模式 相邻的两个 ADC 可以配置成差分模式，ADC0 和 ADC1 组成一组。」
> 〔UM p1207，页脚 1160/1225〕该页含「ADC 差分模式」小节，但为图/文本框混排，提取文字残缺（见 EXTRACTION_REPORT 的已知限制），此处不作为引文依据。

**结论 4.3：完整 ADC 引脚↔通道映射表（全部 16 个 pad，均同时具备 ADC0 与 ADC1 同名通道）。**
来源：DS p52–p55 引脚功能表（表 2「SOC IOMUX」）的「模拟功能」列，逐行摘录：

| Pad | ADC0 通道 | ADC1 通道 | 模拟功能列原文（同格内其它内容） | 出处 |
|---|---|---|---|---|
| PF16 | ADC0_IN08 | ADC1_IN08 | `ACMP0_CH0_I NN7` | DS p52 |
| PF17 | ADC0_IN12 | ADC1_IN12 | `ACMP0_CH1_I NN7` | DS p52 |
| PF18 | ADC0_IN11 | ADC1_IN11 | `ACMP0_CH1_I NP7` | DS p53 |
| PF19 | ADC0_IN13 | ADC1_IN13 | `ACMP0_CH0_I NP7` | DS p53 |
| PF20 | ADC0_IN03 | ADC1_IN03 | `ACMP0_CH0_I NP4 ACMP0_CH1_I NP4` | DS p53 |
| PF21 | ADC0_IN02 | ADC1_IN02 | `ACMP0_CH0_I NN4 ACMP0_CH1_I NN4` | DS p53 |
| PF22 | ADC0_IN14 | ADC1_IN14 | `ACMP0_CH0_I NN5 ACMP0_CH1_I NN5` | DS p53 |
| PF23 | ADC0_IN15 | ADC1_IN15 | `ACMP0_CH0_I NN3 ACMP0_CH1_I NN3` | DS p53 |
| PF24 | ADC0_IN10 | ADC1_IN10 | `ACMP0_CH0_I NP5 ACMP0_CH1_I NP5` | DS p54 |
| PF25 | ADC0_IN09 | ADC1_IN09 | `ACMP0_CH0_I NP3 ACMP0_CH1_I NP3` | DS p54 |
| PF26 | ADC0_IN01 | ADC1_IN01 | `ACMP0_CH0_I NN6 ACMP0_CH1_I NN6` | DS p54 |
| PF27 | ADC0_IN00 | ADC1_IN00 | `ACMP0_CH0_I NP6 ACMP0_CH1_I NP6` | DS p54 |
| PF28 | ADC0_IN05 | ADC1_IN05 | `ACMP0_CH0_I NN2 ACMP0_CH1_I NN2` | DS p54 |
| PF29 | ADC0_IN04 | ADC1_IN04 | `ACMP0_CH0_I NP2 ACMP0_CH1_I NP2` | DS p54 |
| PF30 | ADC0_IN06 | ADC1_IN06 | `ACMP0_CH0_I NN1 ACMP0_CH1_I NN1` | DS p55 |
| PF31 | ADC0_IN07 | ADC1_IN07 | `ACMP0_CH0_I NP1 ACMP0_CH1_I NP1` | DS p55 |

原样示例（便于核对）：
> 〔DS p52〕「N7 | K6 | 47 | PF16 | GPIO_F_16(ALT0) SDM0_DAT_0(ALT23) | ADC0_IN08 ADC1_IN08 ACMP0_CH0_I NN7 | B02」
> 〔DS p54〕「… | PF27 | GPIO_F_27(ALT0) SDM0_CLK_1(ALT23) | ADC0_IN00 ADC1_IN00 ACMP0_CH0_I NP6 ACMP0_CH1_I NP6 | B02」

**重要说明**
* **只有 PF16–PF31 这 16 个 pad 带 ADC 通道**（全表仅 16 行出现 `ADC0_IN`/`ADC1_IN`，已逐页检索 DS 全文）。
* 表格中 `NNn` / `NPn` **不是 ADC 差分负/正端**，而是模拟比较器 ACMP 的输入（N=Negative，P=Positive），见 UM p1197「本产品支持 1 个 ACMP，共 2 个 Channel 的模拟比较器」。文档中**未找到** ADC 的“差分正/负引脚名”表。
* 模拟功能列的供电分组均为 `B02`（PF16–PF31 同组）。

---

## A5. ESC0 引脚（引脚功能表中所有 `ESC0_` 功能）

**结论 5.1：ESC0 port0 可映射到 PA16–PA29 或 PB00–PB11（ALT11）；port1 可映射到 PF02–PF14（ALT11）；另外 PV00–PV15 = 内部 PHY0 的 port0，PW00–PW21 = 内部 PHY1 的 port1。**

port0（外部引脚，ALT11，DS p20–p25）：
| Pad | 功能 | 出处 |
|---|---|---|
| PA16 | ESC0_P0_RXDV | DS p20 |
| PA17 | ESC0_P0_RXD_0 | DS p20 |
| PA18 | ESC0_P0_RXD_1 | DS p20 |
| PA19 | ESC0_P0_RXD_2 | DS p21 |
| PA20 | ESC0_P0_RXD_3 | DS p21 |
| PA21 | ESC0_P0_RXCK | DS p21 |
| PA23 | ESC0_P0_RXER | DS p21 |
| PA24 | ESC0_P0_TXCK | DS p22 |
| PA25 | ESC0_P0_TXD_0（+ESC0_CTR_0 ALT18） | DS p22 |
| PA26 | ESC0_P0_TXD_1 | DS p22 |
| PA27 | ESC0_P0_TXD_2 | DS p22 |
| PA28 | ESC0_P0_TXD_3（+ESC0_CTR_1 ALT18） | DS p22 |
| PA29 | ESC0_P0_TXEN | DS p22 |
| PB00 | ESC0_P0_RXDV | DS p23 |
| PB01 | ESC0_P0_RXD_0 | DS p23 |
| PB02 | ESC0_P0_RXD_1 | DS p23 |
| PB03 | ESC0_P0_RXD_2 | DS p24 |
| PB04 | ESC0_P0_RXD_3 | DS p24 |
| PB05 | ESC0_P0_RXCK | DS p24 |
| PB06 | ESC0_P0_TXCK | DS p24 |
| PB07 | ESC0_P0_TXD_0 | DS p25 |
| PB08 | ESC0_P0_TXD_1 | DS p25 |
| PB09 | ESC0_P0_TXD_2 | DS p25 |
| PB10 | ESC0_P0_TXD_3 | DS p25 |
| PB11 | ESC0_P0_TXEN | DS p25 |
| PV00–PV15 | ESC0_P0_*（内部 PHY0，未引出） | DS p57；UM p1193 |

port1（ALT11，DS p49–p52）：
| Pad | 功能 | 出处 |
|---|---|---|
| PF02 | ESC0_P1_TXCK | DS p49 |
| PF03 | ESC0_P1_TXD_0 | DS p50 |
| PF04 | ESC0_P1_TXD_1 | DS p50 |
| PF05 | ESC0_P1_TXD_2 | DS p50 |
| PF06 | ESC0_P1_TXD_3 | DS p50 |
| PF07 | ESC0_P1_TXEN | DS p51 |
| PF08 | ESC0_P1_RXDV | DS p51 |
| PF09 | ESC0_P1_RXD_0 | DS p51 |
| PF10 | ESC0_P1_RXD_1 | DS p51 |
| PF11 | ESC0_P1_RXD_2 | DS p51 |
| PF12 | ESC0_P1_RXD_3 | DS p52 |
| PF13 | ESC0_P1_RXCK | DS p52 |
| PF14 | ESC0_P1_RXER | DS p52 |
| PW00–PW21 | ESC0_P1_*（内部 PHY1，未引出） | DS p59；UM p1193 |

**结论 5.2：REFCK（25MHz 参考时钟输出）可来自多个 pad。**
| Pad | ALT | 出处 |
|---|---|---|
| PA09 | ESC0_REFCK(ALT11) | DS p20 |
| PD00 | ESC0_REFCK(ALT18) | DS p39 |
| PD01 | ESC0_REFCK(ALT18) | DS p39 |
| PD14 | ESC0_REFCK(ALT18) | DS p43 |
| PD15 | ESC0_REFCK(ALT18) | DS p43 |
| PD29 | ESC0_REFCK(ALT27) | DS p46 |
| PW20 | ESC0_REFCK(ALT11) | DS p59 |
| PW21 | ESC0_REFCK(ALT11) | DS p59 |

**结论 5.3：CTR_0 … CTR_8（分布式时钟同步信号）分布（ALT11/18/27 均有实例）。**
> 〔DS p22〕「PA25 … ESC0_P0_TXD_0(ALT11) ESC0_CTR_0(ALT18)」「PA28 … ESC0_P0_TXD_3(ALT11) ESC0_CTR_1(ALT18)」
> 〔DS p26〕「PB24 … ESC0_CTR_4(ALT11)」「PB25 … ESC0_CTR_5(ALT11)」「PB26 … ESC0_CTR_6(ALT11)」「PB27 … ESC0_CTR_7(ALT11)」「PB28 … ESC0_CTR_8(ALT11)」
> 〔DS p32–p35〕「PC09 … ESC0_CTR_0(ALT27)」「PC10 … ESC0_CTR_1(ALT27)」「PC11 … ESC0_CTR_2(ALT27)」「PC12 … ESC0_CTR_3(ALT27)」「PC13 … ESC0_CTR_4(ALT27)」「PC14 … ESC0_CTR_5(ALT27)」「PC15 … ESC0_CTR_6(ALT27)」「PC16 … ESC0_CTR_7(ALT27)」「PC17 … ESC0_CTR_8(ALT27)」
> 〔DS p35–p39〕「PC18 … ESC0_CTR_0(ALT18)」「PC19 … ESC0_CTR_1(ALT18)」…「PC31 … ESC0_CTR_8(ALT18)」
> 〔DS p39–p42〕「PD00 … ESC0_CTR_0(ALT27)」「PD01 … ESC0_CTR_1(ALT27)」「PD02 … ESC0_CTR_2(ALT27)」「PD03 … ESC0_CTR_3(ALT27)」「PD08 … ESC0_CTR_8(ALT27)」
> 〔DS p47–p49〕「PE00 | … ESC0_CTR_4(ALT11) ESC0_EVTO_1(ALT27)」「PE01 … ESC0_CTR_5(ALT11)」「PE02 … ESC0_CTR_6(ALT11)」「PE03 | … ESC0_CTR_1(ALT11)」「PE04 … ESC0_CTR_2(ALT11)」「PE05 … ESC0_CTR_3(ALT11)」
> 〔DS p57–p58〕「PV12 | GPIO_V_12(ALT0) SPI1_MISO(ALT5) ESC0_CTR_0(ALT11)」「PW12 … ESC0_CTR_1(ALT11)」「PW16 … ESC0_CTR_6(ALT11)」「PW17 … ESC0_CTR_7(ALT11)」

**结论 5.4：MDC/MDIO 有三组可用 pad。**
> 〔DS p23〕「PA30 … ESC0_MDIO(ALT11) ETH0_MDIO(ALT18)」「PA31 … ESC0_MDC(ALT11) ETH0_MDC(ALT18)」
> 〔DS p49〕「PF00 … ESC0_MDC(ALT11)」「PF01 … ESC0_MDIO(ALT11)」
> 〔DS p46–p47〕「PD30 … ESC0_MDIO(ALT27)」「PD31 … ESC0_MDC(ALT27)」

**结论 5.5：ESC 的 EEPROM（I2C）信号 ESC0_SCL/SDA 在 PD02/PD03 与 PD08/PD09。**
> 〔DS p40〕「PD02 … ESC0_SCL(ALT18) … ESC0_CTR_2(ALT27)」「PD03 … ESC0_SDA(ALT18) … ESC0_CTR_3(ALT27)」
> 〔DS p42〕「PD08 … ESC0_SCL(ALT18) ESC0_CTR_8(ALT27)」「PD09 … ESC0_SDA(ALT18)」
> 〔UM p1098〕「● Process data interface（PDI）：片上总线」
> 〔UM p1098〕「● EEPROM：支持 I2C 外挂和模拟 EEPROM」
> 〔UM p1098〕「默认工作在模拟 EEPROM 模式，无需外挂 EEPROM。若使用外挂 EEPROM，需将寄存器 ESC.GPR_CFG0[EEPROM_EMU] 清零。」

**结论 5.7：ESC0 还有第三个端口 port2，可引到 PD16–PD28（ALT27，仅这些 pad 上出现 `ESC0_P2_*`）。**
> 〔DS p43〕「G12 | - | - | PD16 | GPIO_D_16(ALT0) ESC0_GPI_15(ALT10) ESC0_GPO_48(ALT11) PPI0_DQ_10(ALT13) ESC0_GPO_00(ALT18) SYSCTL_CLK_OBS_0(ALT 24) ESC0_P2_RXDV(ALT27) | - | B00」
> 〔DS p44〕「H14 | - | - | PD17 | … ESC0_P2_RXD_0(ALT27)」「H13 | - | - | PD18 | … ESC0_P2_RXD_1(ALT27)」「H12 | - | - | PD19 | … ESC0_P2_RXD_2(ALT27)」「J14 | - | - | PD20 | … ESC0_P2_RXD_3(ALT27)」「J13 | - | - | PD21 | … ESC0_P2_RXCK(ALT27)」
> 〔DS p45〕「J12 | - | - | PD22 | … ESC0_P2_RXER(ALT27)」「F10 | - | - | PD23 | … ESC0_P2_TXCK(ALT27)」「G11 | - | - | PD24 | … ESC0_P2_TXD_0(ALT27)」「G10 | - | - | PD25 | … ESC0_P2_TXD_1(ALT27)」「H11 | - | - | PD26 | … ESC0_P2_TXD_2(ALT27)」
> 〔DS p46〕「H10 | - | - | PD27 | … ESC0_P2_TXD_3(ALT27)」「J11 | - | - | PD28 | … ESC0_P2_TXEN(ALT27)」

port2 完整信号表（PD16–PD28，ALT27）：

| Pad | 功能 | Pad | 功能 |
|---|---|---|---|
| PD16 | ESC0_P2_RXDV | PD23 | ESC0_P2_TXCK |
| PD17 | ESC0_P2_RXD_0 | PD24 | ESC0_P2_TXD_0 |
| PD18 | ESC0_P2_RXD_1 | PD25 | ESC0_P2_TXD_1 |
| PD19 | ESC0_P2_RXD_2 | PD26 | ESC0_P2_TXD_2 |
| PD20 | ESC0_P2_RXD_3 | PD27 | ESC0_P2_TXD_3 |
| PD21 | ESC0_P2_RXCK | PD28 | ESC0_P2_TXEN |
| PD22 | ESC0_P2_RXER | | |

**结论 5.8：ESC0_GPI_00…63 / ESC0_GPO_00…63（PDI 侧的 64 路 GPIO）分布在 PC00–PC31 与 PD00–PD31。**
> 〔DS p27〕「PC00 | … ESC0_GPI_63(ALT10) ESC0_GPO_00(ALT11) … ESC0_GPI_15(ALT18)」
> 〔DS p39〕「PC31 | … ESC0_GPI_32(ALT10) ESC0_GPO_31(ALT11) … ESC0_CTR_8(ALT18)」「PD00 | … ESC0_GPI_31(ALT10) ESC0_GPO_32(ALT11) …」
> 〔DS p47〕「PD31 | … ESC0_GPI_00(ALT10) ESC0_GPO_63(ALT11) … ESC0_GPO_15(ALT18) … ESC0_MDC(ALT27)」

规律（逐行核对）：`PC00→PC31` 对应 `GPI_63→GPI_32` / `GPO_00→GPO_31`（ALT10 / ALT11）；`PD00→PD31` 对应 `GPI_31→GPI_00` / `GPO_32→GPO_63`。PD16–PD31 的 ALT18 上还有第二组 `ESC0_GPO_00…15`。

**结论 5.9：ESC 事件触发信号 ESC0_EVTI_0/1、ESC0_EVTO_0/1 的可用 pad。**
> 〔DS p26〕「PB24 … ESC0_EVTI_0(ALT27)」「PB25 … ESC0_EVTO_0(ALT27)」「PB26 … ESC0_EVTI_1(ALT27)」「PB29 … ESC0_EVTO_1(ALT27)」
> 〔DS p47〕「PE00 … ESC0_EVTO_1(ALT27)」「PE04 … ESC0_EVTI_1(ALT27)」
> 〔DS p53〕「PF18 … ESC0_EVTO_0(ALT27)」「PF20 … ESC0_EVTO_1(ALT27)」「PF21 … ESC0_EVTI_0(ALT27)」「PF22 … ESC0_EVTI_1(ALT27)」

**结论 5.6：ESC 的 PDI 是“片上总线”，没有外部 PDI 引脚；文档中未找到独立的 “ESC 复位引脚/PHY 复位引脚”定义。** 内置 PHY 的复位由内部 IO 完成：> 〔PHY p10，页脚 10/15〕「HPM_IOC->PAD[IOC_PAD_PW12].FUNC_CTL = IOC_PW12_FUNC_CTL_GPIO_W_12; /* resetn */」
> 〔PHY p10〕「HPM_IOC->PAD[IOC_PAD_PV12].FUNC_CTL = IOC_PV12_FUNC_CTL_GPIO_V_12; /* resetn */」

---

## A6. PWM0 / PWM1 通道输出引脚

**结论 6.1：PWM0 有 P_0…P_7 共 8 路，出现在 PA/PB/PC/PD/PE/PF 六个端口（ALT16）；PWM1 有 P_0…P_7，出现在 PA08/PA09、PB08–PB11、PC08–PC15、PD08–PD15、PF08–PF15（ALT16）。**
来源：DS 引脚功能表逐行检索 `PWM0_` / `PWM1_`（共 46 + 30 条），汇总：

PWM0（ALT16，`PWM0_P_n`）：
| 端口 | 可用通道 | 出处 |
|---|---|---|
| PA00–PA07 | PWM0_P_0 … PWM0_P_7 | DS p18–p19 |
| PB00–PB07 | PWM0_P_0 … PWM0_P_7 | DS p23–p25 |
| PC00–PC07 | PWM0_P_0 … PWM0_P_7 | DS p27–p30 |
| PD00–PD07 | PWM0_P_0 … PWM0_P_7 | DS p39–p41 |
| PE00–PE05 | PWM0_P_0 … PWM0_P_5 | DS p47–p49 |
| PF00–PF07 | PWM0_P_0 … PWM0_P_7 | DS p49–p51 |

PWM1（ALT16，`PWM1_P_n`）：
| 端口 | 可用通道 | 出处 |
|---|---|---|
| PA08–PA09 | PWM1_P_0, PWM1_P_1 | DS p20 |
| PB08–PB11 | PWM1_P_0 … PWM1_P_3 | DS p25 |
| PC08–PC15 | PWM1_P_0 … PWM1_P_7 | DS p31–p35 |
| PD08–PD15 | PWM1_P_0 … PWM1_P_7 | DS p42–p43 |
| PF08–PF15 | PWM1_P_0 … PWM1_P_7 | DS p51–p52 |

原样示例：
> 〔DS p18〕「G2 | D1 | 11 | PA00 | GPIO_A_00(ALT0) GPTMR1_COMP_0(ALT1) UART0_TXD(ALT2) MCAN0_TXD(ALT7) PWM0_P_0(ALT16) TRGM_P_00(ALT17) SYSCTL_CLK_OBS_0(ALT 24) | - | B00」
> 〔DS p52〕「N11 | K10 | 60 | PF12 | … PWM1_P_4(ALT16) TRGM_P_12(ALT17) ETH0_RXD_3(ALT18) OWR0_DAT(ALT26) | - | B01」

**结论 6.2：PWM 模块规格 = 2 个模块 × 8 通道 = 16 路，分辨率 100ps。**
> 〔DS p1〕「– 2 个 PWM 模块，共 16 通道高分辨 率 PWM 输出」
> 〔UM p53，页脚 6/1225〕「● 2 个 8 通道 PWM 模块 PWMV2，16 路 PWM 输出调制精度可达 100ps，支持产生互补 PWM 输出，死区插入和故障保护」
> 〔DS p95〕「PWM | 2x 8CH 2x 8CH HR-PWM / HR-PWM 100ps 100ps」——按 p95 竖线，该行在 x=297.6、x=366.7 处**有**竖线（三列独立）：HPM5E31 = 2x8CH HR-PWM 100ps，**HPM5E11 = “/”**，HPM5E3Y = 2x8CH HR-PWM 100ps。

---

## A7. UART / GPIO 数量与 PC16 / PD00

**结论 7.1：UART 数量在文档内部不一致：DS 概览写 9，DS 订购表与 UM 均写 8。**
> 〔DS p1〕「– 9 个 UART、4 个 SPI、4 个 I2C」
> 〔DS p95〕「UART | 8」
> 〔UM p53，页脚 6/1225〕「● 8 个通用异步收发器 UART ● 4 个串行外设接口 SPI ● 4 个集成电路总线 I2C」
> 〔UM p269〕内存映射中仅列出 `UART0`…`UART7`（8 个）
> **建议按 8 个 UART 设计**（两处对一处，且内存映射只到 UART7）。

**结论 7.2：GPIO = 148（BGA196）/ 72（eLQFP100）/ 72（BGA121）。**
> 〔DS p1〕「– 148 个 GPIO」
> 〔DS p95〕「GPIO | 148(BGA196),72(eLQFP100),72(BGA121)」
> 〔UM p53〕「● 2 个正交编码器输入 QEIV2」等同系列描述

**结论 7.3：PC16 = GPIO_C_16（GPIO C 口第 16 脚）；PD00 = GPIO_D_00（GPIO D 口第 0 脚）；二者均属系统电源域普通 IOC pad、供电分组 B00。**
> 〔DS p35，页脚 32/95〕「D8 | A10 | 83 | PC16 | GPIO_C_16(ALT0) GPTMR3_COMP_0(ALT1) UART4_TXD(ALT2) ESC0_GPI_47(ALT10) ESC0_GPO_16(ALT11) PPI0_DQ_16(ALT13) XPI0_CA_DQS(ALT14) ESC0_CTR_7(ALT27) | - | B00」
> 〔DS p39，页脚 36/95〕「D9 | B10 | 81 | PD00 | GPIO_D_00(ALT0) MCAN0_TXD(ALT7) ESC0_GPI_31(ALT10) ESC0_GPO_32(ALT11) PPI0_DQ_02(ALT13) PWM0_P_0(ALT16) TRGM_P_00(ALT17) ESC0_REFCK(ALT18) ESC0_CTR_0(ALT27) | - | B00」
> 〔UM p347，页脚 344/1225〕「D8 | A10 | 83 | PC16 | GPIO_C_16(ALT0) GPTMR3_COMP_0(ALT1) UART4_TXD(ALT2) ESC0_GPI_47(ALT10) ESC0_GPO_16(ALT11) PPI0_DQ_16(ALT13) XPI0_CA_DQS(ALT14) ESC0_CTR_7(ALT27) | - | B00」
> 〔UM p351，页脚 348/1225〕「D9 | B10 | 81 | PD00 | GPIO_D_00(ALT0) MCAN0_TXD(ALT7) ESC0_GPI_31(ALT10) ESC0_GPO_32(ALT11) PPI0_DQ_02(ALT13) PWM0_P_0(ALT16) TRGM_P_00(ALT17) ESC0_REFCK(ALT18) ESC0_CTR_0(ALT27) | - | B00」

**结论 7.4：PDF 中未找到“PC/PD 端口需要额外配置 PIOC/BIOC”的说明。**
* 检索词：`PIOC`、`BIOC`、`PC 端口`、`PD 端口`、`IOC_PAD_PC16`、`电源域 IO`。`BIOC` 在 UM/DS 中 **0 次命中**。
* PDF 中 PIOC 仅作为电源管理域模块出现：
  > 〔UM p49〕「PIOC | 电源管理域 IO 控制器」
  > 〔UM p271〕「0xF4118000        0xF411BFFF        16 KBytes        PIOC        N/A」
  > 〔UM p418，页脚 371/1225〕「IOC base address: 0xF4040000」
  > 〔UM p418〕「PIOC base address: 0xF4118000」
* **工程内交叉印证（非 PDF，仅供硬件确认）**：`sdk_env_v1.13.0/user_template/boards/ecat_drone_5e3y/pinmux.c` 的注释写明该约束只针对 **PY/PZ**：
  > 「PY and PZ IOs: if any SOC pin function needs to be routed to these IOs, besides of IOC, PIOC/BIOC needs to be configured SOC_GPIO_X_xx」
  即 PC/PD 不属于该约束范围（PC16/PD00 在该板级文件里直接写 `HPM_IOC->PAD[IOC_PAD_PC16]` / `[IOC_PAD_PD00]`）。

---

## A8. 启动 / Flash

**结论 8.1：启动模式由 BOOT_MODE[1:0] = PA03:PA02 两个引脚决定；00 = XPI NOR 启动，01/10 = ISP/串行启动，11 = 保留。**

> 〔DS p64，页脚 61/95〕「2.6 特殊功能引脚 芯片默认是通过 BOOT_MODE[0:1]=[PA02:PA03] 引脚选择三种不同的启动模式，启动配置如表 5。」
> 〔DS p64〕「0        1        在系统编程」（表 5 第 2 行，其说明列为「从 UART0/USB0 上烧写固件，」「OTP，或从 UART0/USB0 上启」）
> 〔DS p64〕「1        1        保留模式」
> 〔UM p274，页脚 227/1225〕「BootROM 支持如下启动模式：● 主启动模 (XPI NOR 启动模式)。● 在系统编程模式 (In-System-Programming)」
> 〔UM p324〕「BOOT_MODE | 启动模式选择 0 - 启动模式由 BOOT_MODE 引脚决定 ● 0 - 从 XPI NOR 启动 ● 1 - 在系统编程模式 ● 2 - 在系统编程模式 ● 其他 - 保留」

**结论 8.2：Flash 地址空间 = XPI0，0x80000000–0x8FFFFFFF（256 MB 窗口）。文档没有一句“从 0x80000000 开始执行”，启动地址是由 XPI0 映射 + 镜像布局推得。**
> 〔UM p269，页脚 222/1225〕「0x80000000 | 0x8FFFFFFF | 256 MBytes | XPI0 | N/A」
> 〔UM p269〕「0x20000000 | 0x2001FFFF | 128 KBytes | ROM」（BootROM）

**结论 8.3：XPI NOR 启动镜像布局（FLASH 起始地址 + 偏移）。**
> 〔UM p277，页脚 230/1225〕「启动镜像0」（图 16 的行标签；同图列出「XPI FLASH 配置选项」「FW BLOB 固件容器」「固件容器头」等分区及其偏移）
> 〔UM p276〕「并从 FLASH 的 0x400 地址读取 FLASH 配置信息」
> 〔UM p276〕「1. 从FLASH 0x1000地址读取FW Container Header」
>
> 按图 16 的偏移列：`启动镜像0` 位于 FLASH 起始地址；`XPI FLASH 配置选项` 位于 +0x400；`FW BLOB` 位于 +0x800；`固件容器头` 位于 +0x1000；第二份镜像（可选，OTP `SEC_IMG_OFFSET` 非 0 时有效）位于 `FLASH 起始地址 + 第二份镜像偏移`。
> 〔UM p277〕「启动镜像1 该镜像是可选的, 仅 当OTP 中的 SEC_IMG_OFFSET 值 为非0时才有效」

**结论 8.4：“NOR 配置选项”指的是 XPI NOR Configuration Option 结构（存于 Flash 0x400），可由 OTP 或 Flash 提供。**
> 〔UM p276〕「当 FLASH 配置信息来自 OTP 时: ● 根据 XPI_FREQ_OPTION 决定 FLASH 读操作的时钟频率 ● 根据 PROBE_TYPE 决定 FLASH 的类型 ● 根据 XPI_DRIVE_STRENGTH 决定 XPI IO 的驱动强度 ● 根据 DUMMY_CYCLE 决定 read 时 dummy cycle 的值 ● 根据 XPI_IO_VOLTAGE 决定 FLASH 的 IO 电压」
> 流程图中另出现字段名 XPI_PIN_GROUP、XPI_PORT_SEL、XPI_INSTANCE（图内文字，与流程线混排，不作逐字引文）。
> 〔UM p300，页脚 253/1225〕「0x020 | device_info | 0x50 | 详细定义见69」「0x070 | instruction_set | 0x90 | 标准的 XPI NOR 指令集，分 9 组」

**结论 8.5：内置 Flash 是合封 NOR（不是 MCU 内部嵌入式 Flash），因此“内部 flash + XPI”是同一件事：XPI0 上挂着合封的 1MB NOR。**
> 〔DS p68〕「本产品内置 1MB 闪存。」
> 〔DS p68〕「产品型号HPM5E31IGN1，HPM5E11IGN1，HPM5E31IPB1，HPM5E11IPB1的内置闪存型号为GT25Q80AH-」
> 〔DS p68〕「产品型号 HPM5E3YIGN1，HPM5E3YIVK1，HPM5E31IVK1，HPM5E11IVK1 的内置闪存型号为 P25Q80SU-」
> 〔DS p68〕「4.3 内置以太网 PHY 特性」（同页，说明这些“内置”器件都是合封）
> 〔UM p269〕XPI0 窗口 0x80000000–0x8FFFFFFF，Flash 即映射于此。

**结论 8.6：XPI 能力（可外扩 NOR/PSRAM）。**
> 〔DS p11，页脚 8/95〕「1 个串行总线控制器 XPI，可以连接片外的各种 SPI 串行存储设备… 支持 1/2/4/8 位数据模式，支持 2 个 CS 片选信号… 支持 SDR 和 DDR，最高支持 166MHz… 支持 Quad-SPI 和 Octal-SPI 的串行 NOR Flash」
> 〔UM p269〕另有 `0xB0000000 | 0xBFFFFFFF | 256 MBytes | HYBRID_XPI(256KB）`、`0x30000000 | 0x300FFFFF | 1 MBytes | DM`

---

# B. 片内 MII PHY 使用指南（PHY 文档）

## B1. 连接关系与硬件要求

> 〔PHY p5，页脚 5/15〕「针对 HPM5E00 系列微控制器的 EtherCAT 功能，部分型号片内合封(SIP)了两颗 MII PHY。两颗 MII PHY 在 MCU 内部，引出的 IO 直接与百兆网口变压器连接，然后通过 RJ45 连接网线。」
> 〔PHY p7，页脚 7/15〕「其中，T3 和 T30 为两颗百兆网口变压器，U10 为外部 EEPROM 可以选用，如果不外挂 EEPROM，可以 Flash 模拟的 EEPROM，从而节省物料。」

**引脚与 strap（PHY p5 表，页脚 5/15）**：
> 「PA16 | （端口1）RD_N」「PA17 | RD_P」「PA18 | TD_N」「PA19 | TD_P」
> 「PA20 | （端口0）RD_N」「PA21 | RD_P」「PA22 | TD_N」「PA23 | TD_P」
> 「PA24 | RBIAS（端口1）| 2.49Kohm 1% 对地」「PA27 | RBIAS（端口0）| 2.49Kohm 1% 对地」
> 「PA25 | LED1 | 端口 1 | 地址 1」「PA26 | LED0 | 端口 1 | 地址 0」
> 「PA28 | LED1 | 端口 0 | 地址 1」「PA29 | LED0 | 端口 0 | 地址 0」
> 「PA30 | MDIO | MDIO | 外加上拉，不要用作其他用途」
> 「PA31 | MDC | MDC | 外加上拉，不要用作其他用途」

**PHY 地址与 strap 电阻（UM p1193）**：
> 「LED 引脚可用于设置 PHY 地址，其输入值在上电和复位时被读取，建议使用 4.7K 欧姆电阻进行上拉或下拉。」
> 「偏置电阻引脚需通过 2.49K 欧姆电阻接地。」「差分数据引脚连接变压器。」
> 「MDIO 总线为 2 个以太网 PHY 复用，并通过引脚引出，用户可将外部的其他 PHY 的 MIDO 与其连接在一起。」

**供电/去耦（UM p1192）**：
> 「内置以太网 PHY 的 3.3V 供电复用 VIO_B01 电源引脚，用户需要将 VIO_B01 连接 3.3V 电源。内置以太网 PHY 的数字部分的 1.2V 供电由 PHY 内部 LDO 实现，该 LDO 需通过 VDD_PHY0CAP 和 VDD_PHY1CAP 两个引脚在外部连接 10uF 和 0.1uF 的去耦电容。」
> 〔UM p1194〕「PHY 的 EN_DLDO 端口拥有内部上拉，主芯片不对其进行驱动即为默认的使用 PHY 内部 LDO 为 1.2V 供…」（该页文字有截断）

**端口地址（PHY p5）**：
> 「SDK 中 EtherCAT 的例程，是以 HPM5E00EVK 中定义的 ECAT 端口的地址来定义的。即 ECAT P0 地址为 0x01，ECAT P1 地址为 0x10。」

## B2. REFCK / 时钟（最关键约束）

> 〔UM p1099，页脚 1052/1225〕「ESC 模块使用芯片时钟系统提供的 100MHz 功能时钟作为单一时钟源，该功能时钟经过 4 分频产生 25MHz 时钟，该 25MHz 时钟从 REFCK IO 输出，作为 PHY 的时钟源输入。」
> 〔UM p1099〕「ESC 模块和以太网 PHY 必须使用同一时钟源，不支持 ESC 模块使用芯片内部功能时钟而 PHY 使用外部 25MHz 晶振。」
> 〔UM p1099〕「100MHz 时钟带有门控功能，其使能寄存器为 ESC.GPR_CFG0[CLK100_EN]。25MHz 输出时钟 REFCK 带有门控功能，其使能寄存器为 ESC.PHY_CFG1[REFCK_25M_OE]。I2C 接口时钟 SCL 带有门控功能，其使能寄存器为 ESC.GPR_CFG[I2C_SCLK_EN]。」
> 〔UM p1099〕「ESC 模块提供 2 个寄存器 ESC_RST_ECAT 和 ESC_RST_PDI，可分别被 EtherCAT 主站和从站 CPU 访问以触发复位请求…复位请求能够触发中断和以太网 PHY 的复位。」

**结论：REFCK 是 25 MHz 输出（100MHz/4），不是 50MHz；文档中未找到 50MHz REFCK 用法。**（检索词 `50M`、`REFCK`、`25MHz`）

## B3. 官方初始化序列（init_esc_pins）

> 〔PHY p8，页脚 8/15〕「1）新建 board」「1．复制一份 hpm5e00evk 目录并重命名为 hpm5e00sip」
> 〔PHY p9，页脚 9/15〕「5．board 新建完成」「1）内置 MII PHY 软件修改」「修改 init_esc_pins 函数」
> 〔PHY p9〕「文件位于 boards/hpm5e00sip/pinmux.c，将其内容替换为:」
> 〔PHY p9–p11〕代码顺序（节选，逐行摘自 pinmux.c 代码块）：
> 「HPM_IOC->PAD[IOC_PAD_PW20].FUNC_CTL = IOC_PW20_FUNC_CTL_ESC0_REFCK;」
> 「HPM_IOC->PAD[IOC_PAD_PW06].FUNC_CTL = IOC_PW06_FUNC_CTL_ESC0_P1_TXCK;」
> 「… PW11 TXEN / PW07..PW10 TXD_0..3 / PW05 RXCK / PW00 RXDV / PW15 RXER / PW01..PW04 RXD_0..3」
> 「HPM_IOC->PAD[IOC_PAD_PW21].FUNC_CTL = IOC_PW21_FUNC_CTL_ESC0_REFCK;」
> 「… PV06 P0_TXCK / PV11 TXEN / PV07..PV10 TXD_0..3 / PV05 RXCK / PV00 RXDV / PV15 RXER / PV01..PV04 RXD_0..3」
> 「HPM_IOC->PAD[IOC_PAD_PW12].FUNC_CTL = IOC_PW12_FUNC_CTL_GPIO_W_12; /* resetn */」
> 「HPM_IOC->PAD[IOC_PAD_PV12].FUNC_CTL = IOC_PV12_FUNC_CTL_GPIO_V_12; /* resetn */」
> 「HPM_IOC->PAD[IOC_PAD_PA30].FUNC_CTL = IOC_PA30_FUNC_CTL_ESC0_MDIO;」「…PA31…ESC0_MDC;」
> 「HPM_IOC->PAD[IOC_PAD_PA16].FUNC_CTL = IOC_PAD_FUNC_CTL_ANALOG_MASK; /* RD_N */」…（PA17–PA23 同为 ANALOG_MASK）
> 「HPM_IOC->PAD[IOC_PAD_PA24].FUNC_CTL = IOC_PAD_FUNC_CTL_ANALOG_MASK; /* RBIAS1 */」
> 「HPM_IOC->PAD[IOC_PAD_PA27].FUNC_CTL = IOC_PAD_FUNC_CTL_ANALOG_MASK; /* RBIAS0 */」
> 「HPM_IOC->PAD[IOC_PAD_PW16].FUNC_CTL = IOC_PAD_FUNC_CTL_ANALOG_MASK; /* PHY1 LDO */」
> 「HPM_IOC->PAD[IOC_PAD_PW17].FUNC_CTL = IOC_PAD_FUNC_CTL_ANALOG_MASK; /* PHY0 LDO */」
> 「HPM_IOC->PAD[IOC_PAD_PA25].FUNC_CTL = IOC_PA25_FUNC_CTL_ESC0_CTR_0; /* PHY1 LED1 */」
> 「HPM_IOC->PAD[IOC_PAD_PA25].PAD_CTL = IOC_PAD_PAD_CTL_PE_SET(1) | IOC_PAD_PAD_CTL_PS_SET(1);」
> 「HPM_IOC->PAD[IOC_PAD_PA26].FUNC_CTL = IOC_PAD_FUNC_CTL_ANALOG_MASK; /* PHY1 LED0 */」
> 「HPM_IOC->PAD[IOC_PAD_PA28].FUNC_CTL = IOC_PA28_FUNC_CTL_ESC0_CTR_1; /* PHY0 LED1 */」（同样带 PE/PS 上拉）
> 「HPM_IOC->PAD[IOC_PAD_PA29].FUNC_CTL = IOC_PAD_FUNC_CTL_ANALOG_MASK; /* PHY0 LED0 */」
> 「//FIT HPM5E00EVK, NOT MUST — HPM_IOC->PAD[IOC_PAD_PD08].FUNC_CTL = IOC_PD08_FUNC_CTL_ESC0_SCL; / PD09 … ESC0_SDA;」

**board.h 宏修改（PHY p11）**：
> 〔PHY p11，页脚 11/15〕「#define BOARD_ECAT_PHY0_RESET_GPIO_PORT_INDEX       GPIO_DI_GPIOV」
> 〔PHY p11〕「#define BOARD_ECAT_PHY0_RESET_PIN_INDEX        (12)」
> 〔PHY p11〕「#define BOARD_ECAT_PHY1_RESET_GPIO_PORT_INDEX       GPIO_DI_GPIOW」
> 〔PHY p11〕「#define BOARD_ECAT_NMII_LINK0_CTRL_INDEX 1」
> 〔PHY p12，页脚 12/15〕「#define BOARD_ECAT_SUPPORT_PORT1 (1)」
> 〔PHY p12〕「#define BOARD_ECAT_SUPPORT_PORT2 (0)」（Y 型号只有 2 个端口，故关掉 PORT2）
> 〔PHY p12〕「修改 BOARD_ECAT_SUPPORT_PORT2 的原因并不是不支持 3 个 PORT 口，而是本」

## B4. 未使用的 PHY 处理

> 〔PHY p7，页脚 7/15〕「对于未使用的内置 PHY,用户需做如下处理：」
> 〔PHY p7〕「保持未用 PHY 的端口的电路完整，即端口地址的配置和 RBIAS 电阻的连接，其余引脚浮空」
> 〔PHY p7〕「不要使用未用 PHY 的相关引脚的数字功能」
> 〔PHY p7〕「init_esc_pins 函数配置保持不变」

## B5. 测试 / loopback

**未找到 loopback（环回）测试流程。** 检索词：`loopback`、`回环`、`环回`、`自环`、`测试`（PHY 文档 15 页全文）。文档只说明：
> 〔PHY p14，页脚 14/15〕「内置 MII PHY 与外置 MII PHY 测试例程及方法相同，均可使用刚创建的 ecat_io 工程测试 EtherCAT 通信。」
> 〔PHY p13〕「在“SDK Samples”下拉列表中选择“ ethercat/ecat_io”…在“Build Type”下拉列表中选择“flash_xip”」

---

# C. J288 / S288 舵机手册（最关键）

## C1. 电气与物理层

**结论 1.1：供电 12V 或 25.2V（同一接口，型号/批次不同）；接口为 3 芯 PH2.0。**
> 〔SERVO p3〕「管脚序号 信号 描述」「① SIGNAL 信号通讯」「② VCC 12V /25.2V供电」「③ GND 地线」
> 〔SERVO p1〕「数字舵机*1 PH 2.0线缆*1」

**结论 1.2：通信 = 单总线（1-Wire）半双工，波特率固定 6 Mbps，8N1。**
> 〔SERVO p5〕「我们使用单总线（1-Wire）作为物理层。… 单总线仅利用一根数据线（以及公共地线）进行双向数据传输。」
> 〔SERVO p5〕「由于单总线仅使用一根数据线进行双向通信，因此同一时刻总线上只能进行一个方向的通信，即半双工通信。总线通常由一个主机（上位机）控制通信时序。」
> 〔SERVO p5〕「舵机通过单总线接口与上位机通信，通信速率（波特率）固定为：6Mbps（8N1）。」
> 〔SERVO p7〕「J288/S288 舵机采用串口通信，通信标准为单总线（1-Wire），波特率为 6.0 Mbps。串口的数据位为 8 bit，无奇偶校验位，停止位为 1 bit。」
> 〔SERVO p7〕「用户需要检查自己的硬件是否支持这么高的波特率。如果您的硬件无法支持该波特率，可以使用 Unitree 的 USB 转单总线（1-Wire）模块。」

**结论 1.3：总线容量 = 15 个舵机（地址 0–14，15 为广播）；不允许地址重复。**
> 〔SERVO p5〕「J288/S288 舵机最大支持一条总线上连接 15 个舵机（地址 0~14，15 为广播地址）。总线上不得有地址相同的舵机，否则整个总线通信将出现异常。」

**结论 1.4：空闲电流 / 峰值电流 —— 未找到。** 检索词：`电流`、`A`、`mA`、`功率`、`W`（SERVO 全文 11 页）。手册无任何电流/功率数值。

**结论 1.5：TX/RX 方向切换（外部换向电路、DE/RE 时序）—— 未找到。** 检索词：`方向`、`切换`、`收发`、`换向`、`DE`、`RE`、`推挽`、`开漏`。手册只描述“单总线半双工、主机控制时序”，未给出方向控制引脚或时序参数。
* 交叉印证（非 PDF）：工程板级 `pinmux.c` 把 **PC16/UART2、PD00/UART3** 定义为“servo direction switch control”，即硬件上由 MCU 的 GPIO 控制外部单总线换向器。此事实**不在 PDF 中**。

## C2. 协议帧格式

**结论 2.1：主机 → 舵机固定 20 字节；舵机 → 主机固定 26 字节（不是变长协议、无长度字段）。**
> 〔SERVO p7〕「在控制舵机时，我们会通过串口给舵机发送一个长度为 20 字节的命令，之后舵机会返回一个长度为 26 字节的信息。如果不给舵机发送命令，那么舵机也不会返回状态。」

**结论 2.2：控制帧结构（20 字节，小端）。**
> 〔SERVO p8〕
> 「uint8_t head[2]; // 帧头 0xFE 0xEE 参与 CRC」
> 「uint8_t id : 4; // 舵机 ID: 0-14, 15 表示向所有舵机广播数据 ( 此时无返回 )」
> 「uint8_t status : 3; // 工作模式 : 0. 锁定 1.FOC 闭环」
> 「uint8_t timeout : 1; // Master->Motor: 0. 禁用超时保护 1. 开启 （默认 1s 超时）」
> 「uint8_t res; // 保留位」
> 「int16_t tor_des; // 期望转子输出扭矩 unit: N.m 256000 表示 1NM」
> 「int16_t spd_des; // 期望转子输出速度 unit: rad/s 2.56/2π表示 1rad/s」
> 「int32_t pos_des; // 期望转子输出位置 unit: rad 32768/2π表示 1rad」
> 「int16_t k_pos; // 期望转子刚度系数 unit: N.m/rad 1280000 表示 1」
> 「int16_t k_spd; // 期望转子阻尼系数 unit: N.m/(rad/s) 128000000 表示 1」
> 「uint32_t CRC32; // CRC32」

**字节序（原文明确）**：
> 〔SERVO p7〕「并且，对于 2 个字节的 τ 变量来说，它的低位在前，即第 5 字节，高位在后，即第 6 字节。」
> 〔SERVO p7〕「第 5 和第 6 字节表示舵机前馈力矩τ_(ﬀ )，其中 256000 对应 1 牛·米 (NM)。… 乘以了 256000，然后将其赋值给一个 2 字节的带符号短整型 (signed short int)」

**结论 2.3：校验 = CRC32（多项式 0x04C11DB7，初值 0xFFFFFFFF，逐 32-bit 字处理，非反射/非异或输出）。**
> 〔SERVO p9–p10〕
> 「// CRC32 校验 uint32_t crc32_core(uint32_t *ptr, uint32_t len) { uint32_t xbit = 0; uint32_t data = 0; uint32_t CRC32 = 0xFFFFFFFF; const uint32_t dwPolynomial = 0x04c11db7; …」
> 〔SERVO p7〕「在发送命令和接收状态的末尾，我们可以看到一个 4 字节的 CRC32 校验。」
> 〔SERVO p8〕「如果数据传输过程中没有发生任何错误，那么发送前 CRC 校验值等于发送后 CRC 校验值。」

**结论 2.4：CRC 覆盖范围。** 控制帧帧头标注「参与 CRC」；反馈帧帧头标注「不参与 CRC」。
> 〔SERVO p8〕「uint8_t head[2]; // 帧头 0xFE 0xEE 参与 CRC」
> 〔SERVO p9〕「uint8_t head[2]; // 帧头 0xFC 0xEE 不参与 CRC」
> 〔SERVO p9〕「uint32_t crc32_core(uint32_t *ptr, uint32_t len)」——**按 32-bit 字计算**。
>
> **注意（推断，非原文）**：手册未给出“从哪个字节开始算 CRC”“len 的单位是字还是字节”“初值/结果是否取反”的明文，也没有算例。按结构对齐可得到自洽推测：
> * 控制帧：CRC 位于 16–19，前面正好 16 字节 = 4 个字 → CRC 覆盖 **byte 0–15**（帧头参与）；
> * 反馈帧：CRC 位于 22–25，若帧头不参与，则覆盖 **byte 2–21**（20 字节 = 5 个字，起始地址 4 字节对齐）。
>
> 该推测与「参与/不参与 CRC」的注释和 4 字节对齐要求一致，但**必须实机验证**后再写进固件。

**结论 2.5：广播 ID = 15，广播时无返回。**
> 〔SERVO p8〕「舵机 ID: 0-14, 15 表示向所有舵机广播数据 ( 此时无返回 )」

## C3. 指令 / 命令列表

**结论 3.1：本协议没有指令码/命令字。** 没有 ping、读寄存器、写寄存器、设置 ID、设置模式等独立命令；只有上面这一种固定 20 字节控制帧，以及固定 26 字节状态回帧。
> 〔SERVO p7〕「在控制舵机时，我们会通过串口给舵机发送一个长度为 20 字节的命令，之后舵机会返回一个长度为 26 字节的信息。」
> 〔SERVO p8–p9〕全文仅定义 `ControlData_t`（20B）与 `MotorData_t`（26B）两个结构体，无 opcode 字段。
* ID 修改、固件升级、版本查询等只能通过上位机“舵机调试助手”完成，协议未公开：
  > 〔SERVO p6〕「通过舵机调试助手，您可以对舵机执行以下配置操作：」
  > 〔SERVO p6〕「查询及修改 ID」
  > 〔SERVO p6〕「更新固件」
  > 〔SERVO p6〕「恢复舵机模式」
* `status`（3 bit）是唯一可切换的工作模式：`0` 锁定、`1` FOC 闭环（见 C7）。

## C4. 寄存器 / 内存表

**结论 4.1：手册没有寄存器地址表。** 所有量都是上述两个结构体的字段，直接映射到帧字节。字段、单位、缩放、默认值如下：

发送（20B，`ControlData_t`，SERVO p8）：

| 偏移(字节) | 字段 | 类型 | 单位/缩放 | 说明 |
|---|---|---|---|---|
| 0–1 | head[2] | u8×2 | — | 固定 `0xFE 0xEE`，参与 CRC |
| 2 | id:4 / status:3 / timeout:1 | 位域 | — | id 0–14，15=广播；status 0=锁定 1=FOC闭环；timeout 0=禁用超时 1=开启（默认 1s） |
| 3 | res | u8 | — | 保留 |
| 4–5 | tor_des | int16 | 256000 ↔ 1 N·m | 前馈力矩，低字节在前 |
| 6–7 | spd_des | int16 | 2.56/2π ↔ 1 rad/s | 期望速度 |
| 8–11 | pos_des | int32 | 32768/2π ↔ 1 rad | 期望位置 |
| 12–13 | k_pos | int16 | 1280000 ↔ 1 | 位置刚度 kp |
| 14–15 | k_spd | int16 | 128000000 ↔ 1 | 阻尼 kd |
| 16–19 | CRC32 | u32 | — | CRC32 校验 |

反馈（26B，`MotorData_t`，SERVO p9）：
> **注意偏移**：结构体中 `NoUse[2]` 是 4 字节对齐填充，「通信中不存在…在发送通信数据时必须去除该数据位」，因此**线上偏移 = 结构体偏移 − 2**。下表两列都给。

| 结构体偏移 | **线上偏移** | 字段 | 类型 | 单位/缩放 | 说明 |
|---|---|---|---|---|---|
| 0–1 | —（不传输） | NoUse[2] | u8×2 | — | 「通信中不存在（结构体 4 字节对齐），在发送通信数据时必须去除该数据位」 |
| 2–3 | **0–1** | head[2] | u8×2 | — | 固定 `0xFC 0xEE`，不参与 CRC |
| 4 | **2** | mode（RIS_Mode_t） | 位域 | — | id:4 / status:3 / timeout:1；Motor→Master 时 timeout 位 0=没有超时 1=触发超时保护（需要控制位发 0 清除） |
| 5 | **3** | temp | int8 | °C | 壳体温度 −128~127°C |
| 6 | **4** | sensor | u8 | °C | 绕组温度 0–255°C |
| 7 | **5** | vol | u8 | 0–127.5V（255=127.5V） | 舵机端电压 |
| 8–9 | **6–7** | torque | int16 | 256000 ↔ 1 N·m | 当前转子扭矩 |
| 10–11 | **8–9** | speed | int16 | 2.56/2π ↔ 1 rad/s | 当前转子速度 |
| 12–15 | **10–13** | pos | int32 | 32768/2π ↔ 1 rad | 当前转子位置 |
| 16–19 | **14–17** | MError | u32 | 位掩码 | 错误码（见 C5） |
| 20–21 | **18–19** | OutPos:13 / ExFlag:3 | 位域 | OutPos：2^13 ↔ 1 圈 | 输出端传感器位置（低 13 位）、警告码（高 3 位） |
| 22–23 | **20–21** | res | u16 | — | 保留 |
| 24–27 | **22–25** | CRC32 | u32 | — | CRC32（结构体总 28B，去掉 NoUse 后 26B 正好与手册一致） |

> 推算依据：2(head)+1(mode)+1(temp)+1(sensor)+1(vol)+2(torque)+2(speed)+4(pos)+4(MError)+2(OutPos/ExFlag)+2(res)+4(CRC) = **26**，与手册「长度为 26 字节的信息」一致。

**缩放记号的歧义（重要）**：手册写法「256000 表示 1NM」「2.56/2π表示 1rad/s」「32768/2π表示 1rad」是“整数值 ↔ 物理量”的比例说明，但**乘除方向只在力矩项被明确**（§C2：「乘以了 256000，然后将其赋值给…signed short int」，即 `整数 = 256000 × N·m`）。
> * 力矩：`int16 = round(N·m × 256000)`，上限 ±0.128 N·m〔SERVO p7〕
> * 位置：`int32 = rad × 32768/2π`（即 2π rad = 32768 LSB，一圈 = 32768）——方向由“32768/2π 表示 1rad”推得，建议实测确认
> * 速度：`2.56/2π` 的乘除方向**未明说**，务必实测标定后再写固件

**结论 4.2：EEPROM vs RAM —— 未找到。** 手册未提及参数掉电保存、默认参数表或 EEPROM 概念。检索词：`EEPROM`、`掉电`、`保存`、`默认`、`参数`。

## C5. 反馈 / 状态解码

**结论 5.1：返回量包括位置、速度、扭矩、电压、壳体温度、绕组温度、错误码、警告码、输出端绝对位置。**
> 〔SERVO p9〕「int8_t temp; // 壳体温度 : -128~127°C」「uint8_t sensor; // 绕组温度 : 0-255°C」「uint8_t vol; // 舵机端电压 0-127.5V 255 表示 127.5V」「int16_t torque; // 当前转子扭矩 256000 表示 1NM」「int16_t speed; // 当前转子速度 2.56/2π表示 1rad/s」「int32_t pos; // 当前转子位置 32768/2π表示 1rad」「uint32_t MError; // 舵机错误码 : 0x0. 正常 bit0 过流 ,...」「uint16_t OutPos : 13; // 输出端传感器 2^13 表示 1 圈」「uint16_t ExFlag : 3; // 警告码」

**结论 5.2：故障码表（MError，位掩码，1 = 故障）。**
> 〔SERVO p11〕表 1「舵机故障码表」：
> 「0x01 | 0 | 过流」「0x02 | 1 | 瞬态过压」「0x04 | 2 | 持续过压」「0x08 | 3 | 欠压」
> 「0x10 | 4 | 芯片过热」「0x20 | 5 | MOS 过热/冷」「0x40 | 6 | MOS 温度异常」「0x80 | 7 | 壳体过热/冷」
> 「0x100 | 8 | 壳体温度异常」「0x200 | 9 | 绕组过热」「0x400 | 10 | 转子编码器 1 错误」「0x800 | 11 | 转子编码器 2 错误」
> 「0x1000 | 12 | 输出编码器错误」「0x2000 | 13 | 储存数据错误」「0x4000 | 14 | 异常复位」「0x8000 | 15 | 等待解锁」
> 「0x10000 | 16 | 驱动验证错误」「0x20000 | 17 | 标定模式」「0x40000 | 18 | 通信校验错误」「0x80000 | 19 | 驱动版本过低」
> 「0x100000 | 20 | 固件型号错误」「0x200000 | 21 | 编码器角度异常」「0x... | ... | 未知错误」

**结论 5.3：警告码表（ExFlag，3 bit）仅给出 1 项 + 未知。**
> 〔SERVO p11〕表 2「舵机警告码表」：「0x01 | 0 | 低压电源异常」「0x... | ... | 未知警告」
> **注**：ExFlag 只有 3 bit，但表中给出 0x01（bit0）；其余位未定义。

**结论 5.4：减速比 = 288.35，协议中所有量都是转子端数据。**
> 〔SERVO p7〕「协议中的所有参数均为转子端数据，减速比为 288.35，若需将转子数据换算为输出端数据，则需乘以或除以该减速比。」

## C6. 轮询间隔、超时、多机菊花链

**结论 6.1：超时保护默认 1s，可通过控制帧 timeout 位开启/关闭。**
> 〔SERVO p8〕「uint8_t timeout : 1; // Master->Motor: 0. 禁用超时保护 1. 开启 （默认 1s 超时）」
> 〔SERVO p8〕「// Motor->Master: 0. 没有超时 1. 触发超时保护 ( 需要控制位发 0 清除 )」
* **推荐轮询间隔：未找到**（手册无此数值）。检索词：`周期`、`频率`、`Hz`、`间隔`、`轮询`、`ms`。
* 可推算的上限：6 Mbps 8N1 下 20B 命令 ≈ 33.3 µs、26B 回帧 ≈ 43.3 µs（本摘要按位宽计算，非文档原文）。

**结论 6.2：不是菊花链，而是并联单总线；** 所有舵机接在同一根数据线上，靠 ID 寻址。
> 〔SERVO p5〕「主机发送的指令中包含目标设备地址信息，总线上所有设备均会接收该指令，但只有地址匹配的设备会响应并回复数据，从而完成一轮通信。」
> 〔SERVO p5〕「总线上不得有地址相同的舵机，否则整个总线通信将出现异常。」

## C6b. J288 vs S288、型号差异、ID 分配

**结论 6b.1：J288 与 S288 的差异在机械安装（螺丝/安装孔），电气与协议相同。**
> 〔SERVO p1〕「无刷数字舵机J288/S288使用手册」（同一本手册覆盖两个型号）
> 〔SERVO p2〕「J288：S288：使用M2机牙螺丝 4-M2螺纹 使用M2自攻螺丝 6- …」（同页两张尺寸图分别标注 J288 / S288）
* 手册**未找到**“J288 与 S288 电气/协议差异”的任何说明（检索词 `区别`、`差异`、`不同`、`J288`、`S288`）。

**结论 6b.2：ID 分配流程 —— 手册只说明“用舵机调试助手查询及修改 ID”，无协议级步骤。**
> 〔SERVO p6〕「通过舵机调试助手，您可以对舵机执行以下配置操作：」
> 〔SERVO p6〕「查询及修改 ID」
> 〔SERVO p5〕「总线上不得有地址相同的舵机」

## C7. 位置模式 / 速度模式、力矩限制、温度保护

**结论 7.1：没有独立的“位置模式/速度模式”寄存器；模式只有 `status` 的 0=锁定、1=FOC 闭环。位置/速度通过同一帧里的 `pos_des/spd_des` 与 `k_pos/k_spd` 的增益组合实现（混合控制）。**
> 〔SERVO p8〕「uint8_t status : 3; // 工作模式 : 0. 锁定 1.FOC 闭环」
> 〔SERVO p4〕「与其他舵机的控制方式不同，本舵机使用的控制方式为机器人领域的混合控制。」
> 〔SERVO p5〕「宇树科技的数字舵机包含如下 5 个控制指令：1. 前馈力矩：τ_(ﬀ ); 2. 期望角度位置：p_(des ); 3. 期望角速度：ω_(des ); 4. 位置刚度：k_p; 5. 阻尼系数：k_d;」
> 〔SERVO p5〕「τ=τ_ﬀ+k_p×(p_des-p)+k_d×(ω_des-ω)」
> 〔SERVO p5〕「在实际使用数字舵机时，需要注意将舵机输出端的控制目标量与发送的舵机转子的指令进行换算。」

**结论 7.2：力矩限制源于 int16 表示范围，文档给出明确上限。**
> 〔SERVO p7〕「这意味着命令中τ_(ﬀ ) 的数值不能 2^15 超过特定上限。考虑到我们曾对原始数据τ_(ﬀ ) 乘以了 256000，因此其绝对值存在相应的上限值：|τ_ﬀ | <2^15/256000=0.128」
> 〔SERVO p7〕「由于赋值过程中存在强制取整操作，τ_(ﬀ ) 因此数值越大，保存的小数精度就越低。」

**结论 7.3：温度保护阈值 —— 未找到具体数值。** 手册只给出温度**读数**（壳体 −128~127°C、绕组 0–255°C）与**故障位**（芯片过热 bit4、MOS 过热/冷 bit5、MOS 温度异常 bit6、壳体过热/冷 bit7、壳体温度异常 bit8、绕组过热 bit9），未给出触发阈值。
> 〔SERVO p9〕「int8_t temp; // 壳体温度 : -128~127°C」「uint8_t sensor; // 绕组温度 : 0-255°C」
> 〔SERVO p11〕「0x10 | 4 | 芯片过热」「0x200 | 9 | 绕组过热」

---

# D. 未找到项汇总（含检索词）

| 编号 | 待查事实 | 结论 | 已检索词 |
|---|---|---|---|
| A1 | “HPM5E3Y/HPM5E31” 裸型号 | 未找到（只有带封装后缀的订货型号） | `HPM5E3Y`、`HPM5E31` 全文 |
| A1 | 命名规则中每个字母的**明文**定义 | 未找到（图为矢量图，文字残缺） | 逐字符坐标还原 DS p94 |
| A3 | USB 是否专用引脚 | 已确认：DP/DM/VBUS 专用，ID/OC/PWR 复用 | `USB0_` 全表 |
| A4 | ADC 差分正/负引脚名 | 未找到（`NN`/`NP` 是 ACMP 输入，不是 ADC 差分） | `ADC0_IN`、`ADC1_IN`、`差分`、`NN`、`NP` |
| A5 | ESC 独立 RESET / PHY RESET 引脚 | 未找到（内置 PHY 复位走内部 PW12/PV12） | `ESC_RST`、`reset`、`RST` |
| A7 | PC/PD 需要 PIOC/BIOC 配置 | 未找到（PIOC 仅作为电源管理域 IO 控制器出现） | `PIOC`、`BIOC`、`PC 端口`、`PD 端口` |
| A7 | UART 数量 9 还是 8 | 文档冲突（DS p1=9；DS p95/UM=8） | `UART` |
| A8 | “从 0x80000000 启动”的明文 | 未找到（仅 XPI0 映射到 0x80000000） | `0x80000000`、`启动地址` |
| B | REFCK 50 MHz 用法 | 未找到（只有 100MHz/4=25MHz） | `50M`、`REFCK`、`25MHz` |
| B | loopback / 环回测试流程 | 未找到 | `loopback`、`回环`、`环回`、`自环` |
| C1 | 空闲/峰值电流 | 未找到 | `电流`、`mA`、`功率` |
| C1 | 方向切换电路与时序 | 未找到 | `方向`、`切换`、`收发`、`换向`、`DE`、`RE` |
| C3 | 指令码 / 寄存器读写命令 | 未找到（协议无 opcode） | `指令`、`命令`、`寄存器`、`ping`、`功能码` |
| C4 | 寄存器地址表 / EEPROM 保存 | 未找到 | `寄存器`、`地址`、`EEPROM`、`掉电`、`默认` |
| C6 | 推荐轮询间隔 | 未找到 | `周期`、`频率`、`间隔`、`轮询`、`Hz`、`ms` |
| C7 | 温度保护阈值 | 未找到（只有故障位） | `保护`、`阈值`、`过热`、`℃` |

---

# E. 关键数字速查（供固件配置）

| 项目 | 数值 | 出处 |
|---|---|---|
| 内核 / 主频 | RV32-IMAFDCBP / 480 MHz | DS p94、p95 |
| SRAM | 544 KB（ILM 128K + DLM 128K + 256K XRAM + …） | DS p1、UM p269 |
| Flash | 1 MB（合封 NOR，XPI0 映射 0x80000000） | DS p68、UM p269 |
| 封装 / GPIO | BGA196=148，eLQFP100=72，BGA121=72 | DS p95 |
| ESC | 3 端口，8 FMMU，8 SM，60KB PDRAM，64bit DC | UM p1098 |
| 内置 PHY | 仅 HPM5E3Y：2× 100BASE-TX（PHY0/PHY1），MII | DS p62/p95、PHY p4、UM p1192 |
| REFCK | 25 MHz（100 MHz ÷ 4）输出，必须与 ESC 同源 | UM p1099 |
| ADC | 2× ADC16，16b/2MSPS 或 12b/4MSPS，16 pad（PF16–PF31） | DS p1、UM p1197 |
| PWM | 2×8CH，100 ps 分辨率 | DS p1、UM p53 |
| 启动引脚 | PA03=BOOT_MODE1，PA02=BOOT_MODE0（00=XPI NOR） | DS p64 |
| 舵机总线 | 6 Mbps 8N1，单总线半双工，15 节点（0–14，15=广播） | SERVO p5、p7 |
| 舵机帧 | 命令 20B（0xFE 0xEE）/ 反馈 26B（0xFC 0xEE），CRC32 | SERVO p7–p9 |
| 舵机缩放 | 力矩 256000/Nm；速度 2.56/2π per rad/s；位置 32768/2π per rad；kp 1280000；kd 128000000 | SERVO p8 |
| 舵机减速比 | 288.35（协议数据为转子端） | SERVO p7 |

---

*本文件由 `.work/tools/pdf_extract.py` 的提取结果人工核对生成；所有引文可在对应 `.txt` 中用页码检索复核。*
