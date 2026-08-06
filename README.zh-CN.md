# Daybreak DamageTracker

[English](README.md) · [Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3776927292) · [MIT 许可证](LICENSE)

Daybreak DamageTracker 是一个面向 Terraria 1.4.4 / tModLoader 多人游戏的服务端统一控制型 Boss 伤害结算模组。它统计服务端实际接受的有效伤害、展示团队排名，并把每位玩家的详细伤害来源限制为仅本人可见。

## 主要功能

- 从发现 Boss 开始跟踪战斗，支持胜利、失败、逃离和管理员手动结算。
- 每个 Boss 都有独立的团队总伤害、本体伤害、战斗时长、玩家排名、占比和本人来源树。
- 同时多 Boss 时，每只 Boss 一结束就单独结算，不需要等待仍在场的其他 Boss。
- 按武器、饰品、武器本体和弹幕类型整理本人的伤害来源。
- 来源和弹幕可以原地展开或折叠；长面板会限制在当前视口内并支持滚轮浏览。
- 每个客户端可保留最近 1–10 只 Boss 的记录：最新一场用 `/dt`，之后按需使用 `/dt1` 至 `/dt9`。
- 每个客户端可把自动弹出面板的持续时间设为 1–120 秒；`/dt` 手动打开的面板会一直保留到主动关闭。
- 面板已打开时，后续结算会追加到同一滚动面板下方。
- 可选集成 Boss Checklist，并为普通 Boss 提供 `npc.boss` 回退识别。
- 服务端可为特殊转阶段 Boss 配置生命周期覆盖项。
- 团灭或脱战后会拆分无关 Boss，同时保留真正同时出现的多个 Boss 和已配置的车轮战。
- 一只击杀、一只脱战时会分别及时弹出“胜利”和“逃离”，不会互相阻塞。
- 玩家断线重连后，公开排行榜会合并为一行，但私有来源仍只按精确连接投递。
- 支持的 Boss 名称会按每个客户端选择的语言独立解析。

## 隐私边界

公开结算只包含团队排名。详细伤害来源由玩家自己的客户端记录，不会写入公开结算包或历史包。服务端生成弹幕的来源信息也只会发送给该弹幕的拥有者。

展示项和结算接收范围由服务端统一控制。客户端偏好只控制自动弹出、自动面板持续 1–120 秒、保留 1–10 场本地记录以及面板深色底色透明度；它们不能打开服务端禁用的项目，也不能扩大数据权限。

## 安装

可以直接订阅 [Steam 创意工坊条目](https://steamcommunity.com/sharedfiles/filedetails/?id=3776927292)，也可以手动把同一个 `DaybreakDamageTracker.tmod` 安装到专用服务器和所有客户端。网络数据格式可能随版本调整，因此服务端与客户端不能混用不同版本。

首次加载后，服务端会创建：

```text
<tModLoader SavePath>/ModConfigs/DaybreakDamageTracker.Server.json
```

全部配置项和特殊 Boss 适配方法见 [SERVER_CONFIG.md](SERVER_CONFIG.md)。

## 命令

客户端聊天命令：

```text
/dt
/dt1
/dt2 ... /dt9
/dt list
/dt chat
/dt close
```

历史指令使用相对位置：`/dt` 是最新一场，`/dt1` 是上一场，之后依次类推；设置保留几场，就有几条记录可用。新结算会让旧记录顺延，因此聊天栏会用一条极简消息重列当前的“Boss 名称｜结算状态｜查看指令”，不再添加 `#0` 一类编号。

专用服务器控制台命令：

```text
dtserver reload
dtserver status
dtserver finish victory|defeat|escaped|manual
```

## 从源码构建

需要 .NET 8 SDK 和 tModLoader 1.4.4。最简单的方法是把仓库克隆到 tModLoader 的 `ModSources` 目录；项目会自动使用父目录中的 `tModLoader.targets`。

如果源码位于其他位置，请显式传入 tModLoader 的构建目标文件：

```powershell
dotnet build -c Release -p:TModLoaderTargets="D:\Steam\steamapps\common\tModLoader\tMLMod.targets"
```

只验证编译而不打包或安装 `.tmod` 时，可增加 `-p:BuildMod=false`。

## 已知边界

Terraria 1.4.4 仍会接受客户端提交的命中伤害值。本模组按服务端 `NPC.StrikeNPC` 实际返回的生命损失归属伤害，但它不是反作弊系统。

普通物品和弹幕命中属于支持范围。持续伤害以及其他模组直接修改 `npc.life` 的伤害没有通用、可信的玩家和来源信息，因此本模组不会猜测归属。具有无 NPC 演出阶段或非常规最终形态的 Boss 可能需要服务端覆盖配置；默认关闭通用的无 NPC 超时，以免在转阶段时提前结算。

不同轮次使用不同 Boss Checklist key 的车轮战，可以通过相同的 `EncounterGroupKey` 归为一场。若该车轮战允许全员阵亡后复活继续，还可启用 `ContinueAfterPartyWipe`，并应配置可靠的最终 NPC。

## 参与贡献

欢迎提交可复现的问题和范围明确的拉取请求。开发前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)，多人联机和 Boss 生命周期检查见 [TESTING.md](TESTING.md)。

## 许可证与鸣谢

源代码采用 [MIT 许可证](LICENSE)。实现过程中参考的公开项目和官方示例列在 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。模组图标和创意工坊图片经项目所有者许可随项目提供，不属于 MIT 源代码许可证的授权范围。
