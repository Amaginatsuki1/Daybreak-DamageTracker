using DaybreakDamageTracker.Common.Data;
using Terraria.GameInput;
using Terraria.UI;

namespace DaybreakDamageTracker.Client.UI;

internal sealed class ResultPanelElement : UIElement
{
    public const float MaximumPanelWidth = 700f;
    private const float Margin = 18f;
    private const float MetricHeight = 24f;
    private const float RankingHeight = 21f;
    private const float SourceHeight = 19f;
    private const float SectionHeadingHeight = 27f;

    private ClientHistoryEntry? _entry;
    private PresentationSettings _settings = new();
    private readonly List<RenderLine> _rankingLines = [];
    private readonly List<RenderLine> _sourceLines = [];
    private readonly List<RenderLine> _metricLines = [];
    private readonly List<SourceToggleHitbox> _sourceToggleHitboxes = [];
    private readonly HashSet<DamageRootKey> _collapsedSourceRoots = [];
    private DamageRootKey? _expandedProjectileRoot;
    private bool _showAllSourceRoots;
    private bool _showRankingSection;
    private bool _showSourceSection;
    private int _layoutScreenWidth;
    private int _layoutScreenHeight;
    private float _layoutUiScale;
    private float _contentHeight;
    private float _sourceSectionOffset;
    private float _scrollOffset;
    private float _maximumScrollOffset;

    public float DesiredWidth { get; private set; } = MaximumPanelWidth;
    public float DesiredHeight { get; private set; } = 300f;
    public float Opacity { get; set; } = 1f;

    public void Bind(ClientHistoryEntry entry, PresentationSettings settings)
    {
        if (!ReferenceEquals(_entry, entry))
        {
            _expandedProjectileRoot = null;
            _collapsedSourceRoots.Clear();
            _showAllSourceRoots = false;
            _scrollOffset = 0f;
        }
        _entry = entry;
        _settings = settings.SanitizedClone();
        BuildLines();
    }

    public bool TryToggleSourceTree(Vector2 mousePosition)
    {
        foreach (SourceToggleHitbox toggle in _sourceToggleHitboxes)
        {
            if (!toggle.Bounds.Contains((int)mousePosition.X, (int)mousePosition.Y))
                continue;

            if (toggle.Kind == SourceToggleKind.RootList)
            {
                _showAllSourceRoots = !_showAllSourceRoots;
            }
            else if (toggle.Kind == SourceToggleKind.RootChildren && toggle.RootKey is DamageRootKey rootKey)
            {
                if (!_collapsedSourceRoots.Remove(rootKey))
                {
                    _collapsedSourceRoots.Add(rootKey);
                    if (_expandedProjectileRoot == rootKey)
                        _expandedProjectileRoot = null;
                }
            }
            else if (toggle.Kind == SourceToggleKind.ProjectileList && toggle.RootKey is DamageRootKey projectileRoot)
            {
                bool expanding = _expandedProjectileRoot != projectileRoot;
                _expandedProjectileRoot = expanding
                    ? projectileRoot
                    : null;
            }
            else
            {
                continue;
            }

            BuildLines();
            return true;
        }

        return false;
    }

