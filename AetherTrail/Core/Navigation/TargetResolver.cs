namespace AetherTrail;

public static class TargetResolver
{
    public static bool TryGetTarget(out NavigationTarget target)
    {
        if (MapFlagService.TryGetFlagTarget(out var flagPosition, out uint flagTerritoryId))
        {
            return TryBuildTarget(
                NavigationTargetType.MapFlag,
                "Map Flag",
                flagPosition,
                flagTerritoryId,
                out target
            );
        }

        if (QuestTargetService.TryGetQuestTarget(out var questPosition, out uint questTerritoryId))
        {
            return TryBuildTarget(
                NavigationTargetType.Quest,
                "Quest",
                questPosition,
                questTerritoryId,
                out target
            );
        }

        target = EmptyTarget();
        return false;
    }

    private static bool TryBuildTarget(
        NavigationTargetType type,
        string label,
        System.Numerics.Vector3 originalPosition,
        uint originalTerritoryId,
        out NavigationTarget target)
    {
        uint currentTerritoryId = Plugin.ClientState.TerritoryType;

        if (originalTerritoryId == 0 || originalTerritoryId == currentTerritoryId)
        {
            target = new NavigationTarget
            {
                Type = type,
                Position = originalPosition,
                TerritoryId = currentTerritoryId,
                OriginalPosition = originalPosition,
                OriginalTerritoryId = originalTerritoryId,
                IsUsingTerritoryTransition = false,
                TransitionTargetTerritoryId = 0,
                Label = label
            };

            return true;
        }

        if (NavigationManager.TryGetTransitionToward(
                currentTerritoryId,
                originalTerritoryId,
                out var transitionPosition))
        {
            target = new NavigationTarget
            {
                Type = type,
                Position = transitionPosition,
                TerritoryId = currentTerritoryId,
                OriginalPosition = originalPosition,
                OriginalTerritoryId = originalTerritoryId,
                IsUsingTerritoryTransition = true,
                TransitionTargetTerritoryId = originalTerritoryId,
                Label = $"{label} via territory transition"
            };

            return true;
        }

        target = new NavigationTarget
        {
            Type = type,
            Position = originalPosition,
            TerritoryId = currentTerritoryId,
            OriginalPosition = originalPosition,
            OriginalTerritoryId = originalTerritoryId,
            IsUsingTerritoryTransition = false,
            TransitionTargetTerritoryId = originalTerritoryId,
            Label = $"{label} in unknown territory"
        };

        return true;
    }

    private static NavigationTarget EmptyTarget()
    {
        return new NavigationTarget
        {
            Type = NavigationTargetType.None,
            Label = "None"
        };
    }
}
