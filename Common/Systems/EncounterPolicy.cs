using DaybreakDamageTracker.Common.Data;

namespace DaybreakDamageTracker.Common.Systems;

internal readonly record struct EncounterBoundaryFacts(
    bool HasVictorySignal,
    bool PartyWipeObserved,
    bool ContinueAfterPartyWipe,
    bool HasNoActivePlayers,
    bool LastHadLivingEngagedPlayer,
    bool ForeignEncounterStarted,
    bool GenericFallbackExpired);

internal readonly record struct UnresolvedBossFacts(
    bool PartyWipeObserved,
    bool ContinueAfterPartyWipe,
    bool HasNoActivePlayers,
    bool LastHadLivingEngagedPlayer,
    bool ObservedNonKillDisappearance,
    bool AllowDisappearanceBoundary,
    bool ForeignEncounterStarted,
    bool GenericFallbackExpired,
    bool OtherBossAlreadyResolved,
    bool AllowSiblingBoundary);

internal readonly record struct InactiveSiblingGroupFacts(
    bool EncounterStarted,
    bool AnotherGroupActive,
    bool GroupHasActiveParticipant,
    bool InactiveGraceElapsed,
    bool HasControllerKillCandidate,
    UnresolvedBossFacts Boundary);

internal static class EncounterPolicy
{
    public static bool CanRecordDamage(bool targetEligible, int actualDamage, bool hasUniqueBossOwner)
        => targetEligible && actualDamage > 0 && hasUniqueBossOwner;

    public static bool ShouldReconcileDamagePath(
        long lastTick,
        long currentTick,
        long lastSpawnGeneration,
        long currentSpawnGeneration)
        => lastTick != currentTick || lastSpawnGeneration != currentSpawnGeneration;

    public static bool AllCurrentParticipantsDead<T>(
        IEnumerable<T> participants,
        Func<T, bool> isCurrent,
        Func<T, bool> isDead)
    {
        bool anyCurrent = false;
        foreach (T participant in participants)
        {
            if (!isCurrent(participant))
                continue;
            anyCurrent = true;
            if (!isDead(participant))
                return false;
        }
        return anyCurrent;
    }

    public static bool HasNoCurrentParticipants<T>(
        IReadOnlyCollection<T> participants,
        Func<T, bool> isCurrent)
        => participants.Count > 0 && !participants.Any(isCurrent);

    public static bool ShouldRecoverPartyWipe(
        bool partyWipeObserved,
        bool continueAfterPartyWipe,
        bool livingPlayerEngaged)
        => partyWipeObserved && continueAfterPartyWipe && livingPlayerEngaged;

    public static bool CanAssociateNpcTargetWithGroup(
        bool alreadyKnownParticipant,
        bool targetWithinEncounterWindow)
        => alreadyKnownParticipant || targetWithinEncounterWindow;

    public static void ReleaseAbsentResolvedBossKeys(
        HashSet<string> heldKeys,
        IEnumerable<string> activeKeys)
    {
        HashSet<string> active = activeKeys.ToHashSet(StringComparer.Ordinal);
        heldKeys.RemoveWhere(key => !active.Contains(key));
    }

    public static bool ShouldStartNewEncounter(
        bool hasAnyActiveBoss,
        bool hasActiveCurrentParticipant,
        bool hasRelatedActiveBoss)
        => hasAnyActiveBoss && !hasActiveCurrentParticipant && !hasRelatedActiveBoss;

    public static bool CanUseEntryDownedSignal(string bossKey, string encounterGroupKey)
        => string.Equals(bossKey, encounterGroupKey, StringComparison.Ordinal);

    public static bool CanUseDisappearanceBoundary(
        string bossKey,
        string encounterGroupKey,
        bool allowGroupedDisappearanceBoundary)
        => allowGroupedDisappearanceBoundary || CanUseEntryDownedSignal(bossKey, encounterGroupKey);

    public static bool CanUseAutomaticBodyFinal(
        string bossKey,
        string encounterGroupKey,
        bool finishOnBodyDeath,
        bool continueAfterPartyWipe,
        bool hasDownedSignal,
        bool downedAtArm,
        bool forceWithoutDownedTransition = false)
        => finishOnBodyDeath &&
           !continueAfterPartyWipe &&
           CanUseEntryDownedSignal(bossKey, encounterGroupKey) &&
           (forceWithoutDownedTransition || !hasDownedSignal || downedAtArm);

    public static bool ShouldResolveVictory(
        bool hasActiveKeepAliveNpc,
        bool explicitFinalKill,
        bool provisionalLastParticipantKill,
        bool downedTransition)
        // Explicit final forms and first-clear downed signals are authoritative.
        // They must not be held open by a lingering controller, limb, or add.
        => explicitFinalKill ||
           downedTransition ||
           (!hasActiveKeepAliveNpc && provisionalLastParticipantKill);

