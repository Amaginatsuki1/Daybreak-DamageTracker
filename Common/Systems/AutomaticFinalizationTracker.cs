namespace DaybreakDamageTracker.Common.Systems;

internal enum AutomaticFinalizationDecision
{
    None,
    Pending,
    Victory
}

internal static class AutomaticFinalizationPolicy
{
    // Segmented bosses commonly mark every segment as a boss. realLife is the stable
    // distinction available after NPC initialization: roots/controllers have no parent,
    // while bodies and tails point back to their controlling head.
    // A few cataloged bosses never set npc.boss on their representative form. Such a
    // form still qualifies when its type is already known to be a controller, but new
    // types are only learned from boss-flagged roots so spawned adds and dependents
    // cannot promote themselves into the controller set.
    public static bool IsControllerNpc(
        IReadOnlySet<int> knownControllerTypes,
        int npcType,
        bool isBoss,
        int npcSlot,
        int realLife)
        => (realLife < 0 || realLife == npcSlot) &&
           (isBoss || knownControllerTypes.Contains(npcType));

    public static bool IsKnownControllerType(IReadOnlySet<int> controllerNpcTypes, int npcType)
        => controllerNpcTypes.Contains(npcType);

    public static AutomaticFinalizationDecision ResolveBoundary(
        bool hasControllerKillCandidate,
        bool foreignEncounterStarted)
        => !hasControllerKillCandidate
            ? AutomaticFinalizationDecision.None
            : foreignEncounterStarted
                ? AutomaticFinalizationDecision.Victory
                : AutomaticFinalizationDecision.Pending;

}

/// <summary>
/// Tracks an OnKill-backed final-controller candidate across authoritative NPC scans.
/// A controller kill is not immediately final: a surviving or replacement controller
/// cancels it. Dependent body/tail cleanup may omit OnKill without erasing the candidate.
/// </summary>
internal sealed class AutomaticFinalizationTracker<T> where T : notnull
{
    private const int MinimumConfirmationTicks = 2;

    private HashSet<T> _previousControllers = [];
    private readonly HashSet<T> _retiredControllers = [];
    private readonly HashSet<T> _candidateControllers = [];
    private readonly HashSet<T> _lastConfirmedControllers = [];
    private readonly HashSet<T> _overlapPredecessors = [];
    private readonly HashSet<T> _overlapSuccessors = [];
    private bool _hasCandidate;
    private bool _controllerEpochContaminated;
    private long _participantFreeSinceTick = -1;
    private long _overlapObservedTick = -1;
    private bool _overlapExpired;

    public bool HasCandidate => _hasCandidate;
    public IReadOnlySet<T> CandidateControllerInstances => _candidateControllers;
    public IReadOnlySet<T> LastConfirmedControllerInstances => _lastConfirmedControllers;
    public bool HasUnresolvedControllerDisappearance => _controllerEpochContaminated;
    public bool LastRefreshAcceptedOverlapHandoff { get; private set; }

