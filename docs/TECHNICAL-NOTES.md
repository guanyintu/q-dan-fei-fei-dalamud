# 技术核查记录

核查日期：2026-11-12（Asia/Shanghai）。源码只用于检查，不引入第三方插件代码。

## 实际构建基线

- 官方 [SamplePlugin 工程](https://github.com/goatcorp/SamplePlugin/blob/master/SamplePlugin/SamplePlugin.csproj)：Dalamud.NET.Sdk 15.0.0。
- 0.1.1 的实际构建运行时：`%APPDATA%/XIVLauncherCN/addon/Hooks/15.0.4.0`，Dalamud commit `3dcf078426f9d74e8036d163715949c8f3446556`；runtimeconfig 为 .NET 10。
- 本机运行时及安装的 `ffxivgame.ver` 均为 `2026.11.03.0000.0000`。插件 manifest 从构建引用目录的 version.json 自动读取 SupportedGameVer；构建脚本检查目标一致性。
- 当前 FFXIVClientStructs.dll 文件版本 `7.57.0.9568`，SHA-256 `C42EE1515FF546909A7E739F25C8E7BDEFAEDE913BD5D8B42ABA51C2BB832B27`；已用此实际 DLL 重建和执行原生夹具测试，验证 Scale、DrawObject、ObjectIndex 偏移和调用包装。当前源码提交的网络获取失败，未声称完成新旧源码逐行比对。
- 初次核查的源代码基线为 Dalamud `ecd08fafd6092c51a011b23fe322442baaecf5ce` 及其 [FFXIVClientStructs submodule](https://github.com/goatcorp/Dalamud/tree/ecd08fafd6092c51a011b23fe322442baaecf5ce/lib/FFXIVClientStructs) `cff64c4ae999f8b2b9dae6b13da17d84b1d391a3`。下文固定源码链接保留作结构说明；当前交付使用 15.0.4.0 DLL，不继续引用已卸除的 15.0.3.8。

## 真正的三轴入口

[Scene.Object](https://github.com/aers/FFXIVClientStructs/blob/cff64c4ae999f8b2b9dae6b13da17d84b1d391a3/FFXIVClientStructs/FFXIV/Client/Graphics/Scene/Object.cs) 的 Scale 位于 `0x70`，类型是三分量 Vector3；Position 在 `0x50`，不需要移动 GameObject 才能改比例。ObjectFlags 的 IsTransformChanged 必须设置。

[DrawObject.NotifyTransformChanged](https://github.com/aers/FFXIVClientStructs/blob/cff64c4ae999f8b2b9dae6b13da17d84b1d391a3/FFXIVClientStructs/FFXIV/Client/Graphics/Scene/DrawObject.cs) 包装了变换通知；LoadState 为 3 时还会调用 UpdateTransforms(false)。实现直接调用这一现有包装。

[GameObject](https://github.com/aers/FFXIVClientStructs/blob/cff64c4ae999f8b2b9dae6b13da17d84b1d391a3/FFXIVClientStructs/FFXIV/Client/Game/Object/GameObject.cs) 的 Scale 在 `0xC4` 且为 float；DrawObject 在 `0x100`。插件仅修改 DrawObject 继承的三轴 Scale；不会调用 SetPosition 或写入 GameObject.Scale。

FF14 世界坐标以 Y 为高度、XZ 为地面平面；模型缩放仍沿模型根的局部轴，角色直立时 Y 为竖直方向，倾斜／附着模型不能假设与世界竖直完全重合。实机排障确认普通角色的 ParentObject 也非空，类型为普通 Scene.Object 容器（ObjectType.Object），0.1.2 已允许这种正常父节点，只跳过附着到角色等非普通容器的模型。没有通过高度偏移或镜头移动伪造压缩效果。

[CharacterBase](https://github.com/aers/FFXIVClientStructs/blob/cff64c4ae999f8b2b9dae6b13da17d84b1d391a3/FFXIVClientStructs/FFXIV/Client/Graphics/Scene/CharacterBase.cs) 是 Human、DemiHuman、Monster、Weapon 的共同渲染基类。仅处理前三类的已加载根对象；未依赖不同骨骼的命名。Scene.Object 的 ChildObject 文档指出人物的子对象可为武器；武器是否完全继承非等比变形只能实机验收，不能由结构定义推断为已通过。

## 更新时序与 Hook 范围

[Dalamud Framework.HandleFrameworkUpdate](https://github.com/goatcorp/Dalamud/blob/ecd08fafd6092c51a011b23fe322442baaecf5ce/Dalamud/Game/Framework.cs) 先 RunFrameworkTick 再调用原游戏 Tick；因此单纯在 IFramework.Update 写 Scale 没有“游戏更新后写入”的保证。

当前方案按经验证的对象虚表获取 GameObject.Update（ClientStructs 定义的虚函数 36），使用 IGameInteropProvider.HookFromAddress，原函数只转发一次；之后重新解析对象槽位，应用根缩放并通知变换。相同入口复用 Hook，最多 16 个；异常关闭效果。仅在 Framework 线程访问对象，不把原生指针送入工作线程。读取的对象地址不跨帧盲目信任；尤其不在原函数返回后直接读取传入的旧指针。

**这是根据已确认字段和更新顺序构建的真实渲染写入实现，但没有游戏验证。** 部分原生代码可能在此回调之后再次覆盖变换、跳过 Update，或对子对象采用不同缩放逻辑。自动化内存夹具只能证明写入与校验，不能证明渲染时序、足底支点、装备武器传播或游戏稳定性。

## 音频

只用 NAudio.Core、NAudio.WinMM、NAudio.Wasapi 2.2.1，合计约 415 KiB；WinMM WaveOutEvent 输出，Windows Media Foundation 解码 MP3，WaveFileReader 读取 WAV。LoopStream 在 EOF 回绕，空流有退出保护。准备设备及解码器后记录共享启动 epoch 并 Play，输出设备缓存仍有大约 100 ms 延迟，不提供 BPM 或采样级同步保证。不调用全局 MediaFoundation Shutdown，以免干扰同进程其他音频使用者。

0.1.5 按用户要求取消额外音频压缩和自动解压。发布包直接携带 MP3；音频路径仍按用户选择、配置默认、随包默认的顺序解析，直接由现有音频服务读取。没有新增编解码器或 NuGet 依赖，也不会覆盖已有用户音乐。

[参考 MOD 页面](https://patch-ali.gamehub.homes/showpatch/320889.html)没有指明 BGM。发布者指定的音乐出处为 [Bilibili BV1VGYR6bECg](https://www.bilibili.com/video/BV1VGYR6bECg/)，并已确认公开分发授权；内置 MP3 可从界面更换。插件本身不集成下载器，不访问视频站点。NAudio 依据其 [MIT 许可证](https://github.com/naudio/NAudio/blob/v2.2.1/license.txt) 使用。


