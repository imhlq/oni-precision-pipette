# 精准移气移液 v0.2 · 《缺氧》Mod

同一面板可分别选择地图格或已建成的原版管道为起点和目标；支持地图格→地图格、管道→管道、地图格↔管道。只搬运单批样本，不改变管网连接，也不创建连续泵送任务。

## 安装

1. 解压 `OniPrecision-install.zip`，把其中的 **`OniPrecision` 文件夹**放进《缺氧》的 `mods/Local`（若已有 `mods/local`，使用已有目录）。文件夹内应直接有 `OniPrecision.dll`、`mod.yaml` 和 `mod_info.yaml`，不要多套一层。
2. Windows 通常为 `%USERPROFILE%\Documents\Klei\OxygenNotIncluded\mods\Local`。如果文档目录被重定向，或在 macOS／Linux 玩，从游戏主菜单 **选项 → 反馈 → 浏览存档文件** 打开用户数据目录，找到与存档目录相邻的 `mods`。
3. 在主菜单「模组」中启用，按提示重启。首次请用**独立测试存档**。与其他滴管 Mod 分开测试，避免误用旧按钮。

覆盖升级时，先退出游戏并备份存档，再把本版文件替换到原来的 OniPrecision 文件夹。不要同时启用两份。

## 操作

起点和目标各有「地图 / 管道」选择；气体模式只认通气管，液体模式只认液体管道。建议打开游戏原版对应视图来选管格。隔着墙的已建成管道也可选。运行中的气包会按原版规则继续流动，首次请用隔离段试。

- 点殖民地左上角的「移气 / 移液」，选「移气管」或「移液管」，设置本次剂量及单位。用「选起点」「选目标」分别点选地图格，解除暂停后点「执行一次」。气体支持 10 µg 到 1 kg，液体支持 1 mg 到 10 kg；若游戏的质量精度无法可靠表示该次增减，工具会拒绝而不是取近似的大剂量。
- 默认保持原温。研究温控科技（内部 ID `TemperatureModulation`）后，可只为**本次转移**设定温度，单次变温最多 20 g。会导致相变的设定不接受。它不会持续调节房间、管道或食物温度；当目标中已有同类流体时，设定值针对移入的部分，不表示整格变成该温度。变温每次最多 20 g，禁止设到元素的相变温度。
- 源和目标相同、目标未探索、为固体或门、已有异种元素时拒绝执行。**不主动覆盖，也不改投邻格**。如果取样后目标改变或模拟器拒绝投放，已经取出的样本会暂存在工具，允许另选目标重试或按原温退回原格。等待模拟器确认期间先不要退出或换档；正常投放后，游戏仍会模拟流动、扩散和换热。
- 微克保鲜仍需自己处理保鲜格的隔离、导热管预冷与换热条件；这个 Mod 只负责搬运指定量的流体，可选改变这份流体的温度，并不修改腐败判定。

目标管道必须为空或已装同种元素，并有足够容量，按原版上限气管 1 kg、液管 10 kg 做预检；只接受引擎实际取出或收下的质量。若引擎只接收一部分，余量留在缓冲。两种端点即使位于同一地图坐标，也是不同的空间。

## 上机验收清单

这是待上机验证的候选版。通过编译或离线测试，不代表已在《缺氧》中运行、验证对应 DLC 的兼容性，或能保证永久保鲜。

1. **常温气体：**从较轻的含氢格（例如 1–10 g）向隔离的真空格移入约 50 µg 氢气（0.00005 g）。引擎以单精度记录质量，太重的源格或目标格无法准确扣加微小剂量时会被拒绝。分别检查源格减少量和目标增加量；不要只看剂量输入框。解暂停、存档／读档后再次查看实际落点和质量。
2. **保鲜：**解锁变温后，向预冷、隔离的保鲜格移入 50 µg、−25 ℃ 的氢气。检查实际落点、质量和温度；投放食物并解暂停后检查「深度冷冻」「无菌环境」及新鲜度。结果不符时，分开检查是否按量送达和保鲜格是否维持低温。
3. **液锁：**取一份可辨识的液量（例如 30 g 石油），尝试向兼容的空格排放。检查实际落点，再让游戏按原版规则自然流动。注意液体和气体对目标格均有元素兼容要求。
4. **拒绝：**分别选异种气体、异种液体、固体、未探索格及源格作为目标。应拒绝且不转移；另测同元素合并，以及选择目标后、执行前目标状态改变时的反应。
5. **解锁与重复：**科技未解锁时应不可用变温；确认一次只执行一批，读档不重复。保留独立保存点，有异常先禁用并可恢复测试前的备份。

6. **管道回归：**先用隔离段测试 0.1 g 氢气的气管→空气管、30 g 液体的液管→空液管，再测管道→地图保鲜格、地图→管道及同坐标管内外。分别测试满管、异种气包、选中后拆管、等待地图取样时目标状态改变；用两端的读数核对实际移量和缓冲余量。

## 从源码构建

需要 .NET 8 SDK 或更新版本，以及合法安装的游戏的 `Managed` 文件夹（包含 `Assembly-CSharp.dll`）。Steam 库右键游戏 → 管理 → 浏览本地文件，Windows/Linux 通常在 `OxygenNotIncluded_Data/Managed`；macOS 在游戏应用包内查找 `Assembly-CSharp.dll` 的所在目录。构建引用这些程序集，不将它们打包进 Mod。

Windows PowerShell：

```powershell
.\build.ps1 -ManagedDir 'D:\SteamLibrary\steamapps\common\OxygenNotIncluded\OxygenNotIncluded_Data\Managed'
```

macOS／Linux：

```bash
bash ./build.sh '/路径/OxygenNotIncluded_Data/Managed'
```

也可设置 `ONI_MANAGED_DIR`；SDK 不在 PATH 时可设置 `DOTNET_EXE`。安装 ZIP 默认输出为 `dist/OniPrecision-install.zip`，只含发布 DLL、两个模组清单和本说明。源码和测试另存。离线规则测试可独立运行：

```sh
dotnet run --project tests/TransferPlan.Tests.csproj -c Release
```

## 设计参考

- [移液管 Pipette](https://steamcommunity.com/sharedfiles/filedetails/?id=2630036712)：鼠标定量移液的公开说明。
- [移气管](https://steamcommunity.com/sharedfiles/filedetails/?id=2873888171)：微克量级交互及覆盖原格的风险；10 µg 下限是该 Mod 的公开标称。
- [Precision Pipette Plus](https://steamcommunity.com/sharedfiles/filedetails/?id=3779620887)：气、液共用游戏内调量交互。本 Mod 不采用其向邻格寻找位置的行为。
- [Peter Han 的 ONIMods](https://github.com/peterhaneve/ONIMods) 和 [Sgt. Imalas 的 ONI Mods](https://github.com/Sgt-Imalas/Sgt_Imalas-Oni-Mods)：公开 ONI Mod 接口用法参考。安装包不包含参考程序集或其他 Mod 的二进制。
