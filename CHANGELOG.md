# 更新日志 / Changelog

重要变更记录如下，版本号与 `build.txt` 中的模组版本一致。

Notable changes are documented here. Versions follow the mod version in `build.txt`.

## 0.2.1

### 简体中文

- 修复来源树在补齐缺失差额时覆盖已有未分类伤害的问题；原伤害、叶子和权威总量保持守恒。
- NPC 关联与客户端待归属来源绑定服务端生成代次，避免槽位复用把旧目标的伤害归入新目标；
  私有 DoT 同步同时携带实例身份与明确的 Boss 归属。
- 减益提前清除后重新施加时，无归属施加者也会清理旧玩家及本地来源租约；普通刷新仍只取得新增尾段。
- 隔离加载脚本只清理可证明属于本次运行的进程，避免误关测试期间另行启动的游戏。
- 结算面板为关闭按钮保留空间，配置预览的玩家合计与未归属伤害满足总量守恒。
- 增加来源守恒、租约和实例复用回归，以及纯逻辑 CI；明确灾厄等模组修正下 DoT 玩家份额的估计边界。
- 网络格式变化，服务器与客户端必须同时使用 0.2.1。游戏内验收项目见 `TESTING.md`，源码同步不代表 Workshop 已发布。

### English

- Preserves existing unidentified source damage when reconciling a missing remainder.
- Binds NPC associations and pending client sources to server spawn generations. Private DoT messages
  carry the exact target identity and Boss attribution to prevent slot reuse from changing ownership.
- Clears stale player/local-source leases when a removed debuff is reapplied without an identifiable owner;
  ordinary refreshes continue to attribute only their newly extended duration.
- Restricts isolated-load cleanup to processes proven to belong to that run.
- Reserves space for the close button and makes preview ranking/unattributed totals consistent.
- Adds conservation, lease and instance-reuse regressions and pure-logic CI; documents the estimation
  boundary of DoT player shares when other mods change regeneration weights.
- Changes the network format: servers and clients must both use 0.2.1. See `TESTING.md` for game acceptance;
  source synchronization does not imply a Workshop release.

## 0.2.0

### 简体中文

- 新增 tModLoader 原生的“客户端设置”和“服务端设置”页面。客户端页只控制自动显示、持续时间、
  本地历史和底色透明度；服务端页统一管理展示项目、行数、接收范围、生命周期参数和高级 Boss 覆盖项，
  不再在两端重复同一展示选项。
- 独立服务器默认拒绝远程客户端保存服务端配置。房主可直接修改；服务端控制台可按当前玩家槽位
  临时授权，授权绑定连接代次，并在断线、世界卸载或重启时自动失效。
- 首次升级时会把旧 `DaybreakDamageTracker.Server.json` 一次性导入 tModLoader 规范文件
  `DaybreakDamageTracker_ServerConfig.json`，旧文件保留作回滚参考，之后以新文件为准。
- Boss 战进行中保存的新配置会完整延迟到该场结束后再应用，避免同一场战斗混用两套生命周期、
  投递或展示规则；空闲时的展示变化会立即同步。
- 高级 Boss 覆盖中的继承型布尔值使用明确的“继承／启用／禁用”三态选项，旧 JSON 的
  `null/true/false` 会无损迁移。
- 客户端与服务端页面新增复用实际结算渲染器的实时面板预览；尚未保存的开关、行数和透明度
  变化会直接反映。服务端不再控制任何面板时长或淡出，自动面板持续时间只属于客户端，淡出
  动画固定在本地完成。
- 面板预览优先使用灾厄的犽戎头像与本地化名称；未安装灾厄时仍能安全显示名称占位，不会增加
  灾厄前置依赖。
- 客户端可用箭头调整已获服务端许可的六类内容顺序；Boss 名称、结算状态和最后一击固定在顶部，
  排序不会打开服务端禁用的内容，也不会进入网络协议。
