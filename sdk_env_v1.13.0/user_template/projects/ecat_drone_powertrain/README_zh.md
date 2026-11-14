# ecat_drone_powertrain 鈥?EtherCAT 鏃犱汉鏈虹煝閲忓姩鍔涙€绘垚浠庣珯鍥轰欢

> 鐩爣鑺墖锛?*HPM5E3YIGN1**锛堝厛妤崐瀵间綋锛屽唴缃?1MB XPI NOR Flash + 2 璺墖鍐?MII PHY锛?> 鏉跨骇鍖咃細`user_template/boards/ecat_drone_5e3y`
> SDK锛歨pm_sdk v1.13.0锛坄sdk_env_v1.13.0`锛?> 涓婁綅鏈猴細`user_template/tools/ecat_drone_host`锛圕# WinForms锛?
---

## 1. 鍔熻兘姒傝堪

鏈伐绋嬪疄鐜颁竴涓?EtherCAT 浠庣珯鍔ㄥ姏鎬绘垚鎺у埗鍣細

| 鍔熻兘 | 瀹炵幇鏂瑰紡 | 寮曡剼 |
|---|---|---|
| EtherCAT 浠庣珯锛? 涓綉鍙ｏ級 | ESC0 + 鐗囧唴 MII PHY0/PHY1锛屼粠绔欏崗璁爤锛圔eckhoff SSC锛岄渶鑷鐢熸垚锛?| PV/PW 鍐呴儴 MII锛孭A30/31 MDIO |
| 鍙屾棤鍒风數鏈烘帶鍒?| 鍙屽悜 DShot锛圔DShot锛?00 kbps锛孭WM0 杈撳嚭 + GPTMR1 鎹曡幏鍥炰紶 eRPM | PF02 = PWM0.P2锛孭F03 = PWM0.P3 |
| 鍙屾€荤嚎鑸垫満锛堝畤鏍?J288/S288锛?| UART2 / UART3锛?.0 Mbps 8N1锛屽崐鍙屽伐鍗曟€荤嚎锛孲PDT 鏂瑰悜鍒囨崲 | PC08/09 + PC16锛汸C14/15 + PD00 |
| 鐘舵€佹寚绀?| 1 涓?RGB 涓夎壊鐏紙缁?绾?钃濓級锛孭WM1 涓夐€氶亾娣峰悎棰滆壊 + 鍔ㄧ敾 | PF12/PF13/PF14 = PWM1.P4/P5/P6 |
| 绯荤粺鐢靛帇/鐢垫祦鐩戞帶 | ADC0 IN1 = 绯荤粺鐢垫祦锛孉DC0 IN8 = 绯荤粺鐢靛帇 | PF26 / PF16 |
| 鍙岀數璋冪數娴佹娴?| ADC1 IN3 = 鐢佃皟1 鐢垫祦锛孉DC1 IN13 = 鐢佃皟2 鐢垫祦 | PF20 / PF19 |
| 璋冭瘯鏃ュ織 | UART0 115200 8N1 | PA00/PA01 |
| PC 璋冨弬 | USB0 铏氭嫙涓插彛锛圕DC-ACM锛?| 涓撶敤 USB 寮曡剼 |
| AM32 鐢佃皟璋冨弬 | 4-way interface锛?9200 8N1 浣嶆搷浣滐級缁?USB 涓插彛妗ユ帴 | 澶嶇敤 DShot 淇″彿绾?|
| EtherCAT 鍙傛暟鍙敼 | 鍙傛暟琛?+ Flash 鎸佷箙鍖?+ CoE 瀵硅薄 0x2000+ + 涓婁綅鏈轰慨鏀?| 鈥?|

涓绘帶锛圗therCAT 涓荤珯锛変笅鍙戠殑鎸囦护锛?- 涓や釜鐢垫満鐨勬补闂ㄦ寚浠わ細**1000 ~ 2000**锛堝搴?DShot 娌归棬 48 ~ 2047锛夛紱
- 涓や釜鎬荤嚎鑸垫満鐨勬湡鏈涜搴︼紙0.1掳 鍗曚綅锛夛紱
- 鎺у埗瀛楋紙浣胯兘/鎬ュ仠/娓呮晠闅?鑸垫満浣胯兘锛夈€?
浠庣珯鍥炰紶锛?- 涓よ矾鍙屽悜 DShot 鍥炰紶鐨勭數鏈鸿浆閫燂紙eRPM锛屽師濮嬪€硷級+ 閬ユ祴鍛ㄦ湡锛?- 姣忎釜鑸垫満鐨勭洃鎺х姸鎬侊紙浣嶇疆銆侀€熷害銆佺數娴佷及绠椼€佹俯搴︺€侀敊璇爜锛夛紱
- 绯荤粺鐢靛帇/鐢垫祦銆佷袱璺數璋冪數娴侊紱
- 鐘舵€佸瓧銆佹晠闅滅爜銆丒therCAT 閾捐矾/AL 鐘舵€併€佺姸鎬佺伅鐘舵€併€?
---

## 2. 鐩綍缁撴瀯

```
sdk_env_v1.13.0/user_template/
鈹溾攢鈹€ boards/ecat_drone_5e3y/          # 鏉跨骇鏀寔鍖?鈹?  鈹溾攢鈹€ board.h                      # 鏉跨骇瀹忓畾涔夛紙鎵€鏈夊紩鑴?澶栬濂戠害锛?鈹?  鈹溾攢鈹€ board.c                      # 鏉跨骇鍒濆鍖栵紙鏃堕挓/涓插彛/寮曡剼/寤舵椂/瀹氭椂鍣?ECAT 纭欢锛?鈹?  鈹溾攢鈹€ clock.c / clock.h            # 鏃堕挓鏍?鈹?  鈹溾攢鈹€ pinmux.c / pinmux.h          # 寮曡剼澶嶇敤锛堢敱 pin_mux/pinmux.c 淇敼鑰屾潵锛?鈹?  鈹溾攢鈹€ ecat_drone_5e3y.yaml         # 鏉跨骇鎻忚堪锛圫oC/Flash/鐗规€э級
鈹?  鈹溾攢鈹€ ecat_drone_5e3y.cfg          # OpenOCD 涓嬭浇閰嶇疆
鈹?  鈹溾攢鈹€ CMakeLists.txt
鈹?  鈹斺攢鈹€ README_zh.rst                # 鏉跨骇璇存槑
鈹溾攢鈹€ projects/ecat_drone_powertrain/  # 搴旂敤宸ョ▼
鈹?  鈹溾攢鈹€ CMakeLists.txt
鈹?  鈹溾攢鈹€ app.yaml
鈹?  鈹溾攢鈹€ ecat.c                       # EtherCAT 鍗忚鏍堝惎鍔ㄨ兌姘达紙鍙傜収 ecat_io锛?鈹?  鈹溾攢鈹€ ecat_config.h / user_config.h
鈹?  鈹溾攢鈹€ inc/                         # 鍏叡澶存枃浠讹紙PDO銆佸弬鏁般€佸崗璁€佸悇妯″潡 API锛?鈹?  鈹溾攢鈹€ src/                         # 搴旂敤婧愮爜锛坢ain/app_task/dshot/servo/led/adc/usb/param/ecat_app锛?鈹?  鈹溾攢鈹€ port/                        # EtherCAT 纭欢鎶借薄灞傦紙鏉ヨ嚜 samples/ethercat/port锛?鈹?  鈹溾攢鈹€ SSC/                         # SSC Tool 杈撳叆鏂囦欢 + ESI + 绂荤嚎缂栬瘧 stub
鈹?  鈹溾攢鈹€ docs/                        # 璁捐鏂囨。锛圥DO/鍗忚/鑸垫満/DShot/AM32 绛夛級
鈹?  鈹斺攢鈹€ README_zh.md                 # 鏈枃浠?鈹溾攢鈹€ tools/ecat_drone_host/           # 涓婁綅鏈猴紙C#锛宑sc.exe 缂栬瘧锛?鈹斺攢鈹€ scripts/                         # build.cmd / build.ps1 / check_syntax.py 绛?```

