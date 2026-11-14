# 测试脚本 — ecat_drone_powertrain

本目录是可独立运行的回归/验证脚本（Python 3，仅标准库，无需安装依赖）。
它们从工程源码/头文件中解析常量，因此代码改动后可直接重跑。

| 脚本 | 覆盖内容 | 运行方式（在工程根目录） |
|---|---|---|
| `test_host_proto.py` | USB 主机协议：CRC16 向量、帧编解码/重同步、遥测 54B、参数描述符 70B、LIST_PARAMS 分块、30 条命令与 60 个参数覆盖、AM32 编程命令 0x54/0x55、文档示例帧 | `python tests/test_host_proto.py` |
| `test_servo_frames.py` | S288 舵机协议：CRC32（手册实现 vs 厂商实现）、20B 控制帧/26B 反馈帧逐字节、位域/小端/广播、22 个故障位、坏帧拒收、6 Mbps 时序 | `python tests/test_servo_frames.py` |
| `dshot_sim.py` | 双向 DShot：16bit 帧（反相 CRC）、GCR/差分解码链、4096 种遥测载荷、时钟偏差/边沿抖动鲁棒性、损坏帧拒收、eRPM 换算、TX 脉宽 | `python tests/dshot_sim.py` |

Python 解释器使用 SDK 自带版本即可：

```bat
D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\tools\python3\python.exe tests\test_host_proto.py
```

> 说明：`docs/VERIFICATION.md` 中部分命令路径写作 `.work\...`，那是开发期间的
> 工作副本；本目录是随工程交付的正式副本，内容相同。