- 所有配置项明确标出默认值；客户端与服务端页面各新增带脉冲特效、五秒二次确认的“全部恢复
  默认值”操作，恢复只修改待保存副本，仍需点击保存且不会绕过服务端权限。
- 修正底色透明度滑块把单精度浮点尾数显示成 `0.90000004` 的问题，并把新配置及
  “全部恢复默认值”的默认不透明度统一为 `0.90`；已有已保存设置不会被强制覆盖。
- “最后一击”不再隐式依赖 Boss 名称与头像开关；标题关闭但最后一击开启时，仍会在
  卡片顶部显示带 Boss 名称的独立终结行。
- 修复同时战斗中一只 Boss 脱战、另一只仍在场时，前者会被拖到整场结束才结算的问题；
  现在按 Boss group 独立应用既有脱战／团灭规则，控制体终结确认和无 NPC 过场仍优先等待。
- 若发布结果的同一次权威扫描已经确认该 Boss 完全消失，现在允许之后立即重新召唤同一 Boss；
  仍可见的死亡动画或残留控制体继续阻止重复建账。
- 发布前生命周期审计进一步收紧了伤害建账、同 tick 新 NPC 扫描、分组团灭／脱战、分段
  `realLife` 根、父级生成附属目标、Boss Rush 查询重试和 DoT 最后一击归属；模糊目标与跨轮次
  实例继续宁可不归属，也不会重复计入或猜测。
- 状态请求和服务端配置应用改为主线程排队；断线清理绑定精确连接代次，历史接收权限冻结并与
  当前规则取更严格者。原生配置增加结构、总文本与 48 KiB 序列化预算，超限远程修改会拒绝，
  旧磁盘值会确定性裁剪，避免网络线程竞态或超大配置封包。
- `RankingRows` 现在同时约束网络上实际发送的玩家姓名；超出的行只发送匿名人数与伤害合计，
  不再只是客户端隐藏。积压的私有 DoT 明细会先于结算及其后的生命周期消息送达，避免结算冻结
  来源树后丢失迟到伤害。
- 结果协议升级为 v9；长 Boss 身份使用稳定哈希后缀而不是静默截断碰撞。面板在真实 HUD 的
  滚动边界也会拦截热栏滚轮，嵌入式预览仍可把边界滚动交还设置页；窄设置页文字会安全缩放或截断。
- 确定性逻辑回归扩展为 180 项；Release 编译保持
  0 警告、0 错误，并通过隔离专服加载与旧配置迁移验证。

### English

- Adds native tModLoader Client Settings and Server Settings pages. Client settings contain only
  automatic behavior, lifetime, local history, and background opacity. Server settings exclusively
  control sections, row limits, recipients, lifecycle parameters, and advanced Boss overrides,
  without duplicate presentation switches on both sides.
- Dedicated servers reject remote server-config saves by default. Hosts may edit directly, while
  the server console can grant the current player-slot connection temporarily. Grants are tied to
  the connection generation and expire on disconnect, world unload, or restart.
- On first upgrade, the legacy `DaybreakDamageTracker.Server.json` is imported once into the native
  `DaybreakDamageTracker_ServerConfig.json`; the legacy file is retained for rollback and is no
  longer authoritative afterward.
- A configuration saved during a Boss fight is applied as one complete snapshot after that
  encounter closes, preventing lifecycle, delivery, or presentation rules from changing mid-fight.
- Inheritable Boss-override booleans use explicit Inherit / Enabled / Disabled options in the UI;
  legacy `null/true/false` values migrate without losing meaning.
- Both pages add a live preview backed by the actual result-panel renderer; unsaved section, row,
  and opacity changes are reflected immediately. The server no longer controls panel lifetime or
  fade behavior: automatic duration is client-only and the fade is a fixed local animation.
- The preview prefers Calamity's Yharon portrait and localized name. It keeps a safe name-only
  fallback when Calamity is absent and does not add a hard dependency.
