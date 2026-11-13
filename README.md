# Ecat Drone Vector Powertrain

<p align="center">
  <strong>基于 EtherCAT 的矢量共轴动力单元与空中机器人平台</strong><br/>
  <em>EtherCAT-based Coaxial Vector Powertrain for Aerial Robotics</em>
</p>

<p align="center">
  <a href="https://ylh00.github.io/Ecat_Drone_Vector_Powertrain/">
    <strong>🚀 在线项目展示 / Project Website</strong>
  </a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Focus-Vector%20Powertrain-2563eb?style=flat-square"/>
  <img src="https://img.shields.io/badge/Communication-EtherCAT-0f9d8a?style=flat-square"/>
  <img src="https://img.shields.io/badge/Stage-Hardware%20Development-orange?style=flat-square"/>
</p>

## 项目简介 | Overview

本项目围绕 EtherCAT 总线下的矢量共轴动力单元开展设计，并以此为基础探索空中机器人的整机硬件架构。项目采用上下双旋翼共轴动力布局，结合矢量调节机构，构建模块化动力执行单元。

**目前已完成的工作：**
- 矢量共轴动力单元结构设计；
- 空中机器人整机机载硬件架构设计；
- 动力单元配套 PCB 硬件设计与开发;
 - LinuxRT 实时运行环境与 EtherCAT 主站部署；
- Simulink 控制模型向 ARM64 Linux 平台生成与部署；

**后续研发方向：**

- 基于地平线 RDK S600 NPU 的动态目标感知与运动预测；
- 机械臂空中击球、精准抓取等交互任务。


## 1. 整机框架 | Overall Framework

<p align="center">
  <img src="./docs/images/overall_design.png" alt="空中机器人整体框架图" width="92%"/>
</p>

整机采用左右双侧动力模块对称布局。左右模块通过直线圆柱机臂与中央机身直接连接，机身下方布置一个小型机械臂。中央机身用于承载机载计算、传感器及通信相关硬件。

## 2. 矢量共轴动力单元 | Vector Coaxial Powertrain

<p align="center">
  <img src="./docs/images/vector_powertrain_module.png" alt="矢量共轴动力单元结构图" width="82%"/>
</p>

动力单元采用上下双旋翼共轴布局，集成旋翼、电机、矢量调节机构及配套控制硬件，是整机动力执行系统的核心模块。

设计重点：
- 共轴双旋翼动力布局；
- 矢量调节机构与动力执行结构集成；
- 动力单元模块化设计；
- 面向 EtherCAT 通信与多执行单元协同控制的接口预留。

## 3. PCB 硬件 | PCB Hardware

<p align="center">
  <img src="./docs/images/pcb_board_01.jpg" alt="PCB 板卡 01" width="92%"/>
  <br/><em>PCB Board 01</em>
</p>

<p align="center">
  <img src="./docs/images/pcb_board_02.jpg" alt="PCB 板卡 02" width="92%"/>
  <br/><em>PCB Board 02</em>
</p>

围绕动力单元开展配套 PCB 硬件开发，展示板卡器件布局、接口配置及板级集成设计。具体电路功能与接口定义以仓库中的原理图、PCB 工程文件及硬件资料为准。

## 4. 系统架构 | System Architecture

```mermaid
flowchart TB
    A["机载主控平台"] --> B["EtherCAT 主站"]
    B --> C["EtherCAT 通信链路"]
    C --> D["左侧矢量共轴动力单元"]
    C --> E["右侧矢量共轴动力单元"]
    A --> F["机载传感器 / 任务系统"]
    A --> G["机身下方机械臂"]
```

系统规划采用集中计算、分布式执行的架构：机载主控负责整机控制与任务管理，EtherCAT 链路面向动力单元的周期性通信与状态交互。

## 5. 项目进度 | Development Status

### 已完成 | Completed
- [x] 矢量共轴动力单元设计
- [x] 整机机载硬件架构设计
- [x] 动力单元 PCB 硬件开发

### 计划开发 | Planned

- [ ] RDK S600 NPU 端侧 AI 环境搭建
- [ ] 动态目标检测、跟踪与运动预测
- [ ] 机械臂空中击球与精准抓取
- [ ] AI 感知与实时运动控制协同


## Vision

从矢量共轴动力单元出发，逐步构建融合实时控制、端侧 AI 感知与空中交互能力的智能空中机器人平台。

<p align="center"><strong>From Vector Propulsion to Intelligent Aerial Robotics.</strong></p>