---

## 3. 鏋勫缓

### 3.1 涓€閿瀯寤猴紙鎺ㄨ崘锛?
```bat
:: 鍦ㄤ换鎰?cmd 绐楀彛锛堟棤闇€鍏堣繍琛?start_cmd.cmd锛?cd /d D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\scripts
build.cmd                 :: debug + flash_xip
build.cmd clean           :: 娓呯悊鍚庨噸鏂版瀯寤?build.cmd release         :: release
build.cmd ram             :: 涓嬭浇鍒?RAM 璋冭瘯
```

浜х墿锛歚projects\ecat_drone_powertrain\build_ecat_drone_5e3y_flash_xip_debug\output\ecat_drone_powertrain.elf`锛堜互鍙?`.bin`锛夈€?
### 3.2 start_gui 鍥惧舰鍖栨瀯寤?
1. 鍙屽嚮 `sdk_env_v1.13.0\start_gui.exe`锛?2. 鍕鹃€?**Enable User Board Path**锛岄€夋嫨 `sdk_env_v1.13.0\user_template\boards`锛?3. 鍕鹃€?**Enable User Application Path**锛岄€夋嫨 `sdk_env_v1.13.0\user_template\projects`锛?4. Board 閫?`ecat_drone_5e3y`锛孲ample 閫?`ecat_drone_powertrain`锛?5. Build Type 閫?`flash_xip`锛岀偣鍑?Generate / Compile銆?
### 3.3 鎵嬪姩 CMake

```powershell
# 鍏堟墽琛?start_cmd.cmd 鎴栨墜鍔ㄨ缃幆澧冿紙瑙?scripts/build.ps1 寮€澶达級
cmake -G Ninja -S projects\ecat_drone_powertrain -B build_ecat_drone_5e3y `
      -DBOARD=ecat_drone_5e3y `
      -DBOARD_SEARCH_PATH=D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\boards `
      -DHPM_BUILD_TYPE=flash_xip -DCMAKE_BUILD_TYPE=debug
cmake --build build_ecat_drone_5e3y
```