- Clients may reorder the six server-authorized content sections with arrow controls. Boss identity,
  outcome, and final blow stay fixed at the top; ordering cannot reveal disabled content and is not
  part of the network protocol.
- Every setting now states its default. Both pages add a pulsing, five-second, two-click Restore All
  Defaults action that changes only the pending copy, still requires Save, and cannot bypass server
  authorization.
- Fixes the background-opacity slider exposing a raw floating-point tail such as `0.90000004`, and
  changes the default for new configs and Restore All Defaults to `0.90`. Existing saved values are preserved.
- Final-blow display no longer implicitly depends on the Boss-header switch. When the header is hidden,
  an independent top row still identifies the Boss and its confirmed final blow.
- Fixes an escaped Boss waiting for every simultaneous Boss to end before publishing its own result.
  Existing per-group escape/wipe rules now resolve it independently while controller-final confirmation
  windows and configured NPC-free cinematics remain pending.
- A Boss key proven absent by the same authoritative scan that publishes its result may now re-arm
  immediately afterward, while visible death-animation or controller residue still blocks duplicates.
- The release audit further hardens damage-ledger admission, same-tick NPC rescans, per-group
  wipe/escape state, segmented `realLife` roots, parent-spawned adds, Boss Rush query retries, and
  DoT final-blow attribution. Ambiguous or cross-occurrence targets still fail closed instead of
  being guessed or counted twice.
- State requests and server-config application now cross a main-thread queue. Disconnect cleanup is
  tied to exact connection generations, and historical delivery uses the stricter frozen/current
  recipient rule. Native config receives structural, aggregate-text, and 48 KiB serialized budgets;
  oversized remote edits are rejected and old disk values are trimmed deterministically.
- `RankingRows` now limits the player names actually sent over the wire; excess rows carry only an
  anonymous count and damage total. Backlogged private DoT rows are delivered before the result and
  later lifecycle messages so a client cannot freeze its source tree ahead of older damage.
- Result packets advance to protocol v9. Long Boss identities receive a stable hash suffix instead
  of collision-prone truncation. Real HUD scroll boundaries consume the hotbar wheel while embedded
  previews still bubble boundary scrolling to the config page, and narrow config labels fit safely.
- Deterministic logic coverage grows to 180 tests; the warning-free Release build also passes an isolated dedicated-server load and
  legacy-migration check.

## 0.1.17

### 简体中文

- 修复灾厄 Boss Rush 中星神游龙被错误结算为“已逃离”：灾厄会在分裂形态任意一只头死亡后
  直接推进关卡，并让另一只头无 `OnKill` 退出，这与普通多控制体 Boss 的保守终结规则不同。
- 特殊判定只在灾厄 Boss Rush、星神游龙、本场已权威观察到多个控制头、且实际死亡对象为控制头时
  生效；普通星神游龙、普通脱战和其他多控制体 Boss 继续使用原有生命周期规则。
- 显式服务端 Boss 适配器优先于内置特例；确定性逻辑回归扩展至 136 项，Release 编译保持
  0 警告、0 错误。

### English

- Fixes Astrum Deus being reported as escaped during Calamity Boss Rush. Calamity advances the
  event when either split head dies and removes the other head without `OnKill`, which differs
  from the conservative terminal rule used for ordinary multi-controller bosses.
- The compatibility signal is restricted to Calamity Boss Rush, Astrum Deus, an authoritatively
  observed multi-head phase, and an actual controller death. Ordinary Astrum Deus encounters,
  genuine despawns, and all other multi-controller bosses retain their existing lifecycle rules.
- Explicit server Boss adapters take precedence over the built-in special case. Deterministic
  logic coverage grows to 136 tests; the Release build remains warning-free.

## 0.1.16

### 简体中文

- 修复 0.1.13 引入的弹幕来源合批问题：有效弹幕类型不再被错误拒绝，待发送数据也不会因
  延迟枚举而在封包写入前丢失。
