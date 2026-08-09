# 更新日志 / Changelog

重要变更记录如下，版本号与 `build.txt` 中的模组版本一致。

Notable changes are documented here. Versions follow the mod version in `build.txt`.

## 0.1.7

### 简体中文

- 源码和项目文档从 MIT 改用 GPL-3.0-only。此前已经发布的版本继续遵循各自随附的
  许可证；模组图标和创意工坊图片仍不在该许可证的授权范围内。
- 修复普通 Boss 在仍有存活玩家位于附近时未触发 `OnKill` 便消失，导致战斗一直处于
  待结算状态且不弹出 Daybreak 结算面板的问题。
- 显式配置的无 NPC 演出和同组车轮战继续按各自配置等待，不会把每次暂时消失都误判为
  Boss 逃离。
- 根据 `NPC.UpdateNPC_BuffApplyDOTs` 实际造成的权威生命损失统计持续伤害，包括最后一次
  致死扣血。
- 在真正的 `Player.ProcessHitAgainstNPC` 状态施加窗口内捕获近战药剂和物品/模组命中
  回调，不再等到原版减益已经施加完毕后才关联来源。
- 按持续时间分段归属受支持的原版持续伤害减益：刷新现有减益只拥有新增的尾段，提前
  净化不会留下过期归属，断线重连或玩家槽位复用也不会夺走旧效果。
- 通过仍附着在目标上的弹幕实例归属骨制标枪、触手钉刺、血腥屠刀、破晓之光和星尘
  细胞的叠加持续伤害，同时保留原始连接与来源。
- 无法支持或没有所有者的持续伤害仍计入团队/本体总数，并列为未归属服务端伤害，
  不猜测玩家归属。
- 在私有来源树中加入仅本人可见的持续伤害来源详情和本地化减益行。
- 命中时只有目标能唯一匹配某只 Boss，才冻结其私有来源归属；同时多 Boss 时存在歧义的
  目标不会在之后错误转入另一只 Boss 的私有来源树。
- 显式最终形态击杀和首次通关 downed 信号现在可以越过仍存活的控制器或小怪完成结算，
  且不会重新打开并再次发布已经结算的 Boss 结果。
- 同一 Boss key 只有在一次权威扫描中确认完全离场后，才能建立新的伤害账本；因此同场
  其他 Boss 仍存活时再次召唤相同 Boss，既不会被漏掉，也不会与濒死控制器混淆。每次
  出现都有独立的历史和面板身份，公开结算网络格式升级至版本 6。
- 将能够保留玩家所有者的灵魂汲取加入受支持的原版持续伤害集合。
- 使用精确周期跳跃和有界后备算法替代无上限的逐点持续伤害分配，避免极端模组生命恢复
  数值引发超长循环。
- 将生命周期、来源与持续伤害分配的确定性覆盖扩展至 59 项逻辑测试。

### English

- Relicenses the source code and project documentation from MIT to GPL-3.0-only. Earlier published
  releases keep the license shipped with them; the mod icon and Workshop artwork remain excluded.
- Fixed ordinary bosses disappearing without `OnKill` while a living player was still nearby,
  which previously left the encounter pending and produced no Daybreak result panel.
- Keeps explicit NPC-free cinematics and grouped gauntlets on their configured wait path instead
  of treating every temporary disappearance as an escape.
- Measures damage-over-time from the authoritative life loss applied by
  `NPC.UpdateNPC_BuffApplyDOTs`, including the final lethal tick.
- Captures melee flasks and item/mod hit callbacks inside the real
  `Player.ProcessHitAgainstNPC` application window instead of attaching after vanilla had already
  applied its debuffs.
- Attributes supported vanilla DoT debuffs by duration segment, so refreshing an existing debuff
  owns only the added tail, an early cleanse cannot leave stale ownership, and a reconnect/player-
  slot reuse cannot steal an older effect.
- Attributes stacked Bone Javelin, Tentacle Spike, Blood Butcherer, Daybreak, and Stardust Cell DoT
  through their live projectile instances while preserving the original connection and source.
- Counts unsupported or ownerless DoT in team/body totals as unattributed server-side damage rather
  than guessing a player.
- Adds owner-only DoT source details and localized debuff rows to the private source tree.
- Freezes private source attribution to the uniquely matched Boss at hit time, so a target that was
  ambiguous during a simultaneous fight cannot move into another Boss's private tree later.
- Lets explicit final-form kills and first-clear downed signals finish through lingering controllers
  or adds, without reopening and republishing an already resolved Boss result.
- Allows the same Boss key to start a fresh ledger only after it has been fully absent for an
  authoritative scan, so a second summon during a surviving simultaneous Boss is neither omitted
  nor confused with a dying controller. Each occurrence keeps a distinct history/panel identity;
  the public result wire format is now version 6.
- Adds owner-aware Soul Drain to the supported vanilla DoT set.
- Replaces unbounded per-hit-point DoT distribution with exact cycle skipping and a bounded fallback
  for pathological modded regeneration values.
- Extends deterministic lifecycle/source/DoT allocation coverage to 59 logic tests.

## 0.1.6

### 简体中文

- 修复 tModLoader 尚未完成客户端配置与 HUD 注册时便应用本地偏好所导致的客户端加载
  失败。
- 修复团灭或脱战后陈旧战斗与无关 Boss 合并，并导致战斗时长和伤害异常膨胀的问题。
- 加入车轮战的同组生命周期支持，同时不破坏真正的同时多 Boss 战斗。
- 将全队阵亡作为生命周期结算候选，但胜利和已配置的车轮战继续拥有更高优先级。
- 已确认的无 NPC 转阶段期间暂停计伤，并暂存客户端首次命中，直到服务端判断它属于恢复的
  波次还是一场新战斗。