### 3.4 鐑у綍 / 璋冭瘯

```bat
:: 浣跨敤 SDK 鑷甫 openocd锛團T2232 璋冭瘯鍣紝闇€鍏堝畨瑁?FTDI 椹卞姩锛?tools\openocd\bin\openocd -f boards\openocd\hpm5e00_all_in_one.cfg ^
    -c "program user_template\projects\ecat_drone_powertrain\build_ecat_drone_5e3y_flash_xip_debug\output\ecat_drone_powertrain.elf verify reset exit"
```

鎴栦娇鐢?start_gui 鐨?Flash 鎸夐挳 / Segger Embedded Studio / IAR 宸ョ▼锛圕Make 鏋勫缓鏃惰嚜鍔ㄧ敓鎴愶級銆?
---

## 4. EtherCAT 浠庣珯閰嶇疆

### 4.1 閲嶈锛氫粠绔欏崗璁爤锛圫SC锛夐渶鑷鐢熸垚

鐢变簬鍊嶇 SSC 鍗忚鏍堢殑鎺堟潈闄愬埗锛?*hpm_sdk 涓嶅寘鍚粠绔欏崗璁爤婧愮爜**锛坄samples/ethercat/ecat_io/SSC/Src` 鍙湁涓€涓?README.txt锛夈€?鏈伐绋嬪仛浜嗕互涓嬪鐞嗭細

- `SSC/Src/`锛氶鐣?SSC Tool 鐢熸垚浠ｇ爜鐨勭洰褰曪紙鏋勫缓绯荤粺妫€娴嬪埌 `SSC/Src/ecatappl.c` 鍚庤嚜鍔ㄤ娇鐢ㄧ湡瀹炲崗璁爤锛夛紱
- `SSC/StubSrc/`锛氱绾跨紪璇戞牎楠岀敤鐨?**鍗忚鏍?API 妗?*锛屼娇宸ョ▼鍦ㄦ湭鐢熸垚 SSC 浠ｇ爜鏃朵篃鑳界紪璇戙€侀摼鎺ャ€佽繍琛岋紙浠呭垵濮嬪寲 ESC 纭欢锛?*涓嶈兘鐪熸閫氫俊**锛夛紱
- `SSC/Config/HPM_ECAT_DRONE_Config.xml` + `SSC/ESI/HPM_ECAT_DRONE.xml`锛歋SC Tool 閰嶇疆鏂囦欢涓?ESI 鏂囦欢锛圥DO 甯冨眬涓?`inc/ecat_pdo.h` 涓ユ牸涓€鑷达級銆?
**鐢熸垚鐪熷疄鍗忚鏍堢殑姝ラ**锛堣瑙?`SSC/README.md`锛夛細

1. 浠?[ETG 涓嬭浇椤礭(https://www.ethercat.org/en/downloads.html) 涓嬭浇骞跺畨瑁?SSC Tool锛堥渶 ETG 浼氬憳璐﹀彿 + Vendor ID锛夛紱
2. `Tool -> Options -> Configurations` 瀵煎叆 `SSC/Config/HPM_ECAT_DRONE_Config.xml`锛?3. `File -> New -> Custom`锛岄€夋嫨 HPMicro ECAT 閰嶇疆锛屼繚瀛樺伐绋嬶紱
4. `Tool -> Application -> Import` 瀵煎叆鏈簲鐢ㄧ殑 PDO 鎻忚堪锛堣 `SSC/README.md`锛屼笌 `ecat_drone.xlsx`/CSV 瀵瑰簲锛夛紱
5. `Project -> Create new Slave Files`锛岃緭鍑虹洰褰曟寚鍚戞湰宸ョ▼ `SSC/Src`锛?6. 閲嶆柊鏋勫缓锛孋Make 浼氳嚜鍔ㄥ垏鎹㈠埌鐪熷疄鍗忚鏍堬紙鏃ュ織鏄剧ず `Using Beckhoff SSC slave stack from SSC/Src`锛夈€?
### 4.2 TwinCAT / 涓荤珯鍑嗗

1. 鎶婄敓鎴愮殑 ESI 鏂囦欢锛坄SSC/ESI/HPM_ECAT_DRONE.xml`锛夊鍒跺埌 `C:\TwinCAT\3.1\Config\Io\EtherCAT`锛?2. 涓荤珯鎵弿浠庣珯锛岄娆′娇鐢ㄩ渶 `EEPROM Update` 鍐欏叆 ESI锛堟湰宸ョ▼鏀寔 Flash 妯℃嫙 EEPROM锛屼笂鐢佃嚜鍔ㄥ垵濮嬪寲锛岃 `ecat_config.h`锛夛紱
3. PDO 闀垮害锛歊xPDO 22 瀛楄妭 / TxPDO 48 瀛楄妭锛堣绗?6 鑺備笌 `inc/ecat_pdo.h`锛夈€?
---

