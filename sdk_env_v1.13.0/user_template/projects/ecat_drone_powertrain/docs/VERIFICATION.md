# 楠岃瘉鎶ュ憡 鈥?ecat_drone_powertrain

> 琚獙璇佸璞★細`sdk_env_v1.13.0\user_template\projects\ecat_drone_powertrain`
> 锛圚PM5E3Y / board `ecat_drone_5e3y` / hpm_sdk v1.13.0锛?> 楠岃瘉鑰咃細verifier锛堢嫭绔嬮獙璇侊紝涓嶉噸鍐欐ā鍧楋級銆€楠岃瘉鏃ユ湡锛?026-11-14
> 宸ュ叿閾撅細RISC-V GCC 13.2.0 (`rv32imac_zicsr_zifencei_multilib_b_ext-win`)銆丆Make+Ninja銆丳ython 3锛圫DK 鑷甫锛夈€?NET Framework 4.0 `csc.exe`
> 缁撹锛?*鏋勫缓/閾炬帴閫氳繃锛? warning锛涚紪璇戞湡濂戠害鍏ㄩ儴閫氳繃锛汸DO/CoE/涓婁綅鏈哄崗璁?鍙傛暟绯荤粺涓€鑷存€ч€氳繃锛涙湭鍙戠幇 critical 绾х己闄枫€侲therCAT 鐪熷疄鍗忚鏍堛€丏Shot/鑸垫満/ADC 鐨勭‖浠舵椂搴忔湭楠岃瘉锛堟棤瀹炵墿鏉匡級銆?*
> 鏈姤鍛婁腑鐨勬瀯寤虹洰褰?`D:\Ecat_Drone_Vector_Powertrain\.work\build_verify` 鏄獙璇佷笓鐢ㄧ洰褰曪紙闈炰氦浠樼墿锛夈€?
---

## 0. 缁撹鎽樿

| 椤圭洰 | 缁撴灉 | 璇佹嵁 |
|---|---|---|
| flash_xip 鍏ㄩ噺鏋勫缓锛堜粠闆讹級 | **閫氳繃** | 搂1锛宍output\ecat_drone_powertrain.elf` (839,620 B) |
| 缂栬瘧璀﹀憡鏁?| **0** | 搂1.3锛屽叏鏂囨绱?`warning:` 璁℃暟 = 0 |
| `scripts/check_syntax.py` 鍏ㄩ儴 src/port 鏂囦欢 | **16/16 OK** | 搂1.4 |
| 缁撴瀯浣?鍗忚濂戠害锛?2/48/54/70 B 绛夛級 | **鍏ㄩ儴閫氳繃** | 搂2 `_Static_assert` + `nm` |
| EtherCAT PDO锛圗SI/CSV 鈫?`ecat_pdo.h`锛?| **涓€鑷达紙22/48 B锛屽亸绉婚€愰」鍚诲悎锛?* | 搂4 |
| CoE 鍙傛暟瀵硅薄 0x2000..0x203B锛?0 椤癸級绫诲瀷/瀹藉害 | **60/60 涓€鑷?* | 搂4.4锛堟鍓?11 椤圭被鍨嬮敊璇凡鐢?ecat-agent 淇骞跺楠岋級 |
| 涓婁綅鏈哄崗璁嚜妫€ | **218/218 OK**锛坧roto_test锛夛紝**43/43 PASS**锛坔ost --selftest锛?| 搂5 |
| 鑸垫満甯у崟鍏冩祴璇?| **58 passed / 0 failed** | 搂5.4 |
| PARAM_COUNT / 鍙傛暟琛ㄥ畬鏁存€?| **60 / 0..59 鍏ㄨ鐩?* | 搂6.1 |
| 鍥轰欢闀滃儚 vs 504 KB 搴旂敤鍖轰笂闄?| **154,876 B << 516,096 B锛?0.0%锛?* | 搂6.3 |
| 鍙傛暟鍖?/ EEPROM 妯℃嫙鍖哄啿绐?| **鏃犲啿绐?* | 搂6.4 |

---

## 1. 鏋勫缓缁撴灉

### 1.1 鐜涓庡懡浠わ紙鍙鐜帮級

```powershell
$env:HPM_SDK_BASE='D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\hpm_sdk'
$env:GNURISCV_TOOLCHAIN_PATH='D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\toolchains\rv32imac_zicsr_zifencei_multilib_b_ext-win'
$env:PATH='D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\tools\cmake\bin;D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\tools\ninja;D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\tools\python3;' + $env:GNURISCV_TOOLCHAIN_PATH + '\bin;' + $env:PATH

# 浠庨浂鏋勫缓锛堥獙璇佺洰褰曪級
powershell -NoProfile -ExecutionPolicy Bypass -File `
  D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\scripts\build.ps1 `
  -Project D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\projects\ecat_drone_powertrain `
  -Board ecat_drone_5e3y -BuildType flash_xip -Config debug `
  -BuildDir D:\Ecat_Drone_Vector_Powertrain\.work\build_verify
```

绛変环鐨勬墜鍔?CMake 鍙傛暟锛坆uild.ps1 鍐呴儴鍗宠繖浜涘弬鏁帮級锛?
```
-G Ninja -S <project> -B <builddir> -DBOARD=ecat_drone_5e3y
-DBOARD_SEARCH_PATH=D:\...\user_template\boards
-DHPM_BUILD_TYPE=flash_xip -DCMAKE_BUILD_TYPE=debug
```

鏋勫缓鏃ュ織锛歚.work\build_verify\build_flash_xip.log`锛?3 KB锛夈€?瀹屾暣鏃ュ織涓?`== BUILD OK:` 涓?`[82/82] Linking C executable output\ecat_drone_powertrain.elf` 鍧囧嚭鐜帮紝閫€鍑虹爜 0銆?
琛ュ厖锛歀ead 鍚屾椂鐢?`scripts\build.cmd clean`锛堜慨澶?`%~dp0` 鍙傛暟绉讳綅 bug 鍚庯級鍋氳繃涓€娆＄嫭绔嬩粠闆舵瀯寤猴紝寰楀埌鐩稿悓鐨?`FLASH: 154876 B`锛屼氦鍙夊嵃璇佹湰缁撴灉銆?
### 1.2 浜х墿涓庝綋绉?
| 鏂囦欢 | 澶у皬 | 璇存槑 |
|---|---|---|
| `output\ecat_drone_powertrain.elf` | 839,620 B | 鍚皟璇曚俊鎭?|
| `output\ecat_drone_powertrain.bin` | 161,800 B | 鐑у綍闀滃儚锛堝惈 boot header / nor cfg锛?|
| `.text` | 148,052 B | `riscv32-unknown-elf-size` |
| `.data` | 2,640 B | |
| `.bss`锛堝惈 heap+stack锛?| 85,136 B | |

### 1.3 鍐呭瓨鍗犵敤锛堥摼鎺ュ櫒 `--print-memory-usage` 鍘熷杈撳嚭锛?
```
Memory region         Used Size  Region Size  %age Used
           FLASH:      154876 B         1 MB     14.77%
             ILM:       10000 B       128 KB      7.63%
             DLM:         32 KB       128 KB     25.00%
        AXI_SRAM:       42560 B       192 KB     21.65%
AXI_SRAM_NONCACHEABLE:  12448 B        64 KB     18.99%
        AHB_SRAM:          0 GB        32 KB      0.00%
```

璇存槑锛欴LM 鐨?32 KB = 閾炬帴鑴氭湰榛樿 `_heap_size=0x4000`锛?6 KB锛? `_stack_size=0x4000`锛?6 KB锛夛紱搴旂敤浠ｇ爜涓嶄娇鐢ㄥ姩鎬佸垎閰嶏紙瑙?搂8 姝ｉ潰缁撹锛夛紝heap 鍙悗缁寜闇€璋冨皬銆俙AXI_SRAM_NONCACHEABLE` 涓?DShot TX burst銆佽埖鏈?TX/RX 甯с€乁SB 绔偣绛?DMA 缂撳啿銆?
### 1.4 璀﹀憡娓呭崟锛?*0 鏉?*锛?
- 鍏ㄩ噺鏋勫缓鏃ュ織妫€绱?`warning:` 鈫?**0 鏉?*锛坄(Select-String build_flash_xip.log -Pattern 'warning:').Count = 0`锛夈€?- 鏃╂湡鏋勫缓鍑虹幇杩囥€佺幇宸蹭慨澶?娑堝け鐨勮鍛婏細
  - `SSC/StubSrc/ssc_stub.c:188 'stub_enter_state' defined but not used` 鈫?宸茬敱 ecat-agent 淇锛? 鏉★級锛?  - `src/host_proto_app.c:224 'host_wr_i16' defined but not used` 鈫?宸茬敱 comms-agent 淇锛? 鏉★級锛?  - Lead 鎻愬埌鐨?dshot `s_tx_dma_buf`锛氭渶缁堜唬鐮佷负 `ATTR_PLACE_AT_NONCACHEABLE_BSS_WITH_ALIGNMENT(32)` 涓旇 DMA 寮曠敤锛坄src/dshot_bidir.c:253`锛夛紝涓嶄骇鐢熻鍛娿€?- `python scripts\check_syntax.py --build-dir .work\build_verify --project <proj> <proj>\src\*.c <proj>\port\*.c` 鈫?`All 16 file(s) OK`锛坄-fsyntax-only` 涓嶄骇鐢熻瘖鏂紱缂栬瘧鏈熷憡璀︿互涓婇潰鐨勭湡瀹炴瀯寤轰负鍑嗭級銆?
