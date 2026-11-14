# Ecat_Drone_Vector_Powertrain 鈥?浜や粯璇存槑

鏈洰褰曞寘鍚竴濂楀畬鏁寸殑 **HPM5E3Y EtherCAT 鏃犱汉鏈虹煝閲忓姩鍔涙€绘垚浠庣珯** 杞欢浜や粯锛?鏉跨骇鏀寔鍖咃紙BSP锛夈€佸簲鐢ㄥ伐绋嬨€丳C 涓婁綅鏈恒€佽璁℃枃妗ｄ笌楠岃瘉鑴氭湰銆?
> 鑺墖锛?*HPM5E3YIGN1**锛堝唴缃?1MB Flash + 2脳 鐗囧唴 MII PHY锛?> SDK锛歨pm_sdk v1.13.0锛坄sdk_env_v1.13.0/hpm_sdk`锛?> 鏉跨骇寮曡剼鏉ユ簮锛歚pin_mux/pinmux.c`锛堝凡鎸夈€奌PM5E00 鐗囧唴 MII PHY 浣跨敤鎸囧崡銆嬩慨姝ｅ苟鍥哄寲鍒版澘绾у寘锛?
---

## 1. 浜や粯鍐呭锛堝叏閮ㄤ綅浜?sdk_env_v1.13.0\user_template锛?
| 璺緞 | 鍐呭 |
|---|---|
| `user_template\boards\ecat_drone_5e3y\` | **鏉跨骇鏀寔鍖?*锛歜oard.h/.c銆乧lock.c/h銆乸inmux.c/h銆亂aml銆丱penOCD cfg銆丆MakeLists銆丷EADME_zh.rst |
| `user_template\projects\ecat_drone_powertrain\` | **EtherCAT 浠庣珯宸ョ▼**锛欵therCAT 鍗忚鏍堣兌姘淬€丼SC 閰嶇疆鏂囦欢/ESI/绂荤嚎缂栬瘧妗┿€丏Shot銆佽埖鏈恒€丩ED銆丄DC銆乁SB銆佸弬鏁扮郴缁熴€佷笂浣嶆満鍗忚 |
| `user_template\tools\ecat_drone_host\` | **PC 涓婁綅鏈?*锛圕# WinForms锛屽厤瀹夎銆佸厤渚濊禆锛宍csc.exe` 缂栬瘧锛? 婧愮爜 + 鑷 |
| `user_template\scripts\` | `build.cmd`锛堜竴閿瀯寤猴級銆乣build.ps1`銆乣check_syntax.py`銆乣add_hpm5e3y_soc_support.ps1` |
| `user_template\README_zh.md` | user_template 浣跨敤璇存槑 |
| `user_template\projects\ecat_drone_powertrain\README_zh.md` | 宸ョ▼璇存槑锛堟瀯寤?鐑у綍/PDO/涓婁綅鏈?bring-up锛?|
| `user_template\projects\ecat_drone_powertrain\docs\` | 鏋舵瀯銆佸紩鑴氳〃銆丳DO銆丼SC銆佽埖鏈哄崗璁€丏Shot/AM32銆乁SB 鍗忚銆佷笂浣嶆満绛夋枃妗?|
| `.work\`锛堝伐浣滅洰褰曪紝闈炰氦浠橈級 | PDF 鎻愬彇宸ュ叿涓庡叏鏂囥€乣FACTS.md` 纭欢浜嬪疄鎽樿銆丏Shot/AM32 鍙傝€冭祫鏂欍€佹瀯寤虹洰褰?|

## 2. 蹇€熷紑濮?
```bat
:: 1) 鏋勫缓鍥轰欢
cd /d D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\scripts
build.cmd

:: 2) 鏋勫缓涓婁綅鏈?cd /d D:\Ecat_Drone_Vector_Powertrain\sdk_env_v1.13.0\user_template\tools\ecat_drone_host
build.cmd
```