- 来源同步现在使用按连接代次隔离、跨 tick 保留的公平轮询队列；每包、每位接收者和每 tick
  均有明确上限。断线会移除旧连接队列，客户端缓存满时只淘汰最旧条目，不再清空全部等待项。
- “最后一击”改为候选加生命周期确认：只有真实胜利且致死实例与最终控制体证据一致时才显示。
  附属目标、护盾、阶段替身、已逃离/失败 Boss 和无法唯一判断的多控制体终结都不会猜测归属。
- 修复强制结束一次结算后，仍存活的旧 Boss 受到新伤害时可能立即重新建立同一场战斗的问题。
- 确定性逻辑回归扩展至 129 项；Release 编译保持 0 警告、0 错误。

### English

- Fixes projectile-provenance batching introduced in 0.1.13: valid projectile types are no longer
  rejected, and queued entries are materialized before queue mutation instead of being lost.
- Provenance synchronization now uses a connection-generation-aware, fair round-robin queue that
  persists across ticks with explicit per-packet, per-recipient, and per-tick bounds. Disconnects
  remove stale queues, and a full client cache evicts only its oldest pending entry.
- Final-blow display now uses a staged candidate plus lifecycle confirmation. It is shown only for
  a real victory when the lethal instance matches the terminal controller evidence; adds, shields,
  phase stand-ins, escaped/defeated bosses, and ambiguous multi-controller endings are not guessed.
- Fixes a finished encounter immediately re-arming from another hit while its old Boss is still
  present under the force-finish hold.
- Deterministic logic coverage grows to 129 tests; the Release build remains warning-free.

## 0.1.15

### 简体中文

- 新增客户端本地视图偏好：每个客户端可以在自己的配置里隐藏团队总伤害、本体伤害、战斗时长、
  玩家排名、个人来源树、未归属伤害、最后一击行或聊天摘要，并可把排名行数、来源行数与弹幕行数
  上限降到服务端配置之内。
- 边界保持不变：服务端展示开关仍是上限，本地开关只能进一步收窄自己看到的内容，不能打开服务端
  禁用的项目，也不能扩大数据权限。默认值保持现有行为（全部显示、行数跟随服务端）。
- 纯客户端本地行为，不改变网络协议；面板刷新与聊天摘要门控即时生效。

### English

- Adds client-side view preferences: each client can locally hide the team total, boss body damage,
  fight duration, ranking, personal source tree, unattributed damage, final-blow line, or chat
  summary, and cap ranking/source/projectile rows below the server's configured limits.
- The boundary is unchanged: server switches remain the upper bound, and local toggles can only
  narrow the client's own view. Defaults preserve current behavior (everything shown, rows follow
  the server).
- Purely local client behavior with no protocol changes; panel refresh and the chat-summary gate
  apply immediately.

## 0.1.14

### 简体中文

- 新增“最后一击”归属展示：结算面板每只 Boss 的头像区新增一行，显示终结该 Boss 的玩家与方式
  （直接命中 / 持续伤害）。直接命中按致死一击的归属玩家记录；持续伤害致死的那一跳按各效果
  份额取最大归属者；无法归属到玩家的击杀（服务端效果）不显示该行。
- 同时多 Boss 各自独立记录；Boss 结算后不再被后续事件覆盖。内容由服务端展示开关
  `ShowFinalBlow` 控制（默认开启），沿用服务端统一控制展示的边界。
- 结果封包版本升级为 8，展示同步升级为版本 2（旧版展示包可兼容读取并按开启处理）；
  客户端与服务端仍需使用同一版本。
- 不显示击杀武器：近战命中的 DamageNPC 封包不携带武器信息，为保证口径一致只显示玩家与方式。

### English

- Adds a "final blow" line to each boss header in the result panel, naming the player who ended
  the boss and how — direct hit, or damage over time. Direct kills record the attributed hitter;
  a lethal DoT tick records the dominant share owner; kills that cannot be attributed to a player
  show no line.