---

## 2. 缂栬瘧鏈熷绾︽鏌?
鏂规硶锛氱敤**鐩爣宸ュ叿閾俱€佷笌 `src/app_task.c` 瀹屽叏鐩稿悓鐨勭紪璇戦€夐」**缂栬瘧 `.work\build_verify\contract_check.c`锛堝唴鍚?`_Static_assert`锛夛紝鍐嶇敤 `nm -S` 璇诲彇鏁扮粍绗﹀彿澶у皬寰楀埌鏁板€硷紱閬垮厤鈥滀富鏈虹 C 绋嬪簭 ABI 涓庣洰鏍囦笉涓€鑷粹€濈殑闂銆?
```powershell
# 浠?compile_commands.json 鍙?app_task.c 鐨勭紪璇戝懡浠わ紝鏇挎崲婧愭枃浠?杈撳嚭涓?contract_check.c
cmd /c <app_task compile command with " -o ... -c ..." replaced>
# 杈撳嚭: COMPILE_EXIT=0
riscv32-unknown-elf-nm.exe -S --defined-only contract_check.obj | findstr rpt_
```

### 2.1 缁撴瀯浣撳ぇ灏忥紙鍏ㄩ儴 `_Static_assert` 閫氳繃锛宍nm` 瀹炴祴鍊硷級

| 濂戠害 | 鏈熸湜 | 瀹炴祴锛坄nm` 绗﹀彿澶у皬锛?| 缁撴灉 |
|---|---|---|---|
| `sizeof(ecat_rxpdo_t)` | 22 | `0x16` = 22 | 鉁?|
| `sizeof(ecat_txpdo_t)` | 48 | `0x30` = 48 | 鉁?|
| `sizeof(host_telemetry_t)` | 54 | `0x36` = 54 | 鉁?|
| `HOST_PARAM_DESC_SIZE` | 70 | 70 | 鉁?|
| `sizeof(host_param_desc_t)` | 70 | `0x46` = 70 | 鉁?|
| `sizeof(host_dshot_tlm_t)` | 26 | `0x1a` = 26锛堝浐浠跺唴閮?`dshot_telemetry_t` 涓?28锛岀粡閫愬瓧娈靛簭鍒楀寲锛岄潪 memcpy锛?| 鉁?|
| `sizeof(host_servo_status_t)` | 20 | `0x14` = 20 | 鉁?|
| `sizeof(host_adc_blob_t)` | 24 | `0x18` = 24 | 鉁?|
| `sizeof(host_am32_status_t)` | 2 | 2 | 鉁?|
| `PARAM_COUNT` | 60锛圠ead/鍙傛暟绯荤粺瑕佹眰锛?| `0x3c` = 60 | 鉁?|

### 2.2 瀛楁鍋忕Щ锛坄offsetof` 鍏ㄩ儴閫氳繃锛?
- RxPDO锛歚control_word@0, esc_cmd_us@2, servo_angle_d10@6, servo_ctrl_word@10, dshot_cmd@12, reserved0@16, cycle_counter@18`锛坄dshot_cmd[2]` 鍗?12..15锛屾晠 reserved0 鍦?**16**銆乧ycle_counter 鍦?**18**锛涗笌 Lead 淇鍚庣殑 `inc/ecat_pdo.h:43-44`銆乣README_zh.md:157-158` 涓€鑷达級銆?- TxPDO锛歚status_word@0, fault_code@2, esc_erpm@4, esc_tlm_period_us@12, servo_pos_d10@16, servo_speed_rpm@20, servo_current_ma@24, servo_temp_c@28, servo_error@30, sys_voltage_dv@32, sys_current_ca@34, esc_current_ma@36, loop_counter@40, led_status@42, ecat_link_status@44, reserved0@46`銆?- `host_telemetry_t`锛?/2/4/12/16/20/24/28/30/32/34/36/40/42/44/46/50锛坄host_rx_frames@46`锛宍host_crc_errors@50`锛夈€?- `host_param_desc_t`锛氫笌 `HOST_PARAM_DESC_OFF_*` 閫愰」涓€鑷达紙id@0, type@2, persist@3, readonly@4, len@5, min@6, max@10, def@14, value@18, name@22, desc@38锛宯ame 16 B / desc 32 B锛夈€?- 鍗忚甯搁噺锛歚HOST_PROTO_OVERHEAD == 8`锛沗HOST_PROTO_LIST_PARAMS_MAX(7) 脳 (1+70) = 491 鈮?APP_CFG_HOST_PROTO_MAX_PAYLOAD(512)`銆?
缁撹锛?*濂戠害妫€鏌ュ叏閮ㄩ€氳繃锛圕OMPILE_EXIT=0锛?*銆?
---

## 3. 妯″潡闆嗘垚瀹¤

### 3.1 涓诲惊鐜『搴忥紙`src/main.c:68-98`锛変笌鏋舵瀯鏂囨。涓€鑷?
```
ecat_app_task() 鈫?dshot_task() 鈫?servo_task() 鈫?adc_mon_task() 鈫?led_task()
鈫?usb_cdc_app_task() 鈫?app_task_run()
```
`app_task_run()` 鍐呴儴鎸?`g_app_tick_ms` 鍒嗛鍒?1 kHz锛坄src/app_task.c:349-355`锛夛紱鍒濆鍖栭『搴?`app_task_init()`锛坧aram鈫抋dc鈫抣ed鈫抯ervo鈫抎shot鈫抪aram_apply鈫抲sb锛夆啋 `ecat_app_init()`锛坄src/main.c:59-62`锛夌鍚?`docs/ARCHITECTURE.md`銆?
### 3.2 杩涚▼鏄犲儚鍏ㄥ眬鍙橀噺鐨勫啓鍏ヨ€咃紙濂戠害妫€鏌ラ」锛?
| 鍙橀噺 | 瀹氫箟 | 鍐欏叆鑰?| 璇诲彇鑰?|
|---|---|---|---|
| `g_ecat_rxpdo` | `src/ecat_app.c:63` | 浠?`ecat_app.c`锛坄memset` :150銆乣APPL_OutputMapping` memcpy :518銆乣APPL_StopOutputHandler` 娓?control_word :477锛?| `app_task.c:253`锛堥€愬瓧鑺傚揩鐓э級 |
| `g_ecat_txpdo` | `src/ecat_app.c:64` | 浠?`app_task.c:137-203` | `ecat_app.c:505`锛坄APPL_InputMapping`锛夈€乣host_proto_app.c:531`锛堝揩鐓э級 |
| `g_ecat_op_active` | `src/ecat_app.c:65` | `ecat_app.c:127/152/221/528`锛圓L 鐘舵€侀┍鍔級锛汼SC 妗╁湪 `ECAT_SSC_STUB_TEST_MODE` 涓嬩篃浼氬啓锛坄SSC/StubSrc/ssc_stub.c:92/141`锛岄粯璁ゅ叧闂級 | `app_task.c:73/98/281/309`銆乣host_proto_app.c:368/734/823/931/1120` |

缁撹锛氭棤妯″潡鍐欏叾瀹冩ā鍧楃鏈夊叏灞€鍙橀噺锛汸DO 鍙屽悜鎷疯礉璺緞绗﹀悎璁捐銆?
### 3.3 澶存枃浠跺０鏄?鈫?瀹炵幇 鈫?璋冪敤鏂癸紙鎶芥煡瑕嗙洊锛?
| 澶存枃浠?API | 瀹炵幇 | 璋冪敤鏂癸紙璇佹嵁锛?|
|---|---|---|
| `host_proto_app_bench_active` | `host_proto_app.c:361` | `app_task.c:267` |
| `dshot_enable/set_throttle/send_command/get_telemetry/task/init` | `dshot_bidir.c` | `app_task.c:309/314/316/146`銆乣host_proto_app.c`銆乣main.c:75` |
| `servo_set_target_d10/set_enable/clear_fault/get_status/task/init/scan_bus` | `servo_s288.c` | `app_task.c:290-337`銆乣host_proto_app.c`銆乣main.c:78` |
| `led_set_code/get_status_word/task/init/set_brightness/set_rgb/test_pattern` | `led_rgb.c` | `app_task.c:104-126/171`銆乣host_proto_app.c:1072/1084`銆乣param.c:662` |
| `adc_mon_get/task/init/apply_params` | `adc_mon.c` | `app_task.c:159/210`銆乣param.c:665`銆乣main.c:81` |
| `param_get/set/save/load/reset_defaults/apply/get_desc/count` | `param.c` | 鍏ㄥ伐绋嬶紙`ecat_app.c`銆乣host_proto.c`銆佸悇妯″潡锛?|
| `ecat_app_get_al_state/get_link_status` | `ecat_app.c` | `app_task.c:74/142/165`銆乣host_proto_app.c:575` |
| `usb_cdc_app_init/task/read/write/tx_free/get_stats` | `usb_cdc_app.c` | `main.c:87`銆乣host_proto_app.c` |
| `am32_4way_begin/xfer/end` | `am32_4way.c` | `host_proto_app.c:1127/1167/1200` |
| `dshot_line_park/release/set_gpio/read_gpio/timebase_raw/freq` | `dshot_bidir.c` | `am32_4way.c` |