    public bool RefreshLayoutForViewport()
    {
        float uiScale = Math.Max(Main.UIScale, 0.5f);
        if (_layoutScreenWidth == Main.screenWidth &&
            _layoutScreenHeight == Main.screenHeight &&
            Math.Abs(_layoutUiScale - uiScale) < 0.001f)
            return false;

        BuildLines();
        return true;
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        _sourceToggleHitboxes.Clear();
        if (_entry is null || Opacity <= 0f)
            return;

        CalculatedStyle dimensions = GetDimensions();
        Rectangle panel = dimensions.ToRectangle();
        DrawPanel(spriteBatch, panel);

        float contentTop = panel.Y + Margin;
        float contentBottom = panel.Bottom - Margin;
        float x = panel.X + Margin;
        float y = contentTop - _scrollOffset;
        float right = panel.Right - Margin - (_maximumScrollOffset > 0f ? 10f : 0f);

        if (_settings.ShowBossHeader)
        {
            if (IsFullyVisible(y, 70f, contentTop, contentBottom))
                DrawBossHeader(spriteBatch, x, y, right);
            y += 70f;
        }

        foreach (RenderLine line in _metricLines)
        {
            if (IsFullyVisible(y, MetricHeight, contentTop, contentBottom))
                DrawLine(spriteBatch, line, x, right, y, 0.92f);
            y += MetricHeight;
        }

        if (_showRankingSection && _rankingLines.Count > 0)
        {
            if (IsFullyVisible(y, SectionHeadingHeight, contentTop, contentBottom))
            {
                string heading = TrimToWidth(ClientRuntime.Text("UI.Ranking", "Player damage ranking"), Math.Max(0f, right - x), 0.96f);
                DrawText(spriteBatch, heading, new Vector2(x, y + 2f), new Color(112, 222, 255), 0.96f);
            }
            y += SectionHeadingHeight;
            foreach (RenderLine line in _rankingLines)
            {
                if (IsFullyVisible(y, RankingHeight, contentTop, contentBottom))
                    DrawLine(spriteBatch, line, x, right, y, 0.84f);
                y += RankingHeight;
            }
        }

        if (_showSourceSection && _sourceLines.Count > 0)
        {
            if (IsFullyVisible(y, SectionHeadingHeight, contentTop, contentBottom))
            {
                long localTotal = _entry.PrivateSources.CanonicalTotal;
                string heading = _entry.PrivateSources.CanonicalTotalKnown
                    ? ClientRuntime.Text("UI.PersonalBreakdown", "Your damage sources — {0}", FormatDamage(localTotal))
                    : ClientRuntime.Text("UI.PersonalBreakdownUnknown", "Your damage sources");
                heading = TrimToWidth(heading, Math.Max(0f, right - x), 0.96f);
                DrawText(spriteBatch, heading, new Vector2(x, y + 2f), new Color(255, 210, 112), 0.96f);
            }
            y += SectionHeadingHeight;
            foreach (RenderLine line in _sourceLines)
            {
                if (IsFullyVisible(y, SourceHeight, contentTop, contentBottom))
                {
                    RenderLine drawnLine = line;
                    if (line.ToggleKind != SourceToggleKind.None)
                    {
                        float toggleLeft = x + line.Indent * 20f;
                        Rectangle hitbox = new(
                            (int)toggleLeft,
                            (int)y,
                            Math.Max(1, (int)(right - toggleLeft)),
                            (int)SourceHeight);
                        _sourceToggleHitboxes.Add(new SourceToggleHitbox(hitbox, line.ToggleRoot, line.ToggleKind));
                        if (hitbox.Contains((int)Main.MouseScreen.X, (int)Main.MouseScreen.Y))
                            drawnLine = line with { Color = new Color(125, 220, 255) };
                    }

                    DrawLine(spriteBatch, drawnLine, x, right, y, line.Indent == 0 ? 0.82f : 0.76f);
                }
                y += SourceHeight;
            }
        }

        DrawScrollBar(spriteBatch, panel);
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        if (ContainsPoint(Main.MouseScreen) && !PlayerInput.IgnoreMouseInterface)
            Main.LocalPlayer.mouseInterface = true;
    }

    public override void ScrollWheel(UIScrollWheelEvent evt)
    {
        base.ScrollWheel(evt);
        if (_maximumScrollOffset <= 0f)
            return;

        float step = Math.Sign(evt.ScrollWheelValue) * SourceHeight * 3f;
        _scrollOffset = Math.Clamp(_scrollOffset - step, 0f, _maximumScrollOffset);
        PlayerInput.LockVanillaMouseScroll("DaybreakDamageTracker/ResultPanel");
        ModContent.GetInstance<DamageResultHudSystem>().KeepOpenAfterInteraction();
    }