- 不重叠的断线重连会合并为一条公开排行，同时私有来源核对仍限制在精确连接范围内。
- 加入低频、不记录玩家名的生命周期诊断和确定性逻辑回归测试。
- 每只 Boss 分别拥有独立伤害账本、本体小计、持续时间、排行、仅本人来源树和结算结果。
- 每只 Boss 一结束便立即发布自己的结果，不再被仍存活的同时多 Boss 阻塞。
- 面板已打开时，后续结果追加到同一可滚动面板；玩家交互后重新开始自动关闭计时，而不是
  永久取消计时。
- 加入客户端 1–10 场历史记录设置，可按需使用 `/dt` 至 `/dt9`；服务端保留十场公开同步
  窗口。
- 聊天栏映射简化为 Boss 名称、结算状态和查看指令，不再显示 `#0` 一类标签。
- 加入只调整结算面板深色底色透明度的客户端实时滑条。
- 加入 1–120 秒的客户端自动面板持续时间设置；通过 `/dt` 手动打开的面板保持常驻。
- 加入默认开启的本地自动弹出偏好，不改变服务端控制的结算内容或隐私边界。

### English

- Fixes a client load failure caused by applying local preferences before tModLoader finished registering the client config and HUD.
- Fixed stale encounters merging unrelated bosses and inflating duration/damage after a wipe or disengagement.
- Added encounter-group lifecycle support for sequential gauntlets without breaking simultaneous multi-boss fights.
- Treats a party wipe as a lifecycle candidate, with victory and configured gauntlet continuation taking priority.
- Stops accounting during confirmed NPC-free transitions, while buffering the first client hit
  until the server identifies it as a resumed wave or a new encounter.
- Merges a non-overlapping reconnect into one public ranking row while keeping private source reconciliation scoped to the exact connection.
- Added low-frequency, name-free lifecycle diagnostics and deterministic logic regression tests.
- Gives every Boss an independent damage ledger, body subtotal, duration, ranking, owner-only source tree, and result.
- Publishes a Boss result immediately when that Boss ends; simultaneous survivors no longer block it.
- Appends later results to an already open scrollable panel and resets (rather than disables) the automatic close timer after interaction.
- Adds a client-side 1–10 result-history setting with `/dt` through `/dt9` as needed; the server retains a ten-result public sync window.
- Simplifies each chat mapping to Boss name, outcome, and command without `#0`-style labels.
- Adds a live client-side slider for only the result panel's dark background opacity.
- Adds a client-side 1–120 second lifetime setting for automatic panels; manually opened `/dt` panels remain persistent.
- Adds a local, default-on automatic-popup preference without changing server-controlled result content or privacy.

## 0.1.5

### 简体中文

- 加入逐来源和逐弹幕的展开/折叠控制。
- 面板高度限制在当前视口内，并加入鼠标滚轮浏览、滚动位置提示和固定关闭按钮。
- 加入超过服务端紧凑数量限制的其他伤害来源展开/收起控制。
- 玩家与自动结算面板交互后，面板不再立即自动关闭。

### English

- Added per-source and per-projectile expand/collapse controls.
- Added viewport-bounded panel height, mouse-wheel scrolling, a scroll indicator, and a fixed close button.
- Added expansion and collapse controls for source roots beyond the server-selected compact limit.
- Kept an automatic result open after the player interacts with it.

## 0.1.4

### 简体中文

- 通过 Boss Checklist 本地化键或代表 NPC 类型，让客户端按自身语言显示 Boss 名称。
- 为服务端 Boss 覆盖配置加入 `NameLocalizationKey`。
- 更新公开结算数据包格式；服务端与客户端必须使用相同模组版本。

### English

- Added client-side boss-name localization through Boss Checklist localization keys or representative NPC types.
- Added `NameLocalizationKey` for server boss overrides.
- Updated the public result packet format; servers and clients must use the same mod version.

## 0.1.3

### 简体中文

- 更新作者元数据和图片资源。

### English

- Updated author metadata and artwork.

## 0.1.2

### 简体中文

- 完成模组向 Daybreak DamageTracker 的彻底改名。
- 将内部模组 ID 和程序集名称设为 `DaybreakDamageTracker`。
- 将 `/dt`、`dtserver` 和 `DaybreakDamageTracker.Server.json` 确立为稳定的指令与配置
  名称。

### English

- Completed the rename to Daybreak DamageTracker.
- Set the internal mod ID and assembly name to `DaybreakDamageTracker`.
- Added `/dt`, `dtserver`, and `DaybreakDamageTracker.Server.json` as the stable command and configuration names.

## 0.1.1

### 简体中文

- 展示配置重新加载后，会立即应用到已经打开的结算面板。
- 玩家断开连接时清除对应连接槽的请求限流状态。

### English

- Applied presentation reloads to a result panel that is already open.
- Cleared connection-slot request throttles on disconnect.

## 0.1.0

### 简体中文

- 加入服务端 Boss 战生命周期跟踪和有效伤害归属。
- 加入团队排行、Boss 本体伤害、私有来源树、最近历史和结算 HUD。
- 加入可选 Boss Checklist 集成和可配置的 Boss 生命周期覆盖项。

### English

- Added server-side encounter tracking and effective-damage attribution.
- Added team rankings, boss-body damage, private source trees, recent history, and the result HUD.
- Added optional Boss Checklist integration and configurable boss lifecycle overrides.