閾炬帴鎴愬姛锛埪?锛夋湰韬瘉鏄庘€滆璋冪敤涓斿凡瀹炵幇鈥濈殑绗﹀彿瀹屽锛涙湭瀹炵幇鎴栨湭琚皟鐢ㄧ殑澹版槑瑙?搂3.5銆?
### 3.4 璧勬簮褰掑睘锛堝鐓?`docs/PINMAP.md` 绗?7 鑺傦紝浠ｇ爜璇佹嵁锛?
| 璧勬簮 | 浠ｇ爜浣跨敤 | 缁撹 |
|---|---|---|
| GPTMR0 ch0/ch1 | board 鍖咃紙delay / `board_timer_create`锛?| 鉁?鏃犲啿绐?|
| GPTMR1 ch2/ch3 | `dshot_bidir.c`锛坄BOARD_DSHOT_CAP_*`锛孏PTMR1 鎹曡幏锛?| 鉁?|
| GPTMR2 ch0 | `ecat_config.h:65-68`锛坄ECAT_TIMER_GPTMR`锛孍therCAT 1 ms tick锛?| 鉁咃紙PINMAP 宸茶ˉ锛?|
| PWM0 璁℃暟鍣? + CMP4鈥? | `dshot_bidir.c:131-135`锛堣緭鍑?2/3锛?| 鉁?|
| PWM0 CMP16 鈫?pwm_dma_0 | `dshot_bidir.c:136-138,607-611,671`锛圱X DMA 鍛ㄦ湡瑙﹀彂锛?| 鉁咃紙PINMAP 宸茶ˉ锛?|
| PWM1 璁℃暟鍣?/3 + CMP8鈥?3 | `led_rgb.c`锛坄BOARD_RGB_LED_*`锛?| 鉁?|
| XDMA ch0 | `dshot_bidir.c:149-154`锛坄HPM_XDMA`锛孴RGM DMACFG_0 鈫?MOT_0锛?| 鉁?|
| HDMA ch4鈥? | `servo_s288.c:150-153`锛坄BOARD_APP_DMA0` = `HPM_HDMA`锛?| 鉁?|
| ADC0 IN1/IN8銆丄DC1 IN3/IN13 | `adc_mon.c`锛坄BOARD_ADC*_*_CH`锛?| 鉁?|
| Flash EEPROM 0x80000 + 64 KB / 鍙傛暟 0x7E000 + 4 KB | `ecat_config.h:39-45`銆乣param.h:41-42` | 鉁?涓嶉噸鍙狅紙搂6.4锛?|

涓柇浼樺厛绾э紙浠ｇ爜璇佹嵁锛夛細DShot PWM/GPTMR1 = **1**锛坄dshot_bidir.c:764-765`锛夛紱鑸垫満 UART = 2銆丠DMA = 3锛坄servo_s288.c:155-156,1120-1122`锛夛紱USB = 2锛坄usb_cdc_app.c:329`锛夛紱ECAT RESET = 2銆丼YNC0/1 = 3銆丳DI = 4銆丒CAT 瀹氭椂鍣?GPTMR2 = 3锛坄port/hpm_ecat_hw.c:300-306`锛夈€?瀹¤缁撹锛氭棤鍚屼竴涓柇鍚戦噺琚袱涓ā鍧楁敞鍐岋紱鏈€楂樹紭鍏堢骇鐣欑粰 DShot 鏃跺簭锛汼YNC0/1 涓庤埖鏈?DMA 鍚屼负 3銆佷綆浜庤埖鏈?UART/USB(2)锛屽瓨鍦?碌s 绾?SYNC 鎶栧姩锛? ms 鍛ㄦ湡涓嬪彲鎺ュ彈锛堣 搂8-L5锛夈€?
### 3.5 鏈帴绾?鍐椾綑 API锛堜綆椋庨櫓锛屽缓璁氦浠樺墠娓呯悊锛?
- `ecat_app_outputs_enabled()`锛坄ecat_app.c:287`锛夋棤璋冪敤鑰咃紙app_task 鐩存帴鐢?`g_ecat_op_active`锛夈€?- `ecat_led_set_run()` / `ecat_led_set_err()`锛坄ecat_app.c:389/395`锛夐櫎鑷韩澶栨棤璋冪敤鑰咃紱ESC 渚?RUN/ERR 鐢?`ecat_write_led_overrides()` 鍐?ESC 瀵勫瓨鍣紝SSC 妗╀笉鍥炶皟杩欎袱涓嚱鏁帮紙鎺ョ湡瀹炲崗璁爤鏃堕渶纭鏄惁闇€瑕佹寕鎺ワ級銆?- `ecat_ssc_stop()`锛坄ecat.c:83`锛夋棤璋冪敤鑰呫€?- `ecat_app_get_sync0_period_us()`/`get_sync1`/`get_dc_shift_ns`/`get_dc_mode`/`get_watchdog_ms`锛坄ecat_app.c:363-386`锛夋湭琚换浣曚唬鐮佽皟鐢紙DC 榛樿鍊煎湪 `ecat_app_apply_params()` 鍐呯洿鎺ュ啓 ESC 瀵勫瓨鍣級銆?- `s_last_input_tick_ms`锛坄ecat_app.c:74`锛夊彧鍐欎笉璇伙紙`APPL_InputMapping` 涓洿鏂帮級锛屽睘璇婃柇鍐椾綑銆?
---

## 4. EtherCAT PDO / CoE 涓€鑷存€?
### 4.1 杩涚▼鏄犲儚鎷疯礉瀹炵幇

- `APPL_GenerateMapping()`锛坄src/ecat_app.c:485-494`锛夊浐瀹氳繑鍥?`*pInputSize=APP_ECAT_TXPDO_SIZE(48)`銆乣*pOutputSize=APP_ECAT_RXPDO_SIZE(22)`锛沗ecat.c:59-68` 鍦?`MainInit()` 鍚庤皟鐢ㄥ苟鏍￠獙锛屽昂瀵镐笉绗︿細鎵撳嵃閿欒銆?- `APPL_InputMapping()`锛?497-508锛夛細`memcpy(pData, &g_ecat_txpdo, min(nPdInputSize,48))`锛屽皬绔?packed 缁撴瀯閫愬瓧鑺傛嫹璐濆畨鍏ㄣ€?- `APPL_OutputMapping()`锛?511-521锛夛細`memcpy(&g_ecat_rxpdo, pData, min(nPdOutputSize,22))`銆?- PDO 灏哄鏉ヨ嚜 SSC 閰嶇疆/ESI锛屼笌鏈伐绋嬬殑缂栬瘧鏈熺粨鏋勪竴鑷达紙搂2锛夈€?
### 4.2 ESI `SSC/ESI/HPM_ECAT_DRONE.xml` PDO 鏄犲皠锛?x1600 / #x1A00锛?
RxPDO锛坄Sm="2"`锛宍#x1600`锛孍SI L2669-2742锛夛細**10 涓?Entry锛?+2+2+2+2+2+2+2+2+4 = 22 B**

| # | ESI 瀵硅薄 | 绫诲瀷/浣嶉暱 | 瀛楄妭鍋忕Щ | `ecat_pdo.h` 瀛楁 |
|---|---|---|---|---|
| 1 | 0x7000 | UINT/16 | 0 | control_word |
| 2 | 0x7001 | UINT/16 | 2 | esc_cmd_us[0] |
| 3 | 0x7002 | UINT/16 | 4 | esc_cmd_us[1] |
| 4 | 0x7003 | INT/16 | 6 | servo_angle_d10[0] |
| 5 | 0x7004 | INT/16 | 8 | servo_angle_d10[1] |
| 6 | 0x7005 | UINT/16 | 10 | servo_ctrl_word |
| 7 | 0x7006 | UINT/16 | 12 | dshot_cmd[0] |
| 8 | 0x7007 | UINT/16 | 14 | dshot_cmd[1] |
| 9 | 0x7008 | UINT/16 | 16 | reserved0 |
| 10 | 0x7009 | UDINT/32 | 18 | cycle_counter |

TxPDO锛坄Sm="3"`锛宍#x1A00`锛孍SI L2743-2914锛夛細**24 涓?Entry锛屽悎璁?48 B**锛岄『搴?闀垮害涓?`ecat_txpdo_t` 閫愬瓧娈靛惢鍚堬細
0x6000 status_word(16@0) 鈫?0x6001 fault_code(16@2) 鈫?0x6002/0x6003 esc_erpm(32@4/8) 鈫?0x6004/0x6005 esc_tlm_period_us(16@12/14) 鈫?0x6006..0x600B servo_pos/speed/current(16@16/18/20/22/24/26) 鈫?0x600C..0x600F servo_temp/error(8@28/29/30/31) 鈫?0x6010 sys_voltage_dv(16@32) 鈫?0x6011 sys_current_ca(16@34) 鈫?0x6012/0x6013 esc_current_ma(16@36/38) 鈫?0x6014 loop_counter(@40) 鈫?0x6015 led_status(@42) 鈫?0x6016 ecat_link_status(@44) 鈫?0x6017 reserved(@46)銆?鎬婚暱鏍￠獙锛?+2+4+4+2+2+16+2+2+2+2+2+2+2+2+2+2+2 鈥?= **48 B** 鉁?`SSC/ecat_drone_application.csv` 鐨勫璞￠『搴?绫诲瀷涓?ESI 鐩稿悓锛堝悓涓€鐢熸垚鍣ㄤ骇鐗╋級锛?*宸茬‘璁や笌 `inc/ecat_pdo.h` 瀹為檯鍐呭瓨甯冨眬瀹屽叏涓€鑷?*锛圧x 鐨?reserved0/cycle_counter 鍋忕Щ涓?16/18锛屼笌 Lead 淇鍚庣殑澶存枃浠舵敞閲娿€丷EADME 琛ㄦ牸涓€鑷达級銆?
### 4.3 ESI 璁惧韬唤 / 绔彛 / SM / DC