    private void BuildLines()
    {
        _rankingLines.Clear();
        _sourceLines.Clear();
        _metricLines.Clear();
        _showRankingSection = false;
        _showSourceSection = false;
        _sourceToggleHitboxes.Clear();

        float uiScale = Math.Max(Main.UIScale, 0.5f);
        _layoutScreenWidth = Main.screenWidth;
        _layoutScreenHeight = Main.screenHeight;
        _layoutUiScale = uiScale;

        float uiWidth = Main.screenWidth / uiScale;
        float uiHeight = Main.screenHeight / uiScale;
        DesiredWidth = Math.Max(1f, Math.Min(MaximumPanelWidth, uiWidth - 36f));
        float maxHeight = Math.Max(1f, uiHeight - 36f);

        if (_entry is null)
        {
            DesiredHeight = Math.Min(220f, maxHeight);
            _contentHeight = DesiredHeight;
            _maximumScrollOffset = 0f;
            _scrollOffset = 0f;
            return;
        }

        if (_expandedProjectileRoot is DamageRootKey expandedKey &&
            !_entry.PrivateSources.Roots.Any(root => root.Key == expandedKey))
        {
            _expandedProjectileRoot = null;
        }
        HashSet<DamageRootKey> currentRootKeys = _entry.PrivateSources.Roots.Select(root => root.Key).ToHashSet();
        _collapsedSourceRoots.RemoveWhere(key => !currentRootKeys.Contains(key));
        if (!_settings.ShowPersonalBreakdown)
        {
            _expandedProjectileRoot = null;
            _collapsedSourceRoots.Clear();
            _showAllSourceRoots = false;
        }

        PublicResultSnapshot result = _entry.Public;
        if (_settings.ShowTeamTotal)
            _metricLines.Add(new RenderLine(ClientRuntime.Text("UI.TeamTotal", "Team total damage"), FormatDamage(result.TeamDamage), 0, Color.White));
        if (_settings.ShowBossBodyDamage)
        {
            string value = $"{FormatDamage(result.BossBodyDamage)}  ({Percent(result.BossBodyDamage, result.TeamDamage)})";
            _metricLines.Add(new RenderLine(ClientRuntime.Text("UI.BossBodyDamage", "Damage to boss body"), value, 0, new Color(255, 180, 125)));
        }
        if (_settings.ShowUnattributedDamage && result.UnattributedDamage > 0)
            _metricLines.Add(new RenderLine(ClientRuntime.Text("UI.Unattributed", "Unattributed/server-side damage"), FormatDamage(result.UnattributedDamage), 0, Color.Gray));
        if (_settings.ShowDuration)
            _metricLines.Add(new RenderLine(ClientRuntime.Text("UI.Duration", "Duration"), ClientRuntime.FormatDuration(result.DurationTicks), 0, new Color(190, 220, 255)));

        if (_settings.ShowRanking)
            BuildRanking(result, 4096);
        if (_settings.ShowPersonalBreakdown)
            BuildSources(_entry.PrivateSources, 4096);

        _showRankingSection = _rankingLines.Count > 0;
        _showSourceSection = _sourceLines.Count > 0;

        float cursor = Margin + (_settings.ShowBossHeader ? 70f : 0f) + _metricLines.Count * MetricHeight;
        if (_showRankingSection)
            cursor += SectionHeadingHeight + _rankingLines.Count * RankingHeight;
        _sourceSectionOffset = Math.Max(0f, cursor - Margin);
        if (_showSourceSection)
            cursor += SectionHeadingHeight + _sourceLines.Count * SourceHeight;

        _contentHeight = cursor + Margin;
        DesiredHeight = Math.Max(1f, Math.Min(_contentHeight, maxHeight));
        _maximumScrollOffset = Math.Max(0f, _contentHeight - DesiredHeight);
        _scrollOffset = Math.Clamp(_scrollOffset, 0f, _maximumScrollOffset);
    }

    private void BuildRanking(PublicResultSnapshot result, int maxLines)
    {
        if (maxLines <= 0)
            return;

        List<PlayerResultRow> rows = result.Players.OrderByDescending(row => row.Damage).ToList();
        if (rows.Count == 0)
        {
            _rankingLines.Add(new RenderLine(ClientRuntime.Text("UI.NoDamage", "No attributable player damage"), string.Empty, 0, Color.Gray));
            return;
        }

        int configuredTop = Math.Min(_settings.RankingRows, rows.Count);
        int topCount = Math.Min(configuredTop, maxLines);
        bool needsOther = rows.Count > topCount;
        if (needsOther && topCount >= maxLines && maxLines > 1)
            topCount = Math.Max(0, topCount - 1);

        for (int i = 0; i < topCount; i++)
        {
            PlayerResultRow row = rows[i];
            _rankingLines.Add(new RenderLine(
                $"#{i + 1}  {row.PlayerName}",
                $"{FormatDamage(row.Damage)}  ({Percent(row.Damage, result.TeamDamage)})",
                0,
                i == 0 ? new Color(255, 223, 120) : Color.White));
        }

        if (rows.Count > topCount && _rankingLines.Count < maxLines)
        {
            List<PlayerResultRow> omitted = rows.Skip(topCount).ToList();
            long damage = omitted.Sum(row => row.Damage);
            _rankingLines.Add(new RenderLine(
                ClientRuntime.Text("UI.OtherPlayers", "Other {0} players", omitted.Count),
                $"{FormatDamage(damage)}  ({Percent(damage, result.TeamDamage)})",
                0,
                Color.Gray));
        }
    }