- Each simultaneous boss records its own final blow, frozen once that boss resolves. The line is
  controlled by the server presentation switch `ShowFinalBlow` (enabled by default).
- The result packet version becomes 8 and the presentation sync becomes version 2 (older
  presentation packets remain readable and default the line to on); matching client and server
  versions are still required.
- The killing weapon is not shown: the DamageNPC packet for melee hits carries no item
  information, so the line consistently shows the player and the kind only.

## 0.1.13

### 简体中文

- 全面源码审计后的维护版本，生命周期与统计核心逻辑不变。
- 性能优化：伤害与持续伤害路径每 tick 最多执行一次权威 Boss 扫描，不再对每次命中、
  每个目标每 tick 重复全量扫描与重建描述符；高密度战斗与小怪群场景的服务器开销显著下降。
- 网络优化：服务端弹幕来源同步改为按接收者每 tick 合并为一个封包，召唤系战斗不再为每个
  生成的弹幕单独发包；封包格式变化要求客户端与服务端使用同一版本。
- 修复天顶世界灾虫转场标记在拥有者 Boss 已结算后仍生效的理论边界：之后独立召唤的同类
  Boss 不会再被并入已结束的战斗组。
- 已知控制体类型不再强制要求 `npc.boss` 标志即可参与自动终结判定，未设该标志的
  Boss Checklist 代表形态也能正常结算；新类型仍只从带 Boss 标志的根学习，防止生成物
  自我提升。
- 结果面板的自动关闭计时改用真实游戏时间，高帧率（如 HighFPS 支持/关闭帧跳过）下
  时长不再偏短；游戏暂停时面板保持显示。
- `/dt <序号>` 超出当前保留数量时提示该序号暂无记录，不再显示用法错误。
- 文档明确统计口径：直击伤害与游戏飘字/原版 DPS 饰品一致（含最后一击溢出），持续伤害
  按实际扣血统计。

### English

- Maintenance release following a full source audit; lifecycle and statistics core logic is
  unchanged.
- Performance: damage and damage-over-time paths now run the authoritative boss scan at most once
  per tick instead of rescanning and rebuilding descriptors on every hit and per target per tick,
  sharply reducing server cost during dense fights and add-heavy encounters.
- Networking: server projectile provenance is now batched into one packet per recipient per tick,
  so summon-heavy fights no longer send one packet per spawned projectile. The changed packet
  format requires matching client and server versions.
- Fixes a theoretical Zenith scourge handoff edge: once the owning boss resolves, its verified
  handoff marker is cleared, so a later independently summoned successor of the same key no longer
  merges into the finished encounter group.
- Known controller types no longer require the `npc.boss` flag to participate in automatic
  finalization, letting Boss Checklist representative forms without the flag finalize normally.
  New controller types are still learned only from boss-flagged roots so spawned adds cannot
  promote themselves.
- The result panel's automatic hide timer now uses real game time, so high frame rates (frame skip
  off or HighFPS support) no longer shorten its duration; the panel stays visible while paused.
- `/dt <index>` beyond the retained history now replies that the slot has no record instead of a
  usage error.
- Documents the damage caliber: direct hits match Terraria's own damage numbers and DPS-meter
  accessory (final-blow overkill included), while damage over time counts health actually removed.

## 0.1.12

### 简体中文

- 修复父级生成目标使用“NPC 类型”归属造成的串账：现在归属绑定到服务端权威的具体 NPC
  槽位与生成代次，同类型的普通 NPC、独立召唤实例或同类 Boss 不会被一并算入父 Boss。
- Boss 生成的嵌套 Boss 只屏蔽实际由该父实例生成的那一组 NPC；同场独立召唤的相同 Boss
  仍保留自己的生命周期、伤害与结算。普通附属目标仍进入团队伤害、排名和本人来源树，
  但不进入 Boss 本体小计。
- 服务端会把有界的实例归属目录同步给客户端，用于本人私有来源树；目录不含其他玩家的
  来源数据，并会在 NPC 槽位复用时失效，避免旧实例归属污染新实例。