## 5. 杩囩▼鏁版嵁锛圥DO锛夊畾涔?
瀹屾暣瀹氫箟瑙?`inc/ecat_pdo.h`锛屾憳瑕侊細

### RxPDO锛堜富绔?鈫?浠庣珯锛?x1600锛?2 瀛楄妭锛?
| 鍋忕Щ | 绫诲瀷 | 鍚嶇О | 璇存槑 |
|---|---|---|---|
| 0 | u16 | control_word | bit0 杩愯/浣胯兘锛宐it1 娓呮晠闅滐紝bit2 鎬ュ仠锛?1 鍋滄锛夛紝bit3 鑸垫満杈撳嚭浣胯兘锛宐it4 DShot 3D 妯″紡 |
| 2 | u16[2] | esc_cmd_us | 鐢垫満 1/2 娌归棬锛?000~2000锛坲s 绛夋晥鍊硷級 |
| 6 | i16[2] | servo_angle_d10 | 鑸垫満 1/2 鏈熸湜瑙掑害锛屽崟浣?0.1掳 |
| 10 | u16 | servo_ctrl_word | bit0 鑸垫満1 浣胯兘锛宐it1 鑸垫満2 浣胯兘锛宐it2 娓呰埖鏈烘晠闅?|
| 12 | u16[2] | dshot_cmd | 鐩存帴涓嬪彂 DShot 鐗规畩鍛戒护锛堝 12=淇濆瓨璁剧疆锛夛紝0=涓嶅彂閫?|
| 16 | u16 | reserved0 | 淇濈暀 |
| 18 | u32 | cycle_counter | 涓荤珯鍐欒鏁板櫒锛堢敤浜庨€氫俊鐪嬮棬鐙楋級 |