    private void BuildSources(SourceTreeSnapshot tree, int maxLines)
    {
        if (maxLines <= 0)
            return;

        if (!tree.SourceDetailsAvailable)
        {
            _sourceLines.Add(new RenderLine(ClientRuntime.Text("UI.NoPrivateHistory", "No private source data is available for this result"), string.Empty, 0, Color.Gray));
            return;
        }

        if (tree.CanonicalTotal <= 0)
        {
            _sourceLines.Add(new RenderLine(ClientRuntime.Text("UI.NoPersonalDamage", "You dealt no attributable damage"), string.Empty, 0, Color.Gray));
            return;
        }

        List<SourceRootSnapshot> roots = tree.Roots.OrderByDescending(root => root.Damage).ToList();
        if (roots.Count == 0)
        {
            _sourceLines.Add(new RenderLine(ClientRuntime.Text("UI.NoPrivateHistory", "No private source data is available for this result"), string.Empty, 0, Color.Gray));
            return;
        }

        if (_expandedProjectileRoot is DamageRootKey expandedKey &&
            roots.All(root => root.Key != expandedKey))
            _expandedProjectileRoot = null;

        int configuredRootCount = Math.Min(_settings.SourceRootRows, roots.Count);
        bool hasAdditionalRoots = roots.Count > configuredRootCount;
        if (!hasAdditionalRoots)
            _showAllSourceRoots = false;

        List<SourceBlock> candidates = (_showAllSourceRoots ? roots : roots.Take(configuredRootCount))
            .Select(CreateSourceBlock)
            .ToList();
        if (!_showAllSourceRoots && _expandedProjectileRoot is DamageRootKey hiddenExpandedRoot &&
            candidates.All(block => block.Root.Key != hiddenExpandedRoot))
        {
            _expandedProjectileRoot = null;
        }

        int rootListToggleReserve = _showAllSourceRoots && hasAdditionalRoots && maxLines > 1 ? 1 : 0;
        int sourceContentLineLimit = maxLines - rootListToggleReserve;
        List<SourceBlock> included = [];
        int mandatoryLineCount = 0;
        int protectedDetailLines = 0;

        foreach (SourceBlock block in candidates)
        {
            bool collapsed = _collapsedSourceRoots.Contains(block.Root.Key);
            int required = 1 + (collapsed || block.ItemBody is null ? 0 : 1);
            if (mandatoryLineCount + required + protectedDetailLines > sourceContentLineLimit)
                break;

            included.Add(block);
            mandatoryLineCount += required;

            // Reserve one useful projectile row for the highest visible root that has
            // projectiles before considering lower roots. This gives an item root the
            // compact three-line form: total, weapon body, projectile summary.
            if (!collapsed && protectedDetailLines == 0 && block.Projectiles.Count > 0 && mandatoryLineCount < sourceContentLineLimit)
                protectedDetailLines = 1;
        }

        if (included.Count == 0)
        {
            // A one-line viewport should still show the highest source and its value,
            // never just a generic "more rows" notice.
            AddRootLine(roots[0], tree.CanonicalTotal);
            return;
        }

        HashSet<DamageRootKey> includedKeys = included.Select(block => block.Root.Key).ToHashSet();
        List<SourceRootSnapshot> omittedRoots = roots.Where(root => !includedKeys.Contains(root.Key)).ToList();
        int remainingLines = sourceContentLineLimit - mandatoryLineCount;
        int otherRootLines = !_showAllSourceRoots && omittedRoots.Count > 0 && remainingLines > protectedDetailLines ? 1 : 0;
        int projectileLineBudget = Math.Max(0, remainingLines - otherRootLines);

        int[] projectileAllocations = new int[included.Count];
        int[] desiredProjectileLines = included.Select(GetDesiredProjectileLineCount).ToArray();

        int expandedIndex = included.FindIndex(block => _expandedProjectileRoot == block.Root.Key);
        if (expandedIndex >= 0 && projectileLineBudget > 0)
        {
            int expandedAllocation = Math.Min(desiredProjectileLines[expandedIndex], projectileLineBudget);
            projectileAllocations[expandedIndex] = expandedAllocation;
            projectileLineBudget -= expandedAllocation;
        }

        // Give each other visible root one projectile row first, then distribute
        // additional rows in damage order. A constrained root uses that row as an aggregate.
        for (int i = 0; i < included.Count && projectileLineBudget > 0; i++)
        {
            if (i == expandedIndex)
                continue;
            if (desiredProjectileLines[i] <= 0)
                continue;
            projectileAllocations[i]++;
            projectileLineBudget--;
        }

        bool allocated;
        do
        {
            allocated = false;
            for (int i = 0; i < included.Count && projectileLineBudget > 0; i++)
            {
                if (i == expandedIndex)
                    continue;
                if (projectileAllocations[i] >= desiredProjectileLines[i])
                    continue;
                projectileAllocations[i]++;
                projectileLineBudget--;
                allocated = true;
            }
        }
        while (allocated && projectileLineBudget > 0);

        for (int i = 0; i < included.Count; i++)
        {
            SourceBlock block = included[i];
            bool collapsed = _collapsedSourceRoots.Contains(block.Root.Key);
            bool hasChildren = block.ItemBody is not null || block.Projectiles.Count > 0;
            AddRootLine(block.Root, tree.CanonicalTotal, hasChildren, collapsed);
            if (collapsed)
                continue;
            if (block.ItemBody is not null)
                AddLeafLine(block.ItemBody, block.Root.Damage);
            AddProjectileLines(block, projectileAllocations[i]);
        }

        if (otherRootLines > 0)
        {
            long otherDamage = omittedRoots.Sum(root => root.Damage);
            _sourceLines.Add(new RenderLine(
                ClientRuntime.Text("UI.OtherSources", "▶ Other {0} sources", omittedRoots.Count),
                $"{FormatDamage(otherDamage)}  ({Percent(otherDamage, tree.CanonicalTotal)})",
                0,
                Color.Gray,
                SourceToggleKind.RootList));
        }

        if (_showAllSourceRoots && hasAdditionalRoots && _sourceLines.Count < maxLines)
        {
            string label = omittedRoots.Count > 0
                ? ClientRuntime.Text("UI.CollapseSourcesClipped", "▼ Collapse source list ({0} sources do not fit)", omittedRoots.Count)
                : ClientRuntime.Text("UI.CollapseSources", "▼ Collapse source list");
            _sourceLines.Add(new RenderLine(
                label,
                string.Empty,
                0,
                new Color(150, 205, 235),
                SourceToggleKind.RootList));
        }

        if (tree.WasScaledToCanonicalTotal && _sourceLines.Count < maxLines)
            _sourceLines.Add(new RenderLine(ClientRuntime.Text("UI.ServerReconciled", "Source values were reconciled to the server-confirmed total"), string.Empty, 0, new Color(255, 180, 120)));
    }