    public static EncounterOutcome? ResolveInactiveBoundary(EncounterBoundaryFacts facts)
    {
        // A final kill/downed signal wins a same-tick race with player death.
        if (facts.HasVictorySignal)
            return EncounterOutcome.Victory;

        if (facts.HasNoActivePlayers)
            return facts.PartyWipeObserved ? EncounterOutcome.Defeat : EncounterOutcome.Escaped;

        if (facts.PartyWipeObserved && !facts.ContinueAfterPartyWipe)
            return EncounterOutcome.Defeat;

        // A different encounter is an unambiguous boundary even when the old boss
        // disappeared while a living player was still near its last position.
        if (facts.ForeignEncounterStarted)
            return facts.PartyWipeObserved ? EncounterOutcome.Defeat : EncounterOutcome.Escaped;

        if (!facts.PartyWipeObserved && !facts.LastHadLivingEngagedPlayer)
            return EncounterOutcome.Escaped;

        if (facts.GenericFallbackExpired)
            return facts.PartyWipeObserved ? EncounterOutcome.Defeat : EncounterOutcome.Escaped;

        return null;
    }

    public static EncounterOutcome? ResolveUnresolvedBoss(UnresolvedBossFacts facts)
    {
        if (facts.HasNoActivePlayers)
            return facts.PartyWipeObserved ? EncounterOutcome.Defeat : EncounterOutcome.Escaped;

        if (facts.PartyWipeObserved)
            return facts.ContinueAfterPartyWipe ? null : EncounterOutcome.Defeat;

        // A participant that was present in the previous authoritative scan and then
        // vanished without OnKill is a real despawn/disengagement boundary. Ordinary
        // bosses may close immediately; scripted NPC-free phases and grouped gauntlets
        // opt out through their lifecycle descriptor.
        if (facts.ObservedNonKillDisappearance && facts.AllowDisappearanceBoundary)
            return EncounterOutcome.Escaped;

        if (facts.ForeignEncounterStarted || !facts.LastHadLivingEngagedPlayer)
            return EncounterOutcome.Escaped;

        // In a simultaneous encounter, a sibling's confirmed ending plus the complete
        // disappearance of this boss is a real boundary. This prevents a killed+despawned
        // pair from waiting forever. NPC-free scripted phases can opt out through
        // FinishOnBodyDeathWhenNoParticipants=false and an explicit final signal.
        if (facts.OtherBossAlreadyResolved && facts.AllowSiblingBoundary)
            return EncounterOutcome.Escaped;

        if (facts.GenericFallbackExpired)
            return EncounterOutcome.Escaped;

        return null;
    }

    public static EncounterOutcome? ResolveInactiveSiblingGroup(InactiveSiblingGroupFacts facts)
    {
        if (!facts.EncounterStarted ||
            !facts.AnotherGroupActive ||
            facts.GroupHasActiveParticipant)
        {
            return null;
        }

        // Preserve the same victory-before-wipe/escape contract as the global
        // NPC-free boundary. A real controller OnKill gets its short confirmation
        // window even while an unrelated Boss remains alive.
        if (AutomaticFinalizationPolicy.ResolveBoundary(
                facts.HasControllerKillCandidate,
                foreignEncounterStarted: false) == AutomaticFinalizationDecision.Pending)
        {
            return null;
        }

        // A simultaneous sibling can briefly have no active NPC while replacing a
        // controller or moving between phases. Give it the same synchronization window
        // as the session-wide NPC-free transition before treating disappearance/wipe as
        // a terminal boundary.
        if (!facts.InactiveGraceElapsed)
            return null;

        UnresolvedBossFacts boundary = facts.Boundary;
        if (boundary.HasNoActivePlayers)
            return boundary.PartyWipeObserved ? EncounterOutcome.Defeat : EncounterOutcome.Escaped;

        if (boundary.PartyWipeObserved)
            return boundary.ContinueAfterPartyWipe ? null : EncounterOutcome.Defeat;

        // Another Boss group is still active, so session-wide engagement, sibling,
        // foreign-encounter and generic-fallback signals cannot describe this absent
        // group. Only its own observed disappearance may close it; configured NPC-free
        // cinematics and grouped gauntlets therefore remain pending.
        if (boundary.ObservedNonKillDisappearance && boundary.AllowDisappearanceBoundary)
            return EncounterOutcome.Escaped;

        return null;
    }

    public static EncounterOutcome AggregateOutcomes(IEnumerable<EncounterOutcome> outcomes)
    {
        EncounterOutcome[] distinct = outcomes
            .Where(value => value != EncounterOutcome.Unknown)
            .Distinct()
            .ToArray();
        return distinct.Length switch
        {
            0 => EncounterOutcome.Unknown,
            1 => distinct[0],
            _ => EncounterOutcome.Mixed
        };
    }
}

internal static class PlayerIdentityPolicy
{
    public static bool CanMergeReconnect(
        string existingName,
        bool existingHasActiveConnection,
        string incomingName)
        => !existingHasActiveConnection &&
           string.Equals(existingName, incomingName, StringComparison.Ordinal);

    public static int FindReconnectCandidateIndex(
        IReadOnlyList<LogicalPlayerCandidate> candidates,
        string incomingName)
    {
        int match = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            LogicalPlayerCandidate candidate = candidates[i];
            if (string.Equals(candidate.Name, incomingName, StringComparison.Ordinal) &&
                candidate.HasActiveConnection)
            {
                // If that name is already in use, identity is ambiguous even when a
                // disconnected ledger with the same name also exists.
                return -1;
            }
            if (!CanMergeReconnect(candidate.Name, candidate.HasActiveConnection, incomingName))
                continue;
            if (match >= 0)
                return -1;
            match = i;
        }
        return match;
    }
}

internal readonly record struct LogicalPlayerCandidate(string Name, bool HasActiveConnection);