| 椤圭洰 | ESI 鍊?| 涓庝唬鐮佷竴鑷存€?|
|---|---|---|
| Vendor Id | `#x00000E5A`锛圗SI L4锛?| 鉁?`app_config.h:29` |
| Product Code | `#x0000E3A1`锛圠18锛?| 鉁?`app_config.h:30` |
| Revision No | `#x00010001`锛圠18锛?| 鉁?`app_config.h:31` |
| Device Physics | `"YY"`锛圠17锛? Port0+Port1 涓や釜绔彛 | 鉁?涓ょ墖鍐?MII PHY |
| MBoxOut/MBoxIn | `0x80` B @ `#x1000`/`#x1080`锛孋ontrolByte 0x26/0x22锛圠2665-2666锛?| 鉁?涓?SSC 閰嶇疆 mailbox 涓€鑷?|
| Outputs SM2 | `DefaultSize=22` @ `#x1100`锛圠2667锛?| 鉁?RxPDO 22 B |
| Inputs SM3 | `DefaultSize=48` @ `#x1400`锛圠2668锛?| 鉁?TxPDO 48 B |
| FMMU | Outputs / Inputs / MBoxState锛圠2662-2664锛?| 鉁?|
| DC | `<Dc>` + `<OpMode>` + `AssignActivate #x300` + `CycleTimeSync0/1`锛圠2918-2929锛?| 鉁?`DC_SUPPORTED=1` |

### 4.4 CoE 瀵硅薄 0x2000..0x203B锛?0 椤癸級绫诲瀷澶嶆牳 鈥斺€?鍘?11 椤圭己闄峰凡淇

- 鏄犲皠瑙勫垯锛氬璞＄储寮?`0x2000 + PARAM_ID` 鈫?`param_id_t`锛坄ecat_app.c:58,548-560`锛夛紱`ecat_coe_param_id()` 鐢?`param_count()` 杈圭晫妫€鏌ワ紝**涓嶄細瓒婄晫**銆?- 璇诲啓瀹藉害鏍￠獙锛歚ecat_coe_param_width()` 鐢辨弿杩扮绫诲瀷寰楀埌 1/2/4/16 B锛孲DO 鐨?`size` 蹇呴』鐩哥瓑锛屽惁鍒欒繑鍥?`ABORTIDX_DATA_TYPE_MISMATCH`锛坄ecat_app.c:601/647`锛夛紱`readonly` 鎷掔粷鍐欙紙:650-652锛夛紱鑼冨洿閿欒鏄犲皠 `ABORTIDX_VALUE_EXCEEDED`锛?679锛夛紱I16 鍐欏叆鍏堢鍙锋墿灞曪紙:670-672锛夈€?- 澶嶉獙鏂规硶锛堣剼鏈細`.work\build_verify\check_coe_types.py`锛夛細浠?`src/param.c` 鐨?`s_param_desc[]` 鎻愬彇 60 涓弿杩扮绫诲瀷锛堟潈濞侊級鈫?涓?`SSC/ecat_drone.xlsx`锛坰heet1 鍒?E锛夈€乣SSC/ecat_drone_application.csv`锛圖ataType 鍒楋級銆乣SSC/ESI/HPM_ECAT_DRONE.xml`锛圖ictionary 涓?`<Type>`锛夐€愬璞℃瘮瀵广€傝緭鍑猴細

```
enum entries: 60 table entries: 60 csv: 60 xlsx: 60 esi: 60
mismatches: 0 extra ids: none
RESULT: all 60 CoE objects match src/param.c descriptors (csv+xlsx+esi)
```

- 鍘嗗彶闂锛堝凡淇锛岃褰曞鏌ワ級锛氭鍓?xlsx/CSV/ESI 涓湁 11 涓璞＄被鍨嬮敊璇紙0x200D/0x200F/0x2016/0x2018/0x201A/0x201C/0x201E/0x2020/0x2023 閿欒澹版槑涓?UDINT(4B)銆?x2039/0x203B 鏈夌鍙锋€ч敊璇級锛屼細瀵艰嚧鐪熷疄 SSC 鍗忚鏍堜笅杩欎簺鍙傛暟鐨?SDO 璇诲啓琚?`ecat_app.c` 鐨勫搴︽牎楠屾嫆缁濄€傛牴鍥犳槸 `SSC/tools/gen_ssc.py` 鍘熷厛瀵?`inc/param.h` 涓?*鏃犵被鍨嬫敞閲?*鐨勭浜屼釜閫氶亾鍙傛暟榛樿 `u32`锛沞cat-agent 宸蹭慨澶嶏細鐢熸垚鍣ㄧ幇鍦ㄤ粠 `src/param.c` 鎻忚堪绗﹁鍙栨潈濞佺被鍨嬶紙`gen_ssc.py:217` `ptype = info["type"]`锛夊苟涓?`param.h` 娉ㄩ噴浜ゅ弶鏍￠獙锛?219-228锛夛紝閲嶆柊鐢熸垚鍚?60/60 涓€鑷淬€?- 閰嶇疆鏂囦欢 `SSC/Config/HPM_ECAT_DRONE_Config.xml`锛歏endorID/ProductCode/RevisionNo/DEVICE_NAME 涓?`app_config.h` 涓€鑷达紙L2/95-102锛夛紱`COE_SUPPORTED=1`銆乣ESC_EEPROM_EMULATION=1`銆乣ESC_EEPROM_SIZE=0x800`銆乣ECAT_TIMER_INT=1`銆乣MAX_PD_INPUT_SIZE/OUTPUT=0x40`锛涘簲鐢ㄥ洖璋冨悕 `ecat_coe_obj_read/write` 涓?`ecat_app.c` 瀹炵幇涓€鑷淬€俙SSC/README.md` 涓?`docs/ECAT_SSC.md` 鐜板凡瀛樺湪骞剁粰鍑?SSC Tool 鐢熸垚姝ラ锛堟湰杞湭瀹為檯鎵ц SSC Tool锛岃 搂9锛夈€?
### 4.5 AL 鐘舵€?/ 鐪嬮棬鐙?
- `g_ecat_op_active` 鐢?AL 鐘舵€侀┍鍔細`ecat_set_al_state()`锛坄ecat_app.c:119-137`锛孉L==OP 缃?1锛夈€乣ecat_app_task()`(:221)銆乣APPL_Application()`(:528) 缁熶竴鎸?`s_al_state == OP` 璁＄畻锛沗APPL_StartOutputHandler/StopOutputHandler`(:462-479) 鍦ㄨ繘鍏?OP 鏃剁疆浣嶃€侀€€鍑?OP 鏃舵竻闆跺苟娓?RxPDO control_word銆?- `ecat_app_watchdog_expired()`锛?293-310锛変紭鍏堣 ESC 纭欢 `WDG_STAT_PDAT`锛屽惁鍒欎互 `s_last_output_tick_ms`锛坄APPL_OutputMapping` 鏇存柊锛変负鍩哄噯銆?- **app_task 鐪嬮棬鐙楋紙Lead 宸蹭慨澶嶏紝澶嶉獙閫氳繃锛?*锛歚src/app_task.c:276-293` 鐜板湪
  - `g_ecat_op_active == 0` 鏃讹細鍒锋柊 `s_last_ecat_cycle`銆侀噸瑁呰秴鏃躲€佸苟 `app_state_clear_fault(APP_FAULT_ECAT_OP_TIMEOUT)` 鑷姩鎭㈠锛堟敞閲?:278-280锛夛紱
  - 浠呭湪 OP 鍐呬笖 `cycle_counter` 鍋滄鍙樺寲瓒呰繃 `PARAM_ECAT_WATCHDOG_MS` 鏃剁疆 `APP_FAULT_ECAT_OP_TIMEOUT`銆?  杩欐秷闄や簡鈥滆繘鍏?OP 鍓?绂诲紑 OP 鍚庤閿佸瓨 ECAT_OP_TIMEOUT銆佸悗缁?OP 鏃犳硶浣胯兘杈撳嚭鈥濈殑琛屼负椋庨櫓銆?
---

## 5. 涓婁綅鏈轰笌 Host 鍗忚楠岃瘉

### 5.1 鏋勫缓涓庤嚜妫€锛堝疄娴嬪懡浠や笌杈撳嚭锛?
```bat
cd /d D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\tools\ecat_drone_host
build.cmd
::  project : ...\tools\ecat_drone_host
::  compiler: C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
::  sources : 29 file(s)
::  [BUILD OK] ...\build\ecat_drone_host.exe  size: 216576 bytes
build\ecat_drone_host.exe --selftest
::  鑷缁撴灉: 閫氳繃 43 椤癸紝澶辫触 0 椤?::  缁撹: 鍏ㄩ儴閫氳繃 (SELFTEST PASS)
```

`build.cmd` / `build.ps1` / `run.cmd` / `selftest.cmd` 鍧囧凡瀛樺湪锛坔ost-agent 11:24 琛ラ綈锛夈€?
### 5.2 `inc/host_proto.h` 鈫?`src/host_proto_app.c` 鈫?C# 宸ュ叿 浜ゅ弶鏍稿