    private SourceBlock CreateSourceBlock(SourceRootSnapshot root)
    {
        SourceLeafSnapshot? itemBody = root.Key.Kind == DamageRootKind.Item
            ? root.Leaves.Where(leaf => leaf.Key.Kind == DamageLeafKind.ItemBody).OrderByDescending(leaf => leaf.Damage).FirstOrDefault()
            : null;
        List<SourceLeafSnapshot> projectiles = root.Leaves
            .Where(leaf => leaf.Key.Kind == DamageLeafKind.Projectile)
            .OrderByDescending(leaf => leaf.Damage)
            .ToList();

        bool redundantFallback = root.Key.Kind == DamageRootKind.ProjectileFallback && projectiles.Count == 1 &&
            projectiles[0].Key.Type == root.Key.PrimaryType;
        if (redundantFallback)
            projectiles.Clear();

        return new SourceBlock(root, itemBody, projectiles);
    }

    private int GetDesiredProjectileLineCount(SourceBlock block)
    {
        if (_collapsedSourceRoots.Contains(block.Root.Key) || block.Projectiles.Count == 0)
            return 0;
        if (_expandedProjectileRoot == block.Root.Key)
            return block.Projectiles.Count + 1;
        if (block.Projectiles.Count == 1)
            return 1;

        int individualLimit = _settings.SourceLeafRows;
        int individuals = Math.Min(individualLimit, block.Projectiles.Count);
        return individuals + (individuals < block.Projectiles.Count ? 1 : 0);
    }