    public AutomaticFinalizationDecision Refresh(
        IReadOnlySet<T> activeParticipants,
        IReadOnlySet<T> activeControllers,
        IReadOnlySet<T> killedSincePreviousScan,
        IReadOnlySet<T> killedControllersSincePreviousScan,
        long nowTick,
        int configuredConfirmationTicks,
        IReadOnlySet<T>? spawnedAutomaticFinalInstancesSincePreviousScan = null)
    {
        LastRefreshAcceptedOverlapHandoff = false;
        _lastConfirmedControllers.Clear();
        _retiredControllers.UnionWith(killedControllersSincePreviousScan);
        _retiredControllers.RemoveWhere(instance => !activeControllers.Contains(instance));

        HashSet<T> liveControllers = activeControllers
            .Where(instance => !_retiredControllers.Contains(instance))
            .ToHashSet();

        bool hasUnaccountedSpawn = spawnedAutomaticFinalInstancesSincePreviousScan?.Any(instance =>
            !activeParticipants.Contains(instance) &&
            !killedSincePreviousScan.Contains(instance)) == true;
        int handoffTicks = Math.Max(MinimumConfirmationTicks, configuredConfirmationTicks);
        bool hadPreviousController = _previousControllers.Count > 0;
        HashSet<T> survivingPreviousControllers = _previousControllers
            .Where(liveControllers.Contains)
            .ToHashSet();
        HashSet<T> newControllers = liveControllers
            .Where(instance => !_previousControllers.Contains(instance))
            .ToHashSet();
        bool hasSurvivingPreviousController = survivingPreviousControllers.Count > 0;
        bool hasNewController = newControllers.Count > 0;

        // Some phase transitions create successor roots before directly deactivating the
        // predecessor on the following frame. Remember that short overlap so the old root's
        // non-OnKill disappearance is not confused with one member escaping from a stable
        // multi-controller fight. A long-lived overlap expires and remains conservative.
        if (_overlapObservedTick >= 0 && nowTick - _overlapObservedTick > handoffTicks)
            _overlapExpired = true;
        if (_overlapObservedTick < 0 &&
            hadPreviousController &&
            hasSurvivingPreviousController &&
            hasNewController)
        {
            _overlapPredecessors.UnionWith(_previousControllers);
            _overlapSuccessors.UnionWith(newControllers);
            _overlapObservedTick = nowTick;
            _overlapExpired = false;
        }
        else if (_overlapObservedTick >= 0 && !_overlapExpired &&
                 _overlapPredecessors.Any(liveControllers.Contains))
        {
            foreach (T controller in newControllers)
            {
                if (!_overlapPredecessors.Contains(controller))
                    _overlapSuccessors.Add(controller);
            }
        }

        bool directPhaseReplacement = hadPreviousController &&
            !hasSurvivingPreviousController &&
            hasNewController;
        bool overlapPredecessorsGone = _overlapObservedTick >= 0 &&
            !_overlapPredecessors.Any(liveControllers.Contains);
        bool overlapSuccessorsOwnLiveSet = liveControllers.Count > 0 &&
            liveControllers.All(_overlapSuccessors.Contains);
        bool overlapPhaseReplacement = overlapPredecessorsGone &&
            overlapSuccessorsOwnLiveSet &&
            !_overlapExpired;
        bool phaseReplacement = directPhaseReplacement || overlapPhaseReplacement;
        LastRefreshAcceptedOverlapHandoff = overlapPhaseReplacement;
        bool previousControllerDisappearedWithoutKill = _previousControllers.Any(previous =>
            !liveControllers.Contains(previous) &&
            !killedSincePreviousScan.Contains(previous));
        if (phaseReplacement)
            _controllerEpochContaminated = false;
        else if (previousControllerDisappearedWithoutKill || hasUnaccountedSpawn)
            _controllerEpochContaminated = true;

        bool allPreviousControllersKilled = _previousControllers.Count > 0 &&
            _previousControllers.All(killedSincePreviousScan.Contains);
        bool unobservedControllerKilled = _previousControllers.Count == 0 &&
            killedControllersSincePreviousScan.Count > 0 &&
            spawnedAutomaticFinalInstancesSincePreviousScan is { Count: > 0 } &&
            killedControllersSincePreviousScan.All(spawnedAutomaticFinalInstancesSincePreviousScan.Contains);
        if (!_controllerEpochContaminated &&
            !hasUnaccountedSpawn &&
            (allPreviousControllersKilled || unobservedControllerKilled))
        {
            _hasCandidate = true;
            _candidateControllers.Clear();
            if (allPreviousControllersKilled)
                _candidateControllers.UnionWith(_previousControllers);
            else
                _candidateControllers.UnionWith(killedControllersSincePreviousScan);
        }

        // Do not let the exact NPC instance that already fired OnKill cancel its own
        // candidate if a mod keeps it active for a short death animation. Any genuinely
        // live sibling or successor controller does cancel the candidate.
        if (liveControllers.Count > 0)
        {
            _hasCandidate = false;
            _candidateControllers.Clear();
            _participantFreeSinceTick = -1;
        }

        AutomaticFinalizationDecision decision = AutomaticFinalizationDecision.None;
        if (_hasCandidate)
        {
            if (activeParticipants.Count > 0)
            {
                // A lingering or briefly reappearing dependent segment is cleanup, not a
                // successor phase. Keep the kill evidence but restart the empty window.
                _participantFreeSinceTick = -1;
                decision = AutomaticFinalizationDecision.Pending;
            }
            else
            {
                if (_participantFreeSinceTick < 0)
                    _participantFreeSinceTick = nowTick;

                decision = nowTick - _participantFreeSinceTick >= handoffTicks
                    ? AutomaticFinalizationDecision.Victory
                    : AutomaticFinalizationDecision.Pending;
            }
        }

        _previousControllers = liveControllers;
        if (_overlapObservedTick >= 0 &&
            (!_overlapPredecessors.Any(liveControllers.Contains) ||
             !_overlapSuccessors.Any(liveControllers.Contains)))
        {
            ClearOverlapHandoff();
        }
        if (decision == AutomaticFinalizationDecision.Victory)
        {
            _lastConfirmedControllers.UnionWith(_candidateControllers);
            _hasCandidate = false;
            _candidateControllers.Clear();
            _participantFreeSinceTick = -1;
        }

        return decision;
    }

    public void Reset()
    {
        _previousControllers.Clear();
        _retiredControllers.Clear();
        _candidateControllers.Clear();
        _lastConfirmedControllers.Clear();
        ClearOverlapHandoff();
        _hasCandidate = false;
        _controllerEpochContaminated = false;
        _participantFreeSinceTick = -1;
        LastRefreshAcceptedOverlapHandoff = false;
    }

    private void ClearOverlapHandoff()
    {
        _overlapPredecessors.Clear();
        _overlapSuccessors.Clear();
        _overlapObservedTick = -1;
        _overlapExpired = false;
    }
}