| 椤圭洰 | 鍥轰欢瀹炵幇 | C# 瀹炵幇 | 鏂囨。 | 缁撴灉 |
|---|---|---|---|---|
| 甯ф牸寮?| `A5 5A CMD SEQ LEN_L LEN_H payload CRC16`锛坄host_proto.h:8-25`锛沗host_emit_response` 鍏堝啓 status锛?| `HostProtocol.cs:25-27` 涓€鑷?| `docs/USB_PROTOCOL.md:1-40` | 鉁?|
| CRC16-CCITT-FALSE 瑕嗙洊 CMD..payload | `host_proto_crc16`锛坄host_proto_app.c:283`锛夛紝`host_emit_response` 瑕嗙洊 `[2, 6+payload)` | `Crc16.CcittFalse(frame,2,4+payloadLen)` | 涓€鑷?| 鉁?|
| PING | 搴旂瓟 body = u32 magic + u16脳3 = 10 B锛屽姞 status = **LEN 11**锛坄host_proto_app.c:492-503`锛沗host_proto.h:44-46,236`锛?| `BoardClient.Ping()` 瑙ｆ瀽 magic@0銆佺増鏈珸4/6/8 | `USB_PROTOCOL.md:73,433-451`锛圥ING 搴旂瓟 LEN=11锛?| 鉁?|
| GET_TELEMETRY | 54 B `host_telemetry_t` 閫愬瓧娈靛～鍏呭苟蹇収 `g_ecat_txpdo`锛坄host_proto_app.c:518-565`锛?| `TelemetryData.Parse` 鍋忕Щ 0/2/4/8/12/14/16/18/20/22/24/26/28/29/30/31/32/34/36/38/40/42/44/46/50锛宍WireSize=54`锛坄TelemetryData.cs:36,79-119`锛?| 涓€鑷?| 鉁?|
| 鍙傛暟鎻忚堪绗?70 B | `host_fill_param_desc` + packed struct锛坄host_proto.h:171-183`锛?| `ParamDesc.cs:40-53` 鍋忕Щ/闀垮害涓?`HOST_PARAM_DESC_OFF_*` 瀹屽叏涓€鑷?| 涓€鑷?| 鉁?|
| LIST_PARAMS 鍒嗗潡 | 姣忓簲绛?鈮?`HOST_PROTO_LIST_PARAMS_MAX=7` 鏉★紝甯?start/count锛坄host_proto_app.c:697-733`锛?| 鎸夎繑鍥炴潯鏁拌凯浠ｏ紙`BoardClient`/`ParamCatalog`锛?| `USB_PROTOCOL.md:97-100` | 鉁?|
| SERVO_SCAN 寮傛閲嶈瘯 | 鎵弿杩愯涓繑鍥?`ERR_BUSY`锛屽畬鎴愬悗 OK+32 B bitmap锛坄host_proto_app.c:847-908`锛?| 鏀跺埌 `ErrBusy` 閫掑璁℃暟骞堕噸璇曪紝鏈€闀?12 s锛坄BoardClient.cs:546-581`锛?| 涓€鑷?| 鉁?|
| SERVO_RAW 甯冨眬 | 璇锋眰 `u8 bus,u8 id,u16 reg,u8 wr_len,u8 rd_len,u8[wr_len] data`锛涘簲绛?`u8 rd_len_actual,u8[rd_len_actual] data`锛坄host_proto_app.c:988-1032`锛?| 涓绘満渚ц姹?搴旂瓟鏋勯€犱笌 `USB_PROTOCOL.md:139` 涓€鑷达紱S288 椹卞姩涓嶆敮鎸佸瘎瀛樺櫒璁块棶鏃惰繑鍥?`ERR_UNSUPPORTED`锛岀晫闈㈡樉绀衡€滀笉鏀寔鈥?| 涓€鑷?| 鉁?|
| AM32_STATUS | 璇锋眰 `u8 esc`锛涘簲绛?`u8 state,u8 last_error`锛坄host_proto.c:1211-1220`锛?| `Am32BridgeStatus.Parse` 璇?`body[0],body[1]`锛坄Protocol/BoardData.cs`锛?| `USB_PROTOCOL.md:199-205` | 鉁?|
| Bench 绉熺害鍒锋柊 | `DSHOT_TEST`/`DSHOT_CMD`/`SERVO_MOVE_TEST` 鍧囪皟鐢?`host_bench_refresh()`锛?764/827/935锛夛紱瓒呮椂/杩?OP 鑷姩閲婃斁骞跺仠鏈猴紙:340-385锛?| 涓婁綅鏈烘瘡娆℃祴璇曞懡浠ゅ埛鏂帮紱鏂囨。瑕佹眰 鈮? Hz 鍒锋柊 | `USB_PROTOCOL.md:312-326` | 鉁?|

### 5.3 鍗忚/鑸垫満娴嬭瘯锛坈omms-agent 涓?servo-agent 鎻愪緵锛?
```powershell
python .work\proto_test\test_host_proto.py
# commands: 28  parameters: 60  telemetry: 54 bytes  param desc: 70 bytes
# All 218 checks OK
python .work\servo_test\test_servo_frames.py
# RESULT: 58 passed, 0 failed 鈥?ALL TESTS PASSED
```

锛堜袱濂楁祴璇曞湪鏈€缁堟簮鐮佺姸鎬佷笅閲嶆柊杩愯锛屼粛鍏ㄩ儴閫氳繃锛涜鐩?28 鏉?`HOST_CMD_*`銆?0 涓弬鏁般€?4 B 閬ユ祴銆?0 B 鎻忚堪绗︺€佽埖鏈?20/26 B 甯т笌 CRC32銆佹壂鎻忎綅鍥捐涔夌瓑銆傦級

---

## 6. 鍙傛暟绯荤粺

### 6.1 琛ㄥ畬鏁存€?
- `param.h` 鏋氫妇 60 椤癸紙`PARAM_ECAT_NODE_ALIAS=0` 鈥?`PARAM_SERVO_KD2=59`锛宍PARAM_COUNT=60`锛夛紝缂栬瘧鏈熷疄娴?`PARAM_COUNT=60`锛埪?.1锛夈€?- `src/param.c:76-272` 浣跨敤 **designated initializer** `[PARAM_xxx] = {...}` 濉弧 60 涓?id锛岃剼鏈鏍革細`enum entries: 60  table entries: 60`锛堟棤缂哄け id銆佹棤 id 0x2000..0x203B 缂洪」锛夈€?- 缂栬瘧鏈熷畧鎶わ細`param.c:277` `_Static_assert(APP_ARRAY_SIZE(s_param_desc)==PARAM_COUNT)`锛?279-293 瀵?11 缁勫弻閫氶亾 `PARAM_xxx1+1==PARAM_xxx2` 鍙?`PARAM_xxx(ch)` 瀹忓睍寮€鍋氫簡闈欐€佹柇瑷€锛涢厤鍚?`param_init()` 杩愯鏈熷绌烘弿杩扮鐨勬鏌ワ紙:544-551锛夈€?- Lead 缁欏嚭鐨?id 鏄犲皠锛圞P1/2=56/57銆並D1/2=58/59锛変笌鏋氫妇涓€鑷达紙enum 椤哄簭鎺ㄧ畻 56..59锛夈€?- min/max/default 鎶芥煡锛歚PARAM_DSHOT_RATE` 棰濆闄愬畾 150/300/600/1200锛坄param.c:403-407`锛夛紱`PARAM_SERVO_BAUD` 涓婇檺 6,000,000锛沗PARAM_DSHOT_MIN/MAX_THROTTLE` 榛樿 48/2047锛沗PARAM_ADC_ENABLE_PROTECTION` 榛樿 0锛堟湭鏍囧畾鍓嶅叧闂繚鎶わ紝涓?bring-up 鏂囨。涓€鑷达級锛沗PARAM_ADC_VOLT_MIN/MAX_DV` 榛樿 0=绂佺敤銆傞粯璁ゅ€兼弧瓒?min鈮ef鈮ax锛?*鏈己鍒舵牎楠?* `PARAM_SERVO_ANGLE_MINn < MAXn` 鐨勬垚瀵瑰叧绯伙紙浣庨闄╋紝瑙?搂8-L4锛夈€?
### 6.2 鏍￠獙涓庣被鍨?
- `param_check_type()`锛坄param.c:355-384`锛夛細U8/U16/U32 璐熷€间笌涓婄晫銆両16 鑼冨洿銆両32 鐩存帴鎺ュ彈锛沗param_check_value()`锛?388-409锛夊啀鍋?min/max 涓?DShot rate 鏋氫妇鏍￠獙銆?- `param_clamp()`锛?411-424锛夛細浠?Flash 璇诲嚭鐨勫€煎厛鎸夌被鍨?鑼冨洿閽充綅锛屽紓甯稿€煎洖閫€榛樿鍊硷紱`persist=0` 鐨勫弬鏁帮紙`PARAM_USB_AUTO_TELEMETRY_HZ`锛夊姞杞芥椂寮哄埗榛樿鍊硷紙:576-582锛夈€?
### 6.3 Flash 瀛樺偍瀹夊叏鎬?
- 瀛樺偍浣嶇疆锛歑IP 鍦板潃 = `BOARD_FLASH_BASE_ADDRESS + PARAM_FLASH_OFFSET(0x7E000)`锛坄param.c:325`锛夛紝4 KB 鎵囧尯锛坄param.h:41-42`锛夈€?- 鏁版嵁鍧?`param_flash_blob_t`锛氬ご閮?16 B锛坄magic=0x41524950`銆乣version=1`銆乣count=60`銆乣crc16`銆乺eserved銆乸ad锛? 60脳i32 鍊硷紝**闈欐€佹柇瑷€ = 鎭板ソ 256 B = 涓€涓?NOR page**锛屼笖 `values` 鍋忕Щ = 16銆丆RC 2 瀛楄妭瀵归綈锛坄param.c:306-323`锛夈€?- CRC锛欳RC16-CCITT-FALSE 瑕嗙洊鏁翠釜 blob锛堣绠楁椂 crc 瀛楁缃?0xFFFF锛夛紝淇濆瓨/鍔犺浇鐢ㄥ悓涓€绠楁硶锛?439-464锛夛紱鏍￠獙 magic + version + count + CRC锛屼换涓€涓嶇 鈫?`PARAM_ERR_FLASH`銆?- 鎿﹀啓娴佺▼锛坄param_flash_store`锛?473-530锛夛細鍏冲叏灞€涓柇 鈫?棣栨璋冪敤 `rom_xpi_nor_auto_config()` 骞剁紦瀛橀厤缃?鈫?鎿﹂櫎 `PARAM_FLASH_OFFSET..+0x1000` 涓€涓墖鍖?鈫?缂栫▼ 256 B 鈫?`fencei()` 鈫?**鍥炶 memcmp + 鍐嶆鏍￠獙**锛涘け璐ヨ鏁板苟杩斿洖閿欒銆傜┖鐗囷紙鍏?0xFF锛夋垨鎹熷潖鍧楀潎璧扳€滀娇鐢ㄩ粯璁ゅ€?+ 鍛婅鈥濊矾寰勶紙`param_init:553-559`锛夛紝涓嶄細鍐欏潖浠ｇ爜鍖猴紙鎿﹂櫎鍦板潃鍥哄畾涓哄弬鏁版墖鍖猴級銆?- 涓庝唬鐮佸尯/EEPROM 鍖哄叧绯伙細鍥轰欢 FLASH 鍗犵敤 **154,876 B**锛岃繙灏忎簬鍙傛暟鍖鸿捣鐐?0x7E000锛?16,096 B锛夛紱`.bin` 161,800 B銆傚弬鏁板尯 0x7E000-0x7EFFF 涓?EtherCAT EEPROM 妯℃嫙鍖?0x80000-0x8FFFF锛?6脳4 KB锛宍ecat_config.h:39-45`锛?*涓嶉噸鍙?*锛屼腑闂?0x7F000-0x7FFFF 绌洪棽銆傞摼鎺ュ櫒鏈彁渚?0x7E000 鐨勯摼鎺ユ湡纭笂闄愶紙`param.h` 娉ㄩ噴鎻愬強鈥渓inker script 涓湁妫€鏌モ€濓紝瀹為檯 SDK `flash_xip.ld` 鏈寘鍚妫€鏌ワ級鈥斺€斿綋鍓?30% 浣欓噺瓒冲锛屽缓璁悗缁湪閾炬帴鑴氭湰/CMake 鍔?`ASSERT(_flash_end < 0x7E000)` 纭繚鎶わ紙浣庨闄╋級銆?
### 6.4 杩愯鏈熼闄?
- ~~`param_save()` 鍦ㄦ摝闄?缂栫▼鏈熼棿鍏抽棴鍏ㄥ眬涓柇锛岀敱 USB 鍛戒护 `HOST_CMD_PARAM_SAVE` 鍦?EtherCAT OP 涓皟鐢ㄤ細灞忚斀 SYNC/PDI 涓柇銆倊~ **宸蹭慨澶嶏紙Lead 闆嗘垚淇锛?*锛歚host_cmd_param_save()` 鐜板湪鍦?`g_ecat_op_active != 0` 鏃剁洿鎺ヨ繑鍥?`HOST_STATUS_ERR_NOT_READY`锛孫P 涓笉浼氭墽琛?Flash 鎿﹀啓锛涗笂浣嶆満闇€鍦ㄩ潪 OP 鐘舵€佷繚瀛樺弬鏁帮紙`host_proto_app.c` 鐨?PARAM_SAVE 澶勭悊锛夈€?
---

## 7. 鏂囨。涓€鑷存€?
| # | 鏂囨。浣嶇疆 | 鏂囨。璇存硶 | 浠ｇ爜/瀹為檯 | 澶勭悊 |
|---|---|---|---|---|
| 1 | `inc/ecat_pdo.h` 娉ㄩ噴銆乣README_zh.md` 搂5 | reserved0@14銆乧ycle_counter@16 | 瀹為檯 packed 甯冨眬 **16/18**锛坉shot_cmd[2] 4 B锛?| **宸蹭慨澶?*锛圠ead锛涘楠?`ecat_pdo.h:43-44`銆乣README_zh.md:157-158`锛?|
| 2 | `SSC/ecat_drone.xlsx` / CSV / ESI | 11 涓?CoE 瀵硅薄绫诲瀷閿欒锛圲DINT 绛夛級 | 瀹為檯 60 椤圭被鍨嬭 `src/param.c` | **宸蹭慨澶?*锛坋cat-agent 閲嶆柊鐢熸垚锛涙湰杞?60/60 澶嶉獙閫氳繃锛屄?.4锛?|
| 3 | `docs/PINMAP.md` 搂7 | 鈥淔lash 0x80000 璧?16KB鈥?EtherCAT EEPROM 妯℃嫙鍖?| `ecat_config.h:40-45`锛歚FLASH_EEPROM_SIZE=0x10000`锛?4 KB = 16脳4 KB 鎵囧尯锛?| **宸蹭慨澶?*锛坴erifier锛?|
| 4 | `docs/PINMAP.md` 搂7 | 璧勬簮琛ㄧ己 GPTMR2 ch0锛圗therCAT 1 ms tick锛夈€丳WM0 CMP16锛圖Shot TX DMA 瑙﹀彂锛夈€丒CAT 涓柇锛涘垪浜嗘湭浣跨敤鐨?`IRQn_XDMA` | `ecat_config.h:65-68`銆乣dshot_bidir.c:136-138/607-611/671`銆乣port/hpm_ecat_hw.c:300-306` | **宸蹭慨澶?*锛坴erifier 琛ュ厖/鏇存璧勬簮琛級 |
| 5 | `docs/ARCHITECTURE.md` 搂6 / 搂2 | 鍙傛暟鈥滅害 70 椤光€濓紱DShot 璧勬簮鏈彁 CMP16 | `PARAM_COUNT=60`锛汣MP16 鐢ㄤ綔 TX DMA 璇锋眰 | **宸蹭慨澶?*锛坴erifier锛氭敼涓?60 椤广€佽ˉ鍏?CMP16锛?|
| 6 | `inc/dshot.h:29-30` | 鈥淧WM0 counter1 reload 鈫?PWM DMA request channel 1 鈫?`HPM_TRGM0_DMA_SRC_PWM0_1`鈥?| 瀹炵幇鐢ㄧ嫭绔嬫瘮杈冪偣 **CMP16** 鏄犲皠涓?`pwm_dma_0` 鈫?`HPM_TRGM0_DMA_SRC_PWM0_0`锛坄dshot_bidir.c:26-31,136-138,153,607-611,671`锛涗笌 SDK `pwmv2_enable_dma_at_compare_point()` 鐢ㄦ硶涓€鑷达級 | **宸叉姤鍛?*锛堝ご鏂囦欢娉ㄩ噴杩囨椂锛屼唬鐮佽嚜娲斤紱寤鸿 dshot-agent 鏀规敞閲婏級 |
| 7 | 鏍?`README_zh.md` 搂1/搂4 | 寮曠敤 `tools/ecat_drone_host/README_zh.md` | 璇ユ枃浠?*涓嶅瓨鍦?*锛坆uild.cmd/build.ps1/run.cmd/selftest.cmd 宸插瓨鍦級 | **宸叉姤鍛?*锛坔ost-agent 闇€琛?README 鎴?Lead 鏀瑰紩鐢級 |
| 8 | 宸ョ▼ `README_zh.md` 搂1 寮曡剼琛?| 鈥淧C08/09 + PC16锛汸C14/15 + PD00鈥?| 瀹為檯 pinmux/board.h锛歅C14=UART3_RXD銆丳C15=UART3_TXD锛坄boards/ecat_drone_5e3y/pinmux.c:182-184`锛屼笌 `docs/PINMAP.md:50-51` 涓€鑷达級 | **宸叉姤鍛?*锛堣〃杩伴『搴忔槗璇锛屽缓璁槑纭?TX/RX锛?|
| 9 | `docs/USB_PROTOCOL.md` | PING LEN=11銆侀仴娴?54 B锛圠EN=55锛夈€佹弿杩扮 70 B銆丩IST鈮?銆丼ERVO_RAW/AM32_STATUS/bench 绉熺害璇箟 | 涓?`inc/host_proto.h` + `host_proto_app.c` + C# 瀹炵幇涓€鑷达紙搂5.2锛?| 鉁?涓€鑷?|
| 10 | `docs/PINMAP.md` 搂1-搂6 | 寮曡剼/澶栬鏄犲皠 | 涓?`board.h`/`pinmux.c` 涓€鑷达紙UART2 PC08/09銆乁ART3 PC14/15銆丏Shot PF02/03銆丷GB PF12-14銆丄DC PF26/PF16/PF20/PF19锛?| 鉁?涓€鑷?|
| 11 | 鏉跨骇 `README_zh.rst` | EEPROM 0x80000銆佸簲鐢ㄩ暅鍍忔渶澶?512 KB銆乁ART3 PC14 RXD/PC15 TXD銆乁ART2/3 鏃堕挓 48 MHz | `ecat_config.h`銆乣clock.c`銆乣pinmux.c` 涓€鑷?| 鉁?|
| 12 | 鏍?`README_zh.md` 搂4 / 宸ョ▼ README | 鎸囧悜 `docs/VERIFICATION.md`銆乣docs/DSHOT_AM32.md`銆乣docs/SERVO_S288.md`銆乣SSC/README.md` | 杩欎簺鏂囦欢鍧囧凡瀛樺湪锛坄docs/DSHOT_AM32.md`銆乣docs/SERVO_S288.md`銆乣docs/ECAT_SSC.md`銆乣SSC/README.md`锛?| 鉁咃紙鏈枃浠跺嵆 VERIFICATION.md锛?|

鏈疆 verifier 鍙慨鏀逛簡鏂囨。锛歚docs/PINMAP.md`锛埪? 璧勬簮琛級銆乣docs/ARCHITECTURE.md`锛?0 椤?/ CMP16锛夛紝骞舵柊澧炴湰鏂囦欢锛?*鏈慨鏀逛换浣曟簮鐮?濂戠害澶存枃浠?*銆?
---

## 8. 浠ｇ爜瀹℃煡鍙戠幇锛堟寜涓ラ噸绾у埆锛?
> 瀹℃煡鑼冨洿锛歚src/*.c`銆乣port/*.c`銆乣SSC/StubSrc`銆乣inc/*.h` 鐨勫父瑙佸祵鍏ュ紡缂洪櫡妯″紡锛圛SR 鍏变韩鏁版嵁銆乿olatile銆侀櫎闆躲€佸閮ㄦ暟鎹储寮曘€佹棤鐣屽惊鐜€両SR 涓?printf銆佹爤鍗犵敤銆佷腑鏂紭鍏堢骇锛夛紱骞剁敤 `grep`/鑴氭湰鍋氫簡绯荤粺鎬ф绱€?
**Critical锛? 椤广€?*

**涓瓑锛堜腑锛?*

| ID | 浣嶇疆 | 鍙戠幇 | 褰卞搷/寤鸿 |
|---|---|---|---|
| M1 | `host_proto_app.c` PARAM_SAVE 澶勭悊 | ~~`PARAM_SAVE` 鍦?EtherCAT OP 涓篃浼氭墽琛岋紱Flash 鎿﹂櫎/缂栫▼鏈熼棿鍏冲叏灞€涓柇~~ | **宸蹭慨澶嶏紙Lead锛?*锛歄P 涓繑鍥?`ERR_NOT_READY`锛屼笉鍐嶆墽琛?Flash 鎿﹀啓 |
| M2 | `am32_4way.c:30-31` vs `:252-317` | 鏂囨。绉版渶鍧忛樆濉?~150 ms锛涘疄闄呮渶鍧?鈮?TX锛?08 B@19200 鈮?108 ms锛? RX 涓婇檺 150 ms 鈮?**260 ms**锛屾湡闂翠富寰幆锛堝惈 EtherCAT 鏍堬級琚嫭鍗?| 鑻ヤ富绔欏湪 AM32 璋冨弬杩囩▼涓惎鍔紝鍙兘鍑虹幇 EtherCAT 閫氫俊涓柇銆傚缓璁敹绱?`AM32_4WAY_MAX_RESPONSE`/鎬昏秴鏃舵垨浠呭厑璁稿湪 ECAT 鏈笂鐢垫椂浣跨敤锛圔EGIN 宸叉嫆缁?OP锛屼絾 XFER 鏈熼棿鏃犱簩娆℃鏌ワ級銆?*淇濈暀涓哄凡鐭ラ檺鍒?*锛圶FER 浠呭湪闈?OP + 鍙版灦妯″紡鍙敤锛屽疄闄呬娇鐢ㄤ腑涓荤珯涓嶄細鍚屾椂鍦?OP锛?|
| M3 | `ecat_app.c` CoE 璇诲啓 | ~~CoE 32 浣嶅璞¤鍐欓€氳繃 `*(UINT32 *)pData`锛屽瓨鍦ㄩ潪瀵归綈璁块棶椋庨櫓~~ | **宸插楠屼负涓嶉€傜敤**锛氬綋鍓嶄唬鐮?4 瀛楄妭瀵硅薄鐢ㄤ袱娆?16 浣?mailbox 璁块棶锛坄ecat_app.c` ecat_coe_obj_read/write锛夛紝鍏ㄦ枃浠舵棤 `UINT32*` 寮鸿浆 |

**浣庯紙浣庯級**

| ID | 浣嶇疆 | 鍙戠幇 | 寤鸿 |
|---|---|---|---|
| L1 | `usb_cdc_app.c:186-190` | USB RX 鐜舰缂撳啿婧㈠嚭鏃跺湪 USB 涓柇涓婁笅鏂囪皟鐢?`LOGM_W`锛圲ART0 printf 闃诲锛?| 鏀逛负璁℃暟/缃爣蹇楋紝涓诲惊鐜墦鍗?|
| L2 | `ecat_app.c:74,287,389,395,507`锛沗ecat.c:83` | 鏈帴绾?鍐椾綑 API 涓庡彧鍐欏彉閲忥細`ecat_app_outputs_enabled`銆乣ecat_led_set_run/err`銆乣ecat_ssc_stop`銆? 涓?DC getter銆乣s_last_input_tick_ms` | 浜や粯鍓嶆竻鐞嗘垨鍦ㄧ湡瀹炲崗璁爤 glue 涓寕鎺ワ紙灏ゅ叾 LED 鍥炶皟锛?|
| L3 | `ecat_app.c:293-310` vs `app_task.c:276-293` | 涓ゅ鐙珛鐪嬮棬鐙楋紙ESC `WDG_STAT_PDAT` + 杞欢 cycle_counter锛夛紱app_task 鏈娇鐢?`ecat_app_watchdog_expired()` | 淇濈暀鍏朵腑涓€濂楁垨鏄庣‘涓讳粠鍏崇郴锛岄伩鍏嶈涓哄垎鍙?|
| L4 | `param.c`锛堣〃瀹氫箟锛?`servo_s288.c:1050-1090` | 鏈己鍒?`PARAM_SERVO_ANGLE_MINn < MAXn`锛涜嫢閰嶇疆鍙嶄簡锛岀洰鏍囪浼氳閽冲埌 max | 鍦?`param_set()`/`servo_set_target_d10()` 澧炲姞鎴愬鏍￠獙 |
| L5 | `port/hpm_ecat_hw.c:300-306` | ECAT SYNC0/1 涓庤埖鏈?HDMA/ECAT 瀹氭椂鍣ㄥ悓涓轰紭鍏堢骇 3锛屼綆浜?USB/鑸垫満 UART锛?锛?| 1 ms 鍛ㄦ湡涓嬪彲鎺ュ彈锛涜嫢 DC 鎶栧姩鏁忔劅鍙妸 SYNC 鎻愬埌 1~2 |
| L6 | `usb_cdc_app.c` RX 璺緞 | USB 涓柇鍐?/ 涓诲惊鐜 CSTD 鐜舰缂撳啿锛屼緷璧?CherryUSB `usb_ringbuffer` 鑷韩鐨勫唴瀛樺簭淇濊瘉锛屾湭瑙佹樉寮忓睆闅?| 甯歌鐢ㄦ硶鍙帴鍙楋紱闈欐€佸垎鏋愭棤娉曡瘉鏄庯紝纭欢闀胯窇楠岃瘉 |
| L7 | `am32_4way.c:118-124` 绛?| 浣嶇炕杞?bit-bang 浣跨敤蹇欑瓑锛?9200 baud 涓嬫瘡 bit 52 碌s锛?| 灞炵淮鎶よ矾寰勪笖 BEGIN 宸查檺鍒?OP锛屽彲鎺ュ彈锛涙枃妗ｅ凡璇存槑 |

**姝ｉ潰缁撹锛堥噸鐐规娊鏌ユ湭鍙戠幇闂锛?*

- 鏃?`malloc/calloc/free` 绛夊姩鎬佸垎閰嶏紙鍏ㄥ伐绋嬫绱級銆?- 澶栭儴鏁版嵁绱㈠紩鍧囧仛杈圭晫妫€鏌ワ細`dshot_set_throttle`锛坄dshot_bidir.c:1254`锛夈€乣dshot_get_telemetry`锛?1300锛夈€乣dshot_send_command`銆乣dshot_line_*`銆乣servo_*`锛坈h/bus锛夈€乣host_proto_app` 鐨?esc/bus/param id/闀垮害銆乣ecat_coe_param_id()`锛堜互 `param_count()` 涓虹晫锛夈€?- 闄ら浂闃叉姢锛歚dshot_telemetry_to_erpm`锛坧eriod==0锛?375锛夈€乣dshot_decode_edges`锛坰lot==0/杈圭紭鏁帮紝:475-478锛夈€丩ED 鍛煎惛 `period<2`锛坄led_rgb.c:260-268`锛夈€佽埖鏈烘尝鐗圭巼璁＄畻锛坆aud 涓嬮檺锛夛紱`app_task_cmd_us_to_dshot` 闄ゆ暟涓虹紪璇戞湡甯搁噺 1000銆?- ISR 涓棤 `printf`锛堝敮涓€渚嬪 L1 鐨勬孩鍑哄憡璀︼級锛汥Shot PWM/GPTMR1 ISR 鍙仛鐭搷浣滀笌鏍囧織鏇存柊锛涙棤澶у眬閮ㄦ暟缁勶紙鏈€澶?28 B `uint32_t delta[24]` 鍦?`dshot_decode_edges` 涓诲惊鐜矾寰勶紝ISR 鏃犲ぇ鏁扮粍锛夈€?- DMA 缂撳啿锛欴Shot `s_tx_dma_buf` 32 瀛楄妭瀵归綈 + non-cacheable锛坄dshot_bidir.c:253`锛夛紝鑸垫満 TX/RX 甯?non-cacheable锛坄servo_s288.c:753-755`锛夛紝USB 绔偣缂撳啿瀵归綈 + non-cacheable锛坄usb_cdc_app.c:150`锛夈€?- ISR 涓庝富寰幆鍏变韩鏍囧織鍧?`volatile`锛歚rx_armed/rx_edges/rx_edge_ts`銆佽埖鏈?`tx_done/rx_done/rx_idle/dma_error`銆乁SB `s_tx_busy` 绛夛紱鍏变韩 ring 鐨勫叆闃?鍑洪槦鏈変复鐣屽尯锛坄usb_cdc_app.c:290-303`锛夈€?- 鏍堬細榛樿 `.stack` 16 KB锛堝崟绾跨▼瑁告満锛孖SR 鍏辩敤锛夛紝鏃犻€掑綊銆佹棤澶у眬閮ㄦ暟缁勶紱鏈仛 `-fstack-usage` 瀹炴祴锛堣 搂9锛夈€?
---

## 9. 鏈獙璇侀」涓庣‖浠舵祴璇曞缓璁?
| 椤圭洰 | 鐘舵€?| 寤鸿 |
|---|---|---|
| **鐪熷疄 Beckhoff SSC 鍗忚鏍?* | 鏈獙璇侊細`SSC/Src` 涓嶅瓨鍦紙鎺堟潈鍘熷洜锛夛紝鏈浐浠堕摼鎺ョ殑鏄?`SSC/StubSrc`锛?*娌℃湁鐪熸鐨?EtherCAT 閫氫俊/CoE/SM watchdog/DC** | 鎸?`SSC/README.md` + `docs/ECAT_SSC.md` 鐢?SSC Tool 鐢熸垚 `SSC/Src` 鍚庨噸鏂版瀯寤猴紱鐢熸垚鏃舵敞鎰?`HPM_ECAT_DRONE_Config.xml` 鐨?`APPLICATION_FILE=#include "ecat_drone.h"` 闇€瑕佷笌瀹為檯搴旂敤澶存枃浠跺搴旓紙鐢熸垚鍚庨娆＄紪璇戦渶纭锛夛紱闅忓悗鍦?TwinCAT 涓嬮獙璇?PDO 鏄犲皠銆丄L 鐘舵€併€丼M 鐪嬮棬鐙椾笌 DC |
| DShot 鍙屽悜鏃跺簭锛圥F02/PF03銆丟PTMR1 鎹曡幏銆?1bit鈫扜CR鈫抏RPM锛?| 浠呬富鏈轰晶绠楁硶浠跨湡锛坉shot-agent 鎻愪緵 `.work/dshot_test`锛夛紝鏃犵‖浠舵尝褰?| 绀烘尝鍣ㄩ獙璇?idle 楂樸€?6 bit 甯с€佸弽鐩?CRC銆丒SC 鍥炰紶绐楀彛锛涙帴 AM32 瀹炴祴 eRPM |
| AM32 4-way 妗ワ紙19200 8N1 浣嶇炕杞€佸崐鍙屽伐锛?| 鏃犵‖浠堕獙璇?| 鐢?AM32 鐢佃皟瀹炴祴 EEPROM 璇?鍐欙紱纭鏈€闀块樆濉炲涓诲惊鐜殑褰卞搷锛埪?-M2锛?|
| S288 6 Mbps 鍗曟€荤嚎 + SPDT 鏂瑰悜鍒囨崲 | 鍗曞厓娴嬭瘯閫氳繃锛堝抚鏋勯€?瑙ｆ瀽 58 椤癸級锛屾棤娉㈠舰 | 绀烘尝鍣ㄩ獙璇?6 Mbps 娉㈠舰銆乀X鈫扲X 鍒囨崲鏃跺簭锛? 碌s setup锛夈€丆RC32 瑕嗙洊鑼冨洿锛涘厛鍗曡埖鏈哄啀鍙岃埖鏈?|
| ADC 鏍囧畾锛堢郴缁熺數鍘?鐢垫祦銆丒SC 鐢垫祦锛変笌淇濇姢闃堝€?| 浠ｇ爜閫昏緫瀹¤閫氳繃锛屾棤瀹炴満鏍囧畾 | 涓囩敤琛ㄥ姣旀爣瀹?`PARAM_ADC_*`锛涚‘璁ら噺绋?鍋忕疆/鏋佹€э紱鏍囧畾鍚庡啀寮€ `PARAM_ADC_ENABLE_PROTECTION` |
| 鐗囧唴 MII PHY锛圡DIO 鍦板潃 1/2銆佸浣嶃€丯MII 閾捐矾鏋佹€э級 | 浠ｇ爜渚濇嵁銆婄墖鍐?MII PHY 鎸囧崡銆嬪疄鐜帮紝鏃犵‖浠堕獙璇?| 涓婄數鐪?`ecat_phy_mdio_probe()` 鏃ュ織銆佹彃鎷旂綉绾跨湅 link 浣嶏紱蹇呰鏃惰皟鏁?`BOARD_ECAT_PHY_*` / `*_LINK_INVERT` |
| USB CDC 鏋氫妇涓?PC 宸ュ叿鑱旇皟 | 鑷浣跨敤 FakeFirmwareTransport锛堟ā鎷熻澶囷級锛岄潪鐪熷疄 USB | 瀹炴澘鎺?PC锛岄獙璇?VID/PID 0x0A4D:0xE3A2銆丳ING/閬ユ祴/鍙傛暟/AM32 椤甸潰 |
| 鏍堟繁搴﹀疄娴?| 鏈仛 `-fstack-usage`/濉厖娴嬭瘯 | 寤鸿瀵?ECAT 鐪熷疄鍗忚鏍堝姞鍏ュ悗娴嬫渶鍧忔爤娣憋紙褰撳墠 16 KB 鏍堬級 |
| Flash 鎿﹀啓瀵垮懡/鎺夌數瀹夊叏 | 閫昏緫瀹¤閫氳繃锛堝崟鎵囧尯銆佸洖璇绘牎楠岋級锛屾棤鎺夌數娴嬭瘯 | 鍙嶅 param_save / EEPROM 妯℃嫙鍖哄啓鍏ワ紝楠岃瘉鎺夌數鏃?magic+CRC 鍥為€€榛樿鍊?|
| 鏁存満 EMC/鍔熻€?鐑€丒TG 涓€鑷存€ф祴璇?| 涓嶅叿澶囨潯浠?| 纭欢闃舵琛ュ厖 |

---

## 10. 闄勶細楠岃瘉鑴氭湰涓庤瘉鎹枃浠?
| 鏂囦欢 | 璇存槑 |
|---|---|
| `.work\build_verify\build_flash_xip.log` | 鍏ㄩ噺鏋勫缓鏃ュ織锛? warning锛屽唴瀛樿〃锛?|
| `.work\build_verify\contract_check.c` | 缂栬瘧鏈熷绾︽鏌ユ簮锛坄_Static_assert`锛?|
| `.work\build_verify\contract_check.obj` | 鐩爣宸ュ叿閾剧紪璇戜骇鐗╋紙`nm -S` 鎻愬彇澶у皬锛?|
| `.work\build_verify\check_coe_types.py` | CoE 60 瀵硅薄绫诲瀷 鈫?`src/param.c` 浜ゅ弶鏍稿鑴氭湰 |
| `D:\Ecat_Drone_Vector_Powertrain\.work\proto_test\test_host_proto.py` | 218 椤?host 鍗忚娴嬭瘯锛堝叏閮ㄩ€氳繃锛?|
| `D:\Ecat_Drone_Vector_Powertrain\.work\servo_test\test_servo_frames.py` | 58 椤硅埖鏈哄抚娴嬭瘯锛堝叏閮ㄩ€氳繃锛?|
| `sdk_env_v1.13.0\user_template\tools\ecat_drone_host\build\selftest.log` | 涓婁綅鏈鸿嚜妫€ 43/43 閫氳繃鎶ュ憡 |

**Git 鐘舵€侊細鏈仛浠讳綍 commit锛堟寜浠诲姟瑕佹眰锛夈€?* verifier 鐨勬敼鍔ㄤ粎涓?`docs/PINMAP.md`銆乣docs/ARCHITECTURE.md` 鐨勬枃妗ｄ慨姝ｄ笌鏈?`docs/VERIFICATION.md` 鏂板銆?