    private void AddRootLine(SourceRootSnapshot root, long canonicalTotal, bool hasChildren = false, bool collapsed = false)
    {
        string name = ResolveRootName(root.Key);
        SourceToggleKind toggleKind = SourceToggleKind.None;
        DamageRootKey? toggleRoot = null;
        if (hasChildren)
        {
            name += collapsed ? "  ▶" : "  ▼";
            toggleKind = SourceToggleKind.RootChildren;
            toggleRoot = root.Key;
        }

        _sourceLines.Add(new RenderLine(
            name,
            $"{FormatDamage(root.Damage)}  ({Percent(root.Damage, canonicalTotal)})",
            0,
            root.Key.Kind == DamageRootKind.Unclassified ? Color.Gray : new Color(255, 232, 175),
            toggleKind,
            toggleRoot));
    }

    private void AddLeafLine(SourceLeafSnapshot leaf, long rootDamage)
    {
        _sourceLines.Add(new RenderLine(
            ResolveLeafName(leaf.Key),
            $"{FormatDamage(leaf.Damage)}  ({Percent(leaf.Damage, rootDamage)})",
            1,
            new Color(205, 215, 230)));
    }

    private void AddProjectileLines(SourceBlock block, int allocatedLines)
    {
        if (allocatedLines <= 0 || block.Projectiles.Count == 0)
            return;

        if (block.Projectiles.Count == 1)
        {
            AddLeafLine(block.Projectiles[0], block.Root.Damage);
            return;
        }

        if (_expandedProjectileRoot == block.Root.Key)
        {
            int expandedIndividualCount = Math.Min(block.Projectiles.Count, Math.Max(0, allocatedLines - 1));
            for (int i = 0; i < expandedIndividualCount; i++)
                AddLeafLine(block.Projectiles[i], block.Root.Damage);

            int omittedCount = block.Projectiles.Count - expandedIndividualCount;
            string label = omittedCount > 0
                ? ClientRuntime.Text("UI.CollapseProjectilesClipped", "▼ Collapse ({0} projectiles do not fit)", omittedCount)
                : ClientRuntime.Text("UI.CollapseProjectiles", "▼ Collapse projectile list");
            _sourceLines.Add(new RenderLine(
                label,
                string.Empty,
                1,
                new Color(150, 205, 235),
                SourceToggleKind.ProjectileList,
                block.Root.Key));
            return;
        }

        int individualLimit = _settings.SourceLeafRows;
        bool allFit = block.Projectiles.Count <= individualLimit && block.Projectiles.Count <= allocatedLines;
        int individualCount = allFit
            ? block.Projectiles.Count
            : Math.Min(individualLimit, Math.Max(0, allocatedLines - 1));

        for (int i = 0; i < individualCount; i++)
            AddLeafLine(block.Projectiles[i], block.Root.Damage);

        List<SourceLeafSnapshot> omitted = block.Projectiles.Skip(individualCount).ToList();
        if (omitted.Count > 0 && allocatedLines > individualCount)
        {
            long otherDamage = omitted.Sum(leaf => leaf.Damage);
            _sourceLines.Add(new RenderLine(
                ClientRuntime.Text("UI.OtherProjectiles", "▶ Other {0} projectiles", omitted.Count),
                $"{FormatDamage(otherDamage)}  ({Percent(otherDamage, block.Root.Damage)})",
                1,
                Color.Gray,
                SourceToggleKind.ProjectileList,
                block.Root.Key));
        }
    }

