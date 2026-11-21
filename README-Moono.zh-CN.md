# 又一款精准滴管（YAPP）

适用目标：Oxygen Not Included U60 正式版（最低游戏构建 763796）和 Moono v2026.11.20.04。**已在 Moono 加载测试存档，并验证快速切换管道视图后显示原生氧气标签。暂停取放、地图与管道双向转移、带样本读档仍待实机验证。** 不需要安装 Moono 才能使用、不依赖 Moono 私有组件，不修改 Moono 本身，也未打包它的任何代码或程序集。独立取液管工具支持在地图方格之间取放液体。

- YAPP 使用游戏叠加画布、TextMeshPro 和游戏字体显示管道选点、剂量和执行预览。收起时停止读取端点；有余量时显示投放和退回操作。
- 禁用的控件即使错误报告点击也不处理；无效的剂量单位会在扣取前拒绝。切换端点类型仍清除原选点，避免误用旧坐标。
- Moono 的管网优化调整重建通知、更新排序；审核版本未直接补丁本模组调用的 AddElement / RemoveElement / GetContents。Moono 的后台保存把保存快照之后的写盘移到后台，不能由此推断自动保存总会检查 CanSave。保留原有交易核对规则，并不绕过游戏保存或更改 Moono 设置。

## 双环境验收

1. 各用独立测试存档，记录标题页游戏构建号、Moono 版本、启用的 DLC 和 Moono Tweaks。原生 U60 跑一遍，再用 Moono 默认设置跑一遍。只装一份 OniPrecision。
2. 确认面板能打开、关闭、切换气／液、地图／管道与 µg/mg/g/kg；无关界面点击、按 Esc、切换殖民地不应取样。
3. 隔离气管向空气管转 50 µg 氢气，隔离液管向空液管转约 30 g。核对源、目标和工具缓冲。满管、异种气体、拆除目标、地图端点都应拒绝；暂停时可提交取放，地图模拟回调可能要恢复运行后才完成。温控科技前后各测一次。
4. 手动、自动和保存并退出分别验证；有已确认余量也要试。用原生 U60 读 Moono 的副本，再用 Moono 读原生 U60 的副本。不要覆盖主档。只在异常或复现差异时逐项 A/B 测 Moono 后台保存和管网优化，不必先关所有优化。

证据：[Moono v2026.11.20.04](https://github.com/tigin-backwards/oni-moono/releases/tag/v2026.11.20.04)、[该版本 IMGUI 控件](https://github.com/tigin-backwards/oni-moono/blob/1043305e44fdf265cb0d16e53ef9659d8e4a7962/src/OniMoono.UnityEngine/IMGUI/GUILayout.cs)、[优化说明](https://github.com/tigin-backwards/oni-moono/blob/1043305e44fdf265cb0d16e53ef9659d8e4a7962/docs/TWEAKS.md)。