- 收紧天顶世界荒漠灾虫转为渊海灾虫的兼容条件：只有 tModLoader 父级来源明确证明前者
  实际生成后者时才合并生命周期。仅仅同时出现两只灾虫不再被误判为转阶段；已存在的
  渊海灾虫描述也会正确更新到同一结算组。
- 增加同类型非关联目标、嵌套与独立同类 Boss 共存、真实父级转场和单纯同场四类回归；
  确定性逻辑测试扩展至 109 项，Release 编译保持 0 警告。

### English

- Fixes type-wide contamination in parent-spawn attribution. Ownership is now bound to the exact
  authoritative NPC slot and spawn generation, so unrelated NPCs, independently spawned instances,
  and same-type bosses are not swept into the parent boss.
- Suppresses only the concrete nested-boss instances created by a parent. An independently summoned
  boss of the same type retains its own lifecycle, damage, and result. Ordinary dependents still
  contribute to team damage, ranking, and the owner's private tree without inflating body damage.
- Synchronizes a bounded instance-association catalog to clients for owner-only source trees. It
  contains no other player's source data and is invalidated when an NPC slot is reused.
- Tightens the Zenith Desert Scourge to Aquatic Scourge adapter. The lifecycle is grouped only when
  tModLoader parent-source evidence proves the former spawned the latter; simple co-presence is no
  longer treated as a phase transition, and an already-known Aquatic descriptor adopts the group.
- Adds regressions for unrelated same-type targets, nested and independent same-type bosses,
  verified parent handoffs, and coincidental co-presence. Deterministic coverage is now 109 tests,
  with a warning-free Release compilation.

## 0.1.11

### 简体中文

- 修复星神游龙分裂时旧 Head 与新 Head 短暂同帧存在、旧 Head 下一帧又无可用 `OnKill`
  证据消失，导致 0.1.10 把控制体阶段标记为 incomplete 并最终显示“已逃离”的问题。
- 自动结算会在原有两 tick 确认窗内记录控制体重叠交接：只有旧控制体集合完整退出、当前
  存活控制体全部来自刚出现的后继集合时才接受换阶段；长期共存、旧兄弟仍在而另一只消失，
  或没有后继控制体的整体消失仍按真实逃离处理。
- 增加星神游龙式“旧根与后继根重叠一帧”、稳定多控制体部分逃离和超时重叠三组确定性
  回归，逻辑测试扩展至 105 项；日志会匿名记录成功接受的重叠控制体交接。

### English

- Fixes Astrum Deus splitting with the old Head and successor Heads briefly present in the same
  scan, followed by the old Head disappearing without usable `OnKill` evidence on the next frame.
  Version 0.1.10 treated that sequence as an incomplete controller epoch and later reported
  `Escaped`.
- Tracks an overlapping controller handoff within the existing two-tick confirmation window. The
  transition is accepted only when the complete predecessor set exits and every surviving root
  belongs to the newly observed successor set. Long-lived overlap, partial loss while an old
  sibling remains, and disappearance without successors remain conservative escape evidence.
- Adds deterministic regressions for one-frame predecessor/successor overlap, partial loss in a
  stable multi-controller fight, and expired overlap. Logic coverage is now 105 tests, with an
  anonymous diagnostic when an overlapping handoff is accepted.

## 0.1.10

### 简体中文

- 修复星神游龙等分段 Boss 在重复击杀时仍可能把真实胜利显示为“已逃离”的问题。
  Boss Checklist 的本体列表现在与生命周期控制体列表分离，Body/Tail 的直接失活或
  `OnSpawn` 事件不再被误当成未完整交代的控制体消失。
- 控制体终结候选只接受完成 NPC 初始化后的根 Boss，或已明确登记的控制体类型；分裂阶段
  仍有兄弟控制体、后继控制体或不完整的多控制体消失时继续取消或拒绝胜利候选。