    private void DrawBossHeader(SpriteBatch spriteBatch, float x, float y, float right)
    {
        if (_entry is null)
            return;

        PublicResultSnapshot result = _entry.Public;
        BossPresentation? primary = result.Bosses.FirstOrDefault();
        bool drawPortrait = primary is not null && right - x >= 220f;
        if (drawPortrait)
            DrawNpcPortrait(spriteBatch, primary!.PortraitNpcType, new Rectangle((int)x, (int)y, 56, 56));

        float textX = drawPortrait ? x + 72f : x;
        string bossName = result.Bosses.Count == 0
            ? ClientRuntime.Text("UI.UnknownBoss", "Unknown boss")
            : string.Join(" + ", result.Bosses.Select(ClientRuntime.ResolveBossName));
        string outcome = ClientRuntime.Text($"Outcome.{result.Outcome}", result.Outcome.ToString());

        DrawText(spriteBatch, TrimToWidth(bossName, Math.Max(0f, right - textX - 32f), 1.05f), new Vector2(textX, y + 5f), Color.White, 1.05f);
        DrawText(spriteBatch, TrimToWidth(outcome, Math.Max(0f, right - textX), 0.9f), new Vector2(textX, y + 34f), OutcomeColor(result.Outcome), 0.9f);
    }

    private void DrawPanel(SpriteBatch spriteBatch, Rectangle panel)
    {
        Texture2D pixel = TextureAssets.MagicPixel.Value;
        Color background = new Color(15, 22, 34) * (0.94f * Opacity);
        Color border = new Color(78, 144, 190) * Opacity;
        spriteBatch.Draw(pixel, panel, background);
        spriteBatch.Draw(pixel, new Rectangle(panel.X, panel.Y, panel.Width, 2), border);
        spriteBatch.Draw(pixel, new Rectangle(panel.X, panel.Bottom - 2, panel.Width, 2), border);
        spriteBatch.Draw(pixel, new Rectangle(panel.X, panel.Y, 2, panel.Height), border);
        spriteBatch.Draw(pixel, new Rectangle(panel.Right - 2, panel.Y, 2, panel.Height), border);
    }

    private void DrawScrollBar(SpriteBatch spriteBatch, Rectangle panel)
    {
        if (_maximumScrollOffset <= 0f || _contentHeight <= 0f)
            return;

        Texture2D pixel = TextureAssets.MagicPixel.Value;
        float trackHeight = Math.Max(1f, panel.Height - Margin * 2f);
        float thumbHeight = Math.Max(24f, trackHeight * Math.Min(1f, DesiredHeight / _contentHeight));
        float travel = Math.Max(0f, trackHeight - thumbHeight);
        float progress = _maximumScrollOffset <= 0f ? 0f : _scrollOffset / _maximumScrollOffset;
        int trackX = panel.Right - 9;
        int trackY = panel.Y + (int)Margin;
        int thumbY = trackY + (int)Math.Round(travel * progress);

        spriteBatch.Draw(pixel, new Rectangle(trackX, trackY, 3, (int)trackHeight), new Color(45, 65, 84) * Opacity);
        spriteBatch.Draw(pixel, new Rectangle(trackX - 1, thumbY, 5, Math.Max(1, (int)thumbHeight)), new Color(112, 185, 225) * Opacity);
    }

    private static bool IsFullyVisible(float y, float height, float top, float bottom)
        => y >= top && y + height <= bottom;

    private void DrawLine(SpriteBatch spriteBatch, RenderLine line, float x, float right, float y, float scale)
    {
        float left = x + line.Indent * 20f;
        float totalWidth = right - left;
        if (totalWidth <= 0f)
            return;

        string leftText = line.Indent > 0 ? $"↳ {line.Left}" : line.Left;
        string rightText = TrimToWidth(line.Right, Math.Max(0f, totalWidth * 0.55f), scale);
        float rightWidth = Measure(rightText, scale);
        float gap = rightWidth > 0f ? 18f : 0f;
        float leftWidth = Math.Max(0f, totalWidth - rightWidth - gap);
        string fittedLeft = TrimToWidth(leftText, leftWidth, scale);

        if (!string.IsNullOrEmpty(fittedLeft))
            DrawText(spriteBatch, fittedLeft, new Vector2(left, y), line.Color, scale);
        if (!string.IsNullOrEmpty(rightText))
            DrawText(spriteBatch, rightText, new Vector2(right - rightWidth, y), line.Color, scale);
    }

    private void DrawText(SpriteBatch spriteBatch, string text, Vector2 position, Color color, float scale)
        => Utils.DrawBorderString(spriteBatch, text, position, color * Opacity, scale);

    private static float Measure(string text, float scale) => FontAssets.MouseText.Value.MeasureString(text).X * scale;