### TxPDO锛堜粠绔?鈫?涓荤珯锛?x1A00锛?8 瀛楄妭锛?
| 鍋忕Щ | 绫诲瀷 | 鍚嶇О | 璇存槑 |
|---|---|---|---|
| 0 | u16 | status_word | bit0 ECAT OP锛宐it1 杈撳嚭浣胯兘锛宐it2 鏁呴殰锛宐it3/4 鐢垫満1/2 閬ユ祴鏈夋晥锛宐it5/6 鑸垫満1/2 鍦ㄧ嚎锛宐it7 ADC 鍛婅 |
| 2 | u16 | fault_code | 鏁呴殰浣嶏紙瑙?`ecat_pdo.h` 鐨?app_fault_t锛?|
| 4 | u32[2] | esc_erpm | 鍙屽悜 DShot 鍥炰紶鐢佃浆閫燂紙eRPM 鍘熷鍊硷紝0=鍋滆浆锛?|
| 12 | u16[2] | esc_tlm_period_us | 閬ユ祴鍛ㄦ湡鍘熷鍊?|
| 16 | i16[2] | servo_pos_d10 | 鑸垫満瀹為檯瑙掑害 0.1掳 |
| 20 | i16[2] | servo_speed_rpm | 鑸垫満閫熷害 |
| 24 | i16[2] | servo_current_ma | 鑸垫満鐢垫祦锛堢敱鎵煩鎹㈢畻鐨勪及绠楀€硷紝瑙佽埖鏈烘枃妗ｏ級 |
| 28 | u8[2] | servo_temp_c | 鑸垫満娓╁害 |
| 30 | u8[2] | servo_error | 鑸垫満閿欒鐮侊紙S288 MError 浣?8 浣嶏紝瀹屾暣鍊艰涓婁綅鏈猴級 |
| 32 | u16 | sys_voltage_dv | 绯荤粺鐢靛帇 0.1V |
| 34 | i16 | sys_current_ca | 绯荤粺鐢垫祦 0.01A |
| 36 | u16[2] | esc_current_ma | 鐢佃皟 1/2 鐢垫祦 mA |
| 40 | u16 | loop_counter | 鎺у埗寰幆璁℃暟 |
| 42 | u16 | led_status | 鐘舵€佺伅褰撳墠鐘舵€佺爜 |
| 44 | u16 | ecat_link_status | bit0/1 绔彛閾捐矾锛宐it4-7 AL 鐘舵€?|
| 46 | u16 | reserved0 | 淇濈暀 |

鏈烘杞€熸崲绠楋細`鏈烘 RPM = eRPM 脳 2 / 鏋佸鏁癭锛堟瀬瀵规暟榛樿 7锛屽彲閫氳繃鍙傛暟 `PARAM_DSHOT_POLE_PAIRS` 淇敼锛夈€?
---

## 6. 涓婁綅鏈猴紙PC 璋冨弬宸ュ叿锛?
浣嶇疆锛歚user_template/tools/ecat_drone_host`锛岄缂栬瘧 `build/ecat_drone_host.exe`锛堜篃鍙敤 `build.cmd` 閲嶆柊缂栬瘧锛屼粎闇€ Windows 鑷甫 `csc.exe`锛夈€?
鍔熻兘锛氳繛鎺?閬ユ祴/鍙傛暟/鏃犲埛鐢垫満娴嬭瘯/鎬荤嚎鑸垫満娴嬭瘯/AM32 鐢佃皟閰嶇疆/EtherCAT 鐘舵€?鏃ュ織銆?鍗忚锛歎SB CDC 铏氭嫙涓插彛锛屼簩杩涘埗甯э紙CRC16-CCITT锛夛紝璇﹁ `inc/host_proto.h` 涓?`docs/USB_PROTOCOL.md`銆?
---

## 7. 鐘舵€佺伅鍚箟

| 棰滆壊/鍔ㄧ敾 | 鐘舵€佺爜 | 鍚箟 |
|---|---|---|
| 鐧借壊鍛煎惛 | BOOT | 涓婄數鍚姩 |
| 钃濊壊闂儊 | SELF_TEST | 澶栬鑷 |
| 榛勮壊鎱㈤棯 | ECAT_WAIT | 绛夊緟涓荤珯 / EEPROM 鍒濆鍖?|
| 榛勮壊鍙岄棯 | ECAT_PREOP | EtherCAT PRE-OP |
| 闈掕壊闂儊 | ECAT_SAFEOP | EtherCAT SAFE-OP |
| 缁胯壊鎱㈠懠鍚?| OP_IDLE | EtherCAT OP锛屼絾涓荤珯鏈娇鑳借緭鍑?|
| 缁胯壊甯镐寒 | RUN | 杩愯锛堣緭鍑轰娇鑳斤級 |
| 绾㈣壊闂儊 | FAULT | 鏁呴殰锛堣鏁呴殰鐮侊級 |
| 钃濊壊鑴夊啿 | USB_ACTIVE | 涓婁綅鏈哄凡杩炴帴 |

---

## 8. Bring-up 寤鸿椤哄簭