- 对成对 Boss、组合 Boss 和后续独立根形态，服务端会在权威 Post-AI 扫描中学习额外控制体，
  不依赖体节生成瞬间尚未稳定的 `realLife` 值。服务端覆盖配置新增可选
  `ControllerNpcTypes`，配置架构升级为 3。
- 增加匿名生命周期诊断日志，记录控制体终结候选建立、撤销、确认及不完整控制体阶段；
  日志不包含玩家名或逐次伤害。
- 按灾厄 2.2.4 与 Boss Checklist 2.2.4 的实际程序注册和 NPC 生命周期复核全部 26 个灾厄
  Boss 条目，并将确定性生命周期、来源、持续伤害和聊天回归覆盖扩展至 102 项逻辑测试。

### English

- Fixes a remaining repeat-clear path where segmented bosses such as Astrum Deus could still
  report a real victory as `Escaped`. Boss Checklist body membership is now separate from
  lifecycle-controller membership, so Body/Tail direct deactivation and `OnSpawn` events cannot
  masquerade as an incompletely resolved controller.
- Restricts controller-final candidates to initialized root Boss NPCs or explicitly known
  controller types. A surviving split sibling, phase successor, or incomplete multi-controller
  disappearance still cancels or rejects the candidate.
- Learns additional independent roots for paired, composite, and later-phase Bosses during the
  authoritative post-AI scan instead of trusting transient `realLife` values at spawn time.
  Server overrides gain an optional `ControllerNpcTypes` role and the config schema is now 3.
- Adds anonymous lifecycle diagnostics for controller-final candidate arm, cancellation,
  confirmation, and incomplete controller epochs. These logs contain no player names or per-hit
  records.
- Programmatically reviews all 26 Calamity 2.2.4 Boss Checklist entries against Boss Checklist
  2.2.4 and extends deterministic lifecycle/source/DoT/chat regression coverage to 102 logic tests.

## 0.1.9

### 简体中文

- 修复 Boss Checklist 只登记控制体、却漏掉同样带有 Boss 标记的 Body/Tail 时，遗漏体节
  被另行识别为多个 `npc:*` Boss 并生成重复结算的问题；神明吞噬者现在只保留一场结果，
  体节伤害继续并入同一 Boss。
- 遗漏的分段 NPC 现在按权威 `realLife` 控制体关系归入唯一的现有 Boss；未被目录登记的
  普通分段 Boss 也会共用控制体的回退 key。共享控制体存在歧义时不会再凭空建立第三场
  结算。
- 继承分段角色时分别保留本体、排除项和 KeepAlive 语义，避免把配置为附属目标的分段
  意外变成 Boss 本体或额外延长战斗。
- 按 tModLoader 的权威 NPC 父级生成来源，把 Boss 自己生成的护盾、召唤物和幻象归入
  唯一的父 Boss；这些目标进入团队总伤害、排名和本人来源树，但不进入 Boss 本体小计。
  父级或目标存在歧义时不会猜测，显式服务端适配器始终优先。
- Boss 自己生成且被 Boss Checklist 另行登记的战斗内幻象/嵌套 Boss 不再产生伪造的独立
  结算；服务器在战斗中发现新关联类型时会同步更新客户端归属目录。
- 灾厄 Boss Rush 不再依赖每只 Boss 的首次 downed 跃迁，而使用同样带转阶段撤销和确认窗
  的控制体击杀逻辑；查询结果按游戏 tick 缓存，不随命中次数重复调用。
- 兼容天顶世界中荒漠灾虫直接转为渊海灾虫的特殊流程：两段分别保留伤害记录，并由最终
  渊海灾虫的击杀或真实脱战关闭整组，不会把中间直接失活误判为一场提前逃离。
- 同一小批结果连续到达客户端时，聊天栏会在短暂静默窗口后只输出一次最终历史映射，
  不再为每个结果重复刷出整行 `/dt` 至 `/dtN` 指令。
