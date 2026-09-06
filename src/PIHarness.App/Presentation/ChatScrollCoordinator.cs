// Created: 2026-09-06
// Function: Track whether the detailed conversation should follow its latest item.
// Purpose: Keep layout updates from overriding a user's explicit history-reading intent.

namespace PIHarness.App.Presentation;

public sealed class ChatScrollCoordinator
{
    private readonly double _bottomThreshold;

    public ChatScrollCoordinator(double bottomThreshold)
    {
        if (bottomThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bottomThreshold));
        }

        _bottomThreshold = bottomThreshold;
    }

    public bool IsFollowingLatest { get; private set; } = true;

    public bool ShouldFollowExtentChange => IsFollowingLatest;

    public void Reset()
    {
        IsFollowingLatest = true;
    }

    public void OnUserWheel(int nDelta)
    {
        if (nDelta > 0)
        {
            IsFollowingLatest = false;
        }
    }

    public void OnViewportPositionChanged(double distanceFromBottom)
    {
        IsFollowingLatest = distanceFromBottom <= _bottomThreshold;
    }

    public void ReturnToLatest()
    {
        IsFollowingLatest = true;
    }
}
