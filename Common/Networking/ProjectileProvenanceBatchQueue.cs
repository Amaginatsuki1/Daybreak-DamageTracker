namespace DaybreakDamageTracker.Common.Networking;

internal static class ProjectileProvenancePolicy
{
    public static bool IsValidProjectileType(int projectileType, int noneType, int projectileCount)
        => projectileType > noneType && projectileType < projectileCount;
}

internal readonly record struct RecipientBatch<TRecipient, TEntry>(
    TRecipient Recipient,
    IReadOnlyList<TEntry> Entries);

/// <summary>
/// A per-recipient, bounded queue which drains into materialized packet-sized batches.
/// The round-robin recipient order preserves unsent entries across ticks while enforcing
/// one global packet budget, so a large server cannot create an unbounded send burst.
/// </summary>
internal sealed class RecipientBatchQueue<TRecipient, TEntry>
    where TRecipient : notnull
{
    private readonly int _maximumEntriesPerRecipient;
    private readonly int _maximumBatchSize;
    private readonly int _maximumBatchesPerDrain;
    private readonly Dictionary<TRecipient, List<TEntry>> _entries = [];
    private readonly Queue<TRecipient> _recipientOrder = [];
    private readonly Dictionary<TRecipient, long> _enqueuedSequences = [];
    private readonly Dictionary<TRecipient, long> _drainedSequences = [];
    private readonly object _sync = new();

    public RecipientBatchQueue(
        int maximumEntriesPerRecipient,
        int maximumBatchSize,
        int maximumBatchesPerDrain)
    {
        if (maximumEntriesPerRecipient <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumEntriesPerRecipient));
        if (maximumBatchSize <= 0 || maximumBatchSize > maximumEntriesPerRecipient)
            throw new ArgumentOutOfRangeException(nameof(maximumBatchSize));
        if (maximumBatchesPerDrain <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBatchesPerDrain));

        _maximumEntriesPerRecipient = maximumEntriesPerRecipient;
        _maximumBatchSize = maximumBatchSize;
        _maximumBatchesPerDrain = maximumBatchesPerDrain;
    }

    public int RecipientCount
    {
        get
        {
            lock (_sync)
                return _entries.Count;
        }
    }

    public bool TryEnqueue(TRecipient recipient, TEntry entry)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(recipient, out List<TEntry>? recipientEntries))
            {
                _entries[recipient] = recipientEntries = [];
                _recipientOrder.Enqueue(recipient);
            }
            if (recipientEntries.Count >= _maximumEntriesPerRecipient)
                return false;

            recipientEntries.Add(entry);
            _enqueuedSequences.TryGetValue(recipient, out long sequence);
            _enqueuedSequences[recipient] = sequence == long.MaxValue ? long.MaxValue : sequence + 1;
            return true;
        }
    }

    public long GetEnqueuedSequence(TRecipient recipient)
    {
        lock (_sync)
            return _enqueuedSequences.GetValueOrDefault(recipient);
    }

    public bool IsDrainedThrough(TRecipient recipient, long sequence)
    {
        if (sequence <= 0)
            return true;

        lock (_sync)
            return _drainedSequences.GetValueOrDefault(recipient) >= sequence;
    }

    public List<RecipientBatch<TRecipient, TEntry>> Drain()
    {
        lock (_sync)
        {
            if (_entries.Count == 0)
                return [];

            List<RecipientBatch<TRecipient, TEntry>> batches = [];
            while (batches.Count < _maximumBatchesPerDrain && _recipientOrder.Count > 0)
            {
                TRecipient recipient = _recipientOrder.Dequeue();
                if (!_entries.TryGetValue(recipient, out List<TEntry>? recipientEntries) || recipientEntries.Count == 0)
                    continue;

                int count = Math.Min(_maximumBatchSize, recipientEntries.Count);
                TEntry[] batch = recipientEntries.GetRange(0, count).ToArray();
                recipientEntries.RemoveRange(0, count);
                batches.Add(new RecipientBatch<TRecipient, TEntry>(recipient, batch));
                _drainedSequences.TryGetValue(recipient, out long drained);
                _drainedSequences[recipient] = drained > long.MaxValue - count
                    ? long.MaxValue
                    : drained + count;

                if (recipientEntries.Count > 0)
                    _recipientOrder.Enqueue(recipient);
                else
                    _entries.Remove(recipient);
            }

            return batches;
        }
    }

    public int RemoveRecipientsWhere(Predicate<TRecipient> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        lock (_sync)
        {
            HashSet<TRecipient> removed = _entries.Keys
                .Concat(_enqueuedSequences.Keys)
                .Concat(_drainedSequences.Keys)
                .Where(recipient => predicate(recipient))
                .ToHashSet();
            if (removed.Count == 0)
                return 0;

            foreach (TRecipient recipient in removed)
            {
                _entries.Remove(recipient);
                _enqueuedSequences.Remove(recipient);
                _drainedSequences.Remove(recipient);
            }

            int queuedRecipients = _recipientOrder.Count;
            for (int i = 0; i < queuedRecipients; i++)
            {
                TRecipient recipient = _recipientOrder.Dequeue();
                if (!removed.Contains(recipient))
                    _recipientOrder.Enqueue(recipient);
            }

            return removed.Count;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
            _recipientOrder.Clear();
            _enqueuedSequences.Clear();
            _drainedSequences.Clear();
        }
    }
}
