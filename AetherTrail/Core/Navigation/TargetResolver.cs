namespace AetherTrail;

public static class TargetResolver
{
    public static bool TryGetTarget(out NavigationTarget target)
    {
        if (MapFlagService.TryGetFlagTarget(out var flagPosition, out uint flagTerritoryId))
        {
            uint currentTerritoryId = Plugin.ClientState.TerritoryType;

            if (flagTerritoryId != currentTerritoryId &&
                !NavigationManager.TryGetTransitionToward(currentTerritoryId, flagTerritoryId, out flagPosition))
            {
                target = new NavigationTarget
                {
                    Type = NavigationTargetType.None,
                    Label = "None"
                };

                return false;
            }

            target = new NavigationTarget
            {
                Type = NavigationTargetType.MapFlag,
                Position = flagPosition,
                Label = "Map Flag"
            };

            return true;
        }

        if (QuestTargetService.TryGetQuestTarget(out var questPosition, out uint questTerritoryId))
        {
            uint currentTerritoryId = Plugin.ClientState.TerritoryType;

            if (questTerritoryId != currentTerritoryId &&
                !NavigationManager.TryGetTransitionToward(currentTerritoryId, questTerritoryId, out questPosition))
            {
                target = new NavigationTarget
                {
                    Type = NavigationTargetType.None,
                    Label = "None"
                };

                return false;
            }

            target = new NavigationTarget
            {
                Type = NavigationTargetType.Quest,
                Position = questPosition,
                Label = "Quest"
            };

            return true;
        }

        target = new NavigationTarget
        {
            Type = NavigationTargetType.None,
            Label = "None"
        };

        return false;
    }
}