鐑у綍銆丒therCAT 涓荤珯閰嶇疆锛圱winCAT + SSC Tool 鐢熸垚鍗忚鏍堬級銆乥ring-up 姝ラ瑙佸伐绋?README銆?
## 3. 鍏抽敭璁捐鍐崇瓥锛堣鍏堥槄璇伙級

1. **EtherCAT 鍗忚鏍堟巿鏉?*锛歨pm_sdk 涓嶅惈 Beckhoff SSC 鍗忚鏍堟簮鐮併€傚伐绋嬮粯璁ょ敤 `SSC/StubSrc`
   鐨?API 妗╀繚璇佸彲缂栬瘧/鍙摼鎺?鍙繍琛岋紝**鐪熸 EtherCAT 閫氫俊闇€瑕佹寜 `SSC/README.md` 鐢?SSC Tool
   鐢熸垚涓€娆″崗璁爤浠ｇ爜**锛堢敓鎴愬悗鏋勫缓绯荤粺鑷姩鍒囨崲锛屾棤闇€鏀逛唬鐮侊級銆?2. **鐗囧唴 MII PHY**锛氭湰鏉跨殑 ESC0 浣跨敤 HPM5E3Y 鍐呯疆鐨勪袱璺?MII PHY锛圥V/PW 鍐呴儴 IO锛夛紝
   PV12/PW12 涓?PHY 澶嶄綅锛孭A16鈥揚A29 涓?PHY 妯℃嫙淇″彿锛孭A25/PA28 涓洪摼璺姸鎬佽緭鍏ャ€?   ECAT HAL 宸叉寜銆婄墖鍐?MII PHY 浣跨敤鎸囧崡銆嬮€傞厤锛孭HY 鍦板潃娌跨敤 EVK 绾﹀畾锛?/2锛夈€?3. **HPM5E3Y SoC 鏋勫缓**锛歨pm_sdk v1.13.0 鏈彁渚?HPM5E3Y 鐩綍鐨勬瀯寤烘枃浠讹紙system.c/閾炬帴鑴氭湰绛夛級锛?   鏉跨骇鍖呬娇鐢?`soc: HPM5E31`锛堝悓 IP锛屽ご鏂囦欢閫愬瓧鑺備竴鑷达級锛屽櫒浠跺悕鏍囨敞涓?HPM5E3YIGN1銆?   濡傞渶涓ユ牸浣跨敤 HPM5E3Y 鐩綍锛岃繍琛?`scripts/add_hpm5e3y_soc_support.ps1` 骞舵敼 yaml銆?4. **鑸垫満鍗忚**锛氬畤鏍?J288/S288 涓?6 Mbps 鍗曟€荤嚎鍗婂弻宸ワ紝20B 鎺у埗甯?/ 26B 鍙嶉甯э紝CRC32锛?   鏃犲瘎瀛樺櫒/鏀?ID 鎸囦护銆傛柟鍚戝垏鎹㈢敱 PC16/PD00 鎺у埗锛屾湁鏁堢數骞冲彲鍙傛暟閰嶇疆銆?5. **PD00 澶嶇敤**锛歅D00 鍘熶负 MCAN0_TXD锛屾湰鏉跨敤浣?UART3 鑸垫満鏂瑰悜鍒囨崲锛孧CAN0 涓嶅彲鐢ㄣ€?6. **鍙屽悜 DShot**锛歅WMV2 杈撳嚭锛堝弽鐩?idle 楂樸€佸弽鐩?CRC锛? GPTMR1 杈规部鎹曡幏鍥炰紶锛?   瑙ｇ爜閾撅細21bit 宸垎绾垮簭 鈫?20bit GCR 鈫?16bit锛?2bit eRPM+4bit 鍙嶇浉 CRC锛夆啋 eRPM銆?
