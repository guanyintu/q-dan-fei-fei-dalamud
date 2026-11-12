# q弹狒狒

会让所有人变得q弹

作者：**someboy**。这是卫月（Dalamud）版本，作用于当前加载的玩家、NPC、怪物／BOSS，并支持循环 BGM。默认关闭效果。

**本项目主要由 Codex 生成。**

[下载最新版本](https://github.com/guanyintu/q-dan-fei-fei-dalamud/releases/latest) · [独立 EXE 版](https://github.com/guanyintu/q-dan-fei-fei-standalone)

**卫月版会更流畅一些，已有卫月建议优先使用本版本。** [独立版](https://github.com/guanyintu/q-dan-fei-fei-standalone/releases/latest) 无需卫月，下载 EXE 即可运行。

**构建已通过；完整模型效果验收仍见测试报告。** 当前版本 **0.1.6** 针对国服 Dalamud **15.0.4.0 / API 15 / .NET 10**，游戏版本 **2026.11.03.0000.0000**。构建时从实际引用的 Dalamud `version.json` 生成 manifest 适用版本，并检查清单与目标运行时一致；不会使用 `any` 跳过版本限制。

0.1.6 将项目名、主文件和界面名称统一为“q弹狒狒”，作者为 someboy。0.1.1 修复版本上限导致的加载失败，0.1.2 修复场景父节点过滤，0.1.3 加入默认音乐，0.1.5 取消额外音频压缩。

## 加载

也可在卫月“自定义插件仓库”中添加以下地址，再到插件安装器搜索“q弹狒狒”：

```text
https://raw.githubusercontent.com/guanyintu/q-dan-fei-fei-dalamud/main/repo.json
```

手动开发插件加载方式：

1. 从 Releases 下载 `q弹狒狒-卫月版-0.1.6.zip`，解压到独立目录，保留三个 `NAudio.*.dll` 及 `Assets` 音乐目录。无需复制 Dalamud 或 FFXIVClientStructs DLL。
2. 通过卫月启动游戏。输入 `/xlsettings`，在“实验性 / Experimental → Dev Plugin Locations”添加解压后的 **q弹狒狒.dll 完整路径**，启用并保存；必要时在 `/xlplugins` 扫描开发插件／重新启动游戏。
3. 输入 `/qbounce`，勾选“Q 弹总开关”。默认强度 **0.4**、周期 **0.45 秒**、所有范围开启且相位同步。
4. 关闭总开关，或输入 `/qbounce off`，撤回本插件的缩放。重新加载插件后仍默认关闭。

自行构建后可添加 `dist/q弹狒狒-卫月版/q弹狒狒.dll` 的绝对路径。不要从 ZIP 内直接加载，也不要只复制主 DLL。旧版 QBounce 用户需先停用旧版，再添加新 DLL；设置目录会随内部名称变更，默认效果仍关闭。

## 音乐与快捷键

- **音乐出处：[Bilibili BV1hXbN6VEKV](https://www.bilibili.com/video/BV1hXbN6VEKV/)**。内置 MP3 约 42 分 54 秒，发布者已确认公开分发授权。插件运行时不连接 Bilibili、不下载音乐。
- 发布包直接携带 `Assets/bgm.mp3`（61,769,971 字节，约 61.8 MB），直接播放，无额外音频压缩或首次加载解压步骤。
- 选曲优先级：界面所选路径 → 配置目录 `bgm.mp3` → 随包的 `Assets/bgm.mp3`。配置默认文件通常为 `%APPDATA%\XIVLauncherCN\pluginConfigs\q弹狒狒\bgm.mp3`；界面显示实际使用路径。
- 可选择其他本地 MP3 或 PCM／IEEE-float WAV，或粘贴路径后点击“应用音乐 / 重置节奏”。清空路径恢复默认。构建工具及视频下载器不随插件运行，也不作为插件依赖。
- 开启效果即播放并循环，关闭或卸载即停止释放；静音与音量实时生效。音乐缺失、解码失败或设备错误不会阻止 Q 弹。
- F6：开关；F7：强度 `0 → 0.2 → 0.4 → 0.6 → 0`；F8：视觉节奏及音乐从头开始。快捷键**默认禁用**，可手动启用。后台、输入文字或带修饰键时忽略；不吞掉游戏原键，因此请移除冲突绑定。
- 过场、切图、未登录及 GPose 自动暂停并撤回缩放、停止音乐；恢复普通场景后，开关仍开启时重新开始节奏。

## 实现与边界

真实写入的是 `Graphics.Scene.Object.Scale` 的 **X/Z 水平、Y 纵向**向量，随后调用 `DrawObject.NotifyTransformChanged()`。纵向 `1 + A*sin(2πt/P)`，水平 `1/sqrt(Y)`，按当前基础缩放逐轴相乘。只改渲染比例，不改 GameObject.Scale、世界位置、速度、碰撞半径、服务器或游戏文件。

IFramework.Update 负责共享 Stopwatch 时钟、恢复与对象管理；由于它在游戏 Tick 之前执行，插件通过 Dalamud 对已验证角色的 **GameObject.Update 虚函数入口**安装少量 Hook，在原生更新后重新核验并写入渲染缩放。没有硬编码游戏函数地址或自制注入器。需要实机确认此时序是否涵盖所有模型的后续动画更新。

当前对象每帧重验；空槽位每帧扫描约 1/8，通常八帧内发现新对象。对象身份包含槽位、地址、实体／模型 ID、DrawObject、Skeleton 和虚表。失效对象直接丢弃，不向旧指针恢复。仅支持已加载 Human、DemiHuman、Monster 根模型；普通 Scene.Object 场景容器父节点允许处理，附着到角色或其他非普通容器的模型安全跳过。BattleNpc 全部归入“怪物 / BOSS”（包括部分友方战斗 NPC／宠物）。

以根模型原点为缩放支点并保持位置不动，通常能保持脚底位置；非足底原点、飞行／骑乘、特殊 BOSS、独立 VFX 和附着武器的外观需要游戏验证。装备／头发共用角色渲染骨架，武器依赖原生附着传播，不逐骨骼修改，也不重复缩放子对象。GPose 当前有意暂停。

按轴比较最后写入值，只撤回未被外部改动的轴，避免累积并尊重其他比例写入。无法完美区分其他插件对同一数据的相同值写入、基于本插件结果继续叠乘，或在两次观测间完全相同身份／地址的销毁重建；遇到同字段争用请关闭其中一个效果。托管异常会自动关闭；原生访问异常或版本不兼容无法靠 C# catch 保证恢复。

**恢复操作：**先 `/qbounce off`，再在 `/xlplugins` 停用。若仍变形，切换区域或重新登录以重建模型；仍异常则停用后重启客户端。无需修复游戏文件。开发日志可用 `/xllog` 查看 QBounce 错误。

## 构建与测试

安装 .NET 10 SDK 和匹配的 API 15 Dalamud，然后在 PowerShell 执行：

```powershell
./build.ps1 -DalamudPath "$env:APPDATA\XIVLauncherCN\addon\Hooks\15.0.4.0"
```

脚本优先使用本工作区 `.tools/dotnet/dotnet.exe`（如存在），其次使用系统 SDK；NuGet 版本已锁定。未指定 DalamudPath 时，优先使用 DALAMUD_HOME，否则自动选择已安装的最新 API 15 运行时。自动构建、检查目标版本、运行测试并打包到 `dist`；可用 `-GameVersionFile` 指定游戏安装目录中的 `ffxivgame.ver`，同时核验实际游戏版本。测试包括无游戏依赖的数学／生命周期、使用本机结构 DLL 的自有原生内存夹具，以及 Windows 音频设备的静音 WAV／MP3 播放测试；后者需要 Windows 音频输出与 Media Foundation 编解码器。

本次 Release 构建 0 warning / 0 error，44 项测试通过、0 失败、0 跳过。结果见 [验收报告](docs/VALIDATION.md)，结构版本与时序依据见 [技术核查](docs/TECHNICAL-NOTES.md)。**编译成功、内存夹具测试通过和音乐设备测试通过均不代表所有游戏内模型效果已验证。**