1. 鍙儳褰曞浐浠躲€佹帴 UART0锛?15200锛夛紝纭鍚姩鏃ュ織銆佹椂閽熼鐜囥€佹澘绾у垵濮嬪寲鎴愬姛锛?2. 鐢ㄤ笂浣嶆満杩炴帴 USB锛岀‘璁?PING/鐗堟湰/鍙傛暟璇诲啓/鐘舵€佺伅鎺у埗姝ｅ父锛?3. 鍗曠嫭娴嬭瘯鐘舵€佺伅锛堢孩/缁?钃?鐧斤級锛?4. 娴嬭瘯 ADC锛氫竾鐢ㄨ〃瀵规瘮绯荤粺鐢靛帇璇绘暟锛屾爣瀹?`PARAM_ADC_SYS_V_*` 绛夊弬鏁帮紱
5. 绀烘尝鍣ㄧ湅 PF02/PF03 DShot 娉㈠舰锛坕dle 楂樸€?6 bit 甯с€佸弽鐩?CRC锛夛紝鍐嶆帴 AM32 鐢佃皟娴嬭瘯杞悜/娌归棬/鍥炰紶 eRPM锛?6. 鐢ㄤ笂浣嶆満 AM32 椤甸潰璇诲啓鐢佃皟鍙傛暟锛堥渶鐢垫満鍋滆浆銆佹湭杩?OP锛夛紱
7. 娴嬭瘯鎬荤嚎鑸垫満锛堝厛 1 涓埖鏈猴紝纭 6 Mbps 娉㈠舰鍜屾柟鍚戝垏鎹㈡椂搴忥紝鍐嶄笂涓や釜锛夛紱
8. 鏈€鍚庢帴鍏?EtherCAT 涓荤珯锛氬厛鐢?`ecat_io` 椋庢牸楠岃瘉 EEPROM 鍒濆鍖栨棩蹇楋紝鍐嶈繘 OP 娴?PDO銆?
---

## 9. 宸茬煡闄愬埗涓庢敞鎰忎簨椤?
1. **SSC 鍗忚鏍堟湭鍖呭惈**锛氫笉鐢熸垚 SSC Tool 浠ｇ爜鏃讹紝EtherCAT 涓嶈兘鐪熸閫氫俊锛堜絾鍏朵粬鍔熻兘鍏ㄩ儴鍙敤锛夈€?2. **鏈湪鐪熷疄纭欢涓婇獙璇?*锛氭湰浠撳簱涓鸿蒋浠朵氦浠橈紝DShot/鑸垫満/ADC/AM32 鏃跺簭闇€鎸夌 8 鑺傚湪纭欢涓婇獙璇佷笌寰皟銆?3. **HPM5E3Y SoC 鏀寔**锛歨pm_sdk v1.13.0 鏈彁渚?HPM5E3Y 鐩綍鐨勬瀯寤烘枃浠讹紙`system.c`/閾炬帴鑴氭湰绛夛級锛屾澘绾у寘浣跨敤 `soc: HPM5E31`锛堜袱鑰呭ご鏂囦欢閫愬瓧鑺備竴鑷达級銆傚闇€涓ユ牸浣跨敤 HPM5E3Y锛岃繍琛?`scripts/add_hpm5e3y_soc_support.ps1` 骞舵妸鏉跨骇 yaml 鐨?`soc:` 鏀逛负 `HPM5E3Y`銆?4. **鍐呴儴 MII PHY**锛歅A16鈥揚A29 鐨勬ā鎷熷紩鑴氥€丳W16/PW17锛圥HY LDO锛夈€丳V12/PW12锛圥HY 澶嶄綅锛夐厤缃緷鎹€奌PM5E00 鐗囧唴 MII PHY 浣跨敤鎸囧崡 v1.1銆嬶紱PHY MDIO 鍦板潃娌跨敤 EVK 绾﹀畾锛?/2锛夛紝鑻ュ疄鏉?strap 涓嶅悓闇€璋冩暣 `board.h` 鐨?`BOARD_ECAT_PHY_*`銆?5. **PD00 澶嶇敤**锛歅D00 鍘熶负 MCAN0_TXD锛屾湰鏉跨敤浣?UART3 鑸垫満鏂瑰悜鍒囨崲锛孧CAN0 涓嶅彲鍚屾椂浣跨敤銆?6. **鑸垫満鍗忚**锛歋288 鍗忚鏃犲瘎瀛樺櫒璇诲啓/鏀?ID 鍛戒护锛岀浉鍏充笂浣嶆満鍔熻兘鏄剧ず涓衡€滀笉鏀寔鈥濓紱鍗忚缁嗚妭瑙?`docs/SERVO_S288.md`銆?