## 4. 楠岃瘉鐘舵€?
| 椤圭洰 | 鐘舵€?|
|---|---|
| 鍥轰欢缂栬瘧锛堝叏閮ㄦā鍧楋紝`-Wall -Wundef`锛?| 鉁?閫氳繃锛? warning / 0 error锛坄scripts\build.cmd clean` 鍏ㄦ柊鏋勫缓锛?|
| 鍥轰欢閾炬帴锛圫SC 妗?+ 瀹屾暣搴旂敤锛?| 鉁?閫氳繃锛歚output/ecat_drone_powertrain.elf`锛孎LASH 154,876 B锛堝崰 1MB 鐨?14.77%锛岃繙浣庝簬 0x7E000=504KB 涓婇檺锛夛紝AXI_SRAM 42,560 B / 192KB |
| 涓婁綅鏈虹紪璇?+ 鍗忚鑷 | 鉁?閫氳繃锛歚build\ecat_drone_host.exe`锛宍--selftest` 43/43 PASS |
| 鍙屽悜 DShot 鏀跺彂绠楁硶浠跨湡 | 鉁?閫氳繃锛?096/4096 閬ユ祴杞借嵎瑙ｇ爜锛屄?% ESC 鏃堕挓+鎶栧姩涓?0 閿欒瑙ｇ爜锛屾崯鍧忓抚鍏ㄩ儴鎷掓敹 |
| 鑸垫満甯ф瀯閫?瑙ｆ瀽鍗曞厓娴嬭瘯 | 鉁?閫氳繃锛?8/58锛圕RC32 涓庡巶鍟嗗疄鐜?600 闅忔満甯ф瘮瀵逛竴鑷淬€?0B/26B 甯у瓧鑺傜骇鏍￠獙锛?|
| USB 鍗忚涓€鑷存€ф祴璇?| 鉁?閫氳繃锛?18/218 妫€鏌ワ紙甯?CRC/闀垮害/鍛戒护/鍙傛暟瑕嗙洊锛?|
| EtherCAT PDO/CoE 涓€鑷存€?| 鉁?闈欐€佹牎楠岄€氳繃锛?2B/48B銆?0 涓?CoE 瀵硅薄绫诲瀷涓庡弬鏁拌〃涓€鑷达級锛涚湡瀹炲崗璁爤闇€ SSC Tool 鐢熸垚鍚庤仈璋?|
| 纭欢瀹炴満楠岃瘉 | 鈿狅笍 **鏈繘琛?*锛堟棤瀹炵墿鏉匡級锛沚ring-up 姝ラ瑙佸伐绋?README 绗?8 鑺傘€佸悇妯″潡 docs 鐨勨€滅‖浠堕獙璇佲€濈珷鑺?|

璇︾粏鐨勯獙璇佽瘉鎹紙鍛戒护銆佽緭鍑恒€佷唬鐮佸鏌ュ彂鐜帮級瑙?`user_template\projects\ecat_drone_powertrain\docs\VERIFICATION.md`銆?
## 5. 鍚庣画宸ヤ綔锛堟寜浼樺厛绾э級

1. 鐢?Beckhoff SSC Tool 鐢熸垚鍗忚鏍?鈫?瀹炴祴 EtherCAT 閫氫俊涓?TwinCAT PDO 鏄犲皠锛?2. 纭欢瀹炴祴骞舵爣瀹?ADC 鍙傛暟锛堢數鍘?鐢垫祦姣斾緥锛夈€佺‘璁?PHY MDIO 鍦板潃涓庨摼璺瀬鎬э紱
3. 纭欢瀹炴祴鍙屽悜 DShot 鏃跺簭锛堢ず娉㈠櫒锛変笌 AM32 鍥炰紶瑙ｇ爜锛屽繀瑕佹椂寰皟 `PARAM_DSHOT_*`锛?4. 纭欢瀹炴祴 S288 6 Mbps 娉㈠舰銆佹柟鍚戝垏鎹㈡椂搴忎笌 CRC 瑕嗙洊鑼冨洿锛?5. 涓婁綅鏈烘牴鎹疄娴嬭ˉ鍏?AM32 EEPROM 鍙傛暟瑙ｇ爜锛堝綋鍓嶆寜 AM32 瀹樻柟瀛楁琛ㄥ疄鐜帮紝鍑哄巶鍊奸渶瀹炴満璇诲彇锛夈€?