- 针对灾厄 2.2.1 的 26 个 Boss Checklist Boss 完成程序级生命周期复核；生命周期、来源、
  持续伤害、生成归属、分段回退和聊天批处理的确定性覆盖扩展至 100 项逻辑测试。

### English

- Fixes Boss Checklist entries that register only the controller while omitting boss-marked
  Body/Tail types. Those segments no longer arm extra `npc:*` encounters and publish duplicate
  results; the Devourer of Gods now keeps one result while segment damage remains in that Boss.
- Omitted segmented NPCs now follow their authoritative `realLife` controller into the unique
  existing Boss. Uncatalogued segmented fallbacks also share the controller key, while an
  ambiguous shared controller never creates a third synthetic result.
- Preserves body, excluded-target, and keep-alive roles separately when inheriting a segment, so
  configured adds neither become body damage nor extend the encounter implicitly.
- Uses tModLoader's authoritative NPC parent-spawn source to associate boss-created shields,
  summons, and illusions with their unique parent Boss. They contribute to team damage, ranking,
  and the owner's source tree without inflating the Boss-body subtotal. Ambiguous ancestry or
  target ownership is never guessed, and explicit server adapters take precedence.
- Prevents combat illusions or nested Bosses spawned by another Boss from publishing synthetic
  standalone results, and republishes the associated-target catalog when a new type appears.
- Allows Calamity Boss Rush to use the phase-safe controller-kill confirmation path without relying
  on a first-clear downed transition. The optional ModCall is cached once per game tick.
- Handles the Zenith-world Desert Scourge to Aquatic Scourge transformation as one lifecycle with
  separate per-Boss damage records, closed by the final Aquatic Scourge kill or real escape.
- Coalesces a short burst of client result packets and prints the final history mapping once after
  a brief quiet window instead of repeating the full `/dt` through `/dtN` line per result.
- Completes a programmatic lifecycle review of all 26 Calamity 2.2.1 Boss Checklist bosses and
  extends deterministic lifecycle, source, DoT, spawn-attribution, segmented-fallback, and
  chat-batching coverage to 100 logic tests.

## 0.1.8

### 简体中文

- 修复星神游龙等分裂/合体式分段 Boss 在重复挑战中已正常击败并掉落战利品，却因残余
  体节直接失活、没有逐节触发 `OnKill` 而被显示为“已逃离”的问题。
- 自动结算现在以根/控制 NPC 的 `OnKill` 建立候选；同场仍有兄弟控制器或转阶段后继
  控制器时会撤销候选，最终控制器死亡后的 Body/Tail 清理则不再覆盖胜利证据。
- 每只 Boss 分别使用至少两个权威扫描窗口确认最终离场。一个 Boss 的确认不会等待同时
  战斗中的其他 Boss，也不会被同一时刻的团灭、脱战或新遭遇边界抢先改写。
- 补强同帧生成、同槽位复用、未扫描兄弟控制器与死亡动画残留的保守判定；没有控制器
  击杀证据的整体消失仍结算为逃离。
- 将生命周期、来源与持续伤害分配的确定性覆盖扩展至 79 项逻辑测试。

### English

- Fixes split/merge segmented bosses such as Astrum Deus being reported as `Escaped` on repeat
  clears even after a real kill and loot drop, when residual segments deactivate without firing
  `OnKill` individually.
- Automatic completion now starts from a root/controller NPC `OnKill` candidate. A surviving
  sibling or phase-successor controller cancels it, while Body/Tail cleanup after the final
  controller no longer erases valid victory evidence.
- Confirms final absence over at least two authoritative scans per Boss. One Boss's confirmation
  neither waits for simultaneous Bosses nor loses the result to a same-time wipe, despawn, or new
  encounter boundary.
- Adds conservative handling for same-scan spawns, reused NPC slots, unscanned sibling controllers,
  and lingering death animations. A complete disappearance without controller-kill evidence still
  resolves as `Escaped`.
- Extends deterministic lifecycle/source/DoT allocation coverage to 79 logic tests.

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
