namespace AetherTrail;

public static class TargetResolver
{
    public static bool TryGetTarget(out NavigationTarget target)
    {
        if (MapFlagService.TryGetFlagPosition(out var flagPosition))
        {
            target = new NavigationTarget
            {
                Type = NavigationTargetType.MapFlag,
                Position = flagPosition,
                Label = "Map Flag"
            };

            return true;
        }

        if (QuestTargetService.TryGetQuestTarget(out var questPosition))
        {
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