    private static string TrimToWidth(string text, float width, float scale)
    {
        if (string.IsNullOrEmpty(text) || width <= 0f)
            return string.Empty;
        if (Measure(text, scale) <= width)
            return text;

        const string ellipsis = "…";
        if (Measure(ellipsis, scale) > width)
            return string.Empty;

        while (text.Length > 0 && Measure(text + ellipsis, scale) > width)
            text = text[..^1];
        return text + ellipsis;
    }

    private void DrawNpcPortrait(SpriteBatch spriteBatch, int npcType, Rectangle bounds)
    {
        if (npcType <= NPCID.None || npcType >= NPCLoader.NPCCount)
            return;

        try
        {
            Main.instance.LoadNPC(npcType);
            Texture2D texture = TextureAssets.Npc[npcType].Value;
            int frameCount = Math.Max(1, Main.npcFrameCount[npcType]);
            Rectangle frame = new(0, 0, texture.Width, Math.Max(1, texture.Height / frameCount));
            float scale = Math.Min(bounds.Width / (float)frame.Width, bounds.Height / (float)frame.Height);
            Vector2 center = bounds.Center.ToVector2();
            spriteBatch.Draw(texture, center, frame, Color.White * Opacity, 0f, frame.Size() / 2f, scale, SpriteEffects.None, 0f);
        }
        catch
        {
            // A malformed or intentionally headless NPC texture should not break the result panel.
        }
    }

    private static string ResolveRootName(DamageRootKey key) => key.Kind switch
    {
        DamageRootKind.Item when key.PrimaryType > ItemID.None && key.PrimaryType < ItemLoader.ItemCount => Lang.GetItemNameValue(key.PrimaryType),
        DamageRootKind.Buff when key.PrimaryType >= 0 && key.PrimaryType < BuffLoader.BuffCount => Lang.GetBuffName(key.PrimaryType),
        DamageRootKind.Mount => ClientRuntime.Text("UI.MountSource", "Mount #{0}", key.PrimaryType),
        DamageRootKind.ProjectileFallback => ResolveProjectileName(key.PrimaryType),
        DamageRootKind.Unclassified => ClientRuntime.Text("UI.Unclassified", "Unclassified damage"),
        _ => ClientRuntime.Text("UI.UnknownSource", "Unknown source")
    };

    private static string ResolveLeafName(DamageLeafKey key) => key.Kind switch
    {
        DamageLeafKind.ItemBody => ClientRuntime.Text("UI.WeaponBody", "Weapon body"),
        DamageLeafKind.Projectile => ResolveProjectileName(key.Type),
        _ => ClientRuntime.Text("UI.UnknownSource", "Unknown source")
    };

    private static string ResolveProjectileName(int type)
    {
        if (type >= ProjectileID.None && type < ProjectileLoader.ProjectileCount)
            return Lang.GetProjectileName(type).Value;
        return ClientRuntime.Text("UI.ProjectileSource", "Projectile #{0}", type);
    }

    private static string FormatDamage(long damage) => damage.ToString("N0", CultureInfo.CurrentCulture);

    private static string Percent(long part, long whole)
        => whole <= 0 ? "0.0%" : (part / (double)whole).ToString("P1", CultureInfo.CurrentCulture);

    private static Color OutcomeColor(EncounterOutcome outcome) => outcome switch
    {
        EncounterOutcome.Victory => new Color(128, 255, 160),
        EncounterOutcome.Defeat => new Color(255, 125, 125),
        EncounterOutcome.Escaped => new Color(255, 205, 120),
        _ => new Color(190, 205, 225)
    };

    private sealed record SourceBlock(
        SourceRootSnapshot Root,
        SourceLeafSnapshot? ItemBody,
        List<SourceLeafSnapshot> Projectiles);

    private enum SourceToggleKind
    {
        None,
        RootList,
        RootChildren,
        ProjectileList
    }

    private readonly record struct SourceToggleHitbox(
        Rectangle Bounds,
        DamageRootKey? RootKey,
        SourceToggleKind Kind);

    private readonly record struct RenderLine(
        string Left,
        string Right,
        int Indent,
        Color Color,
        SourceToggleKind ToggleKind = SourceToggleKind.None,
        DamageRootKey? ToggleRoot = null);
}
