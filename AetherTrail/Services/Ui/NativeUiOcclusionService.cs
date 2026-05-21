using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AetherTrail;

public static unsafe class NativeUiOcclusionService
{
    private static readonly string[] DefaultAddonNames =
    {
        "AreaMap",
        "ScenarioTree",
        "ToDoList",
        "ChatLog",
        "InventoryGrid",
        "Character",
        "SelectString",
        "Talk",
        "Journal",
        "ContentsFinder",
        "RecipeNote",
        "ItemDetail",
        "ActionMenu",
        "GatheringNote",
        "Teleport",
        "ContentsInfo"
    };

    private static readonly List<(Vector2 Min, Vector2 Max)> Rects = new();

    public static void BeginFrame()
    {
        Rects.Clear();

        foreach (string addonName in DefaultAddonNames)
            TryAddAddonRect(addonName);

        AddDalamudWindowRectsFallback();
    }

    public static bool Contains(Vector2 point)
    {
        foreach (var rect in Rects)
        {
            if (point.X >= rect.Min.X &&
                point.X <= rect.Max.X &&
                point.Y >= rect.Min.Y &&
                point.Y <= rect.Max.Y)
            {
                return true;
            }
        }

        return false;
    }

    private static void TryAddAddonRect(string addonName)
    {
        try
        {
            var addonPtr = Plugin.GameGui.GetAddonByName(addonName);

            if (addonPtr.IsNull)
                return;

            var addon = (AtkUnitBase*)addonPtr.Address;

            if (addon == null)
                return;

            if (!addon->IsVisible)
                return;

            float scale = addon->Scale;
            float x = addon->X;
            float y = addon->Y;
            if (addon->RootNode == null)
                return;

            float width = addon->RootNode->Width * scale;
            float height = addon->RootNode->Height * scale;

            if (width <= 0 || height <= 0)
                return;

            Vector2 min = new(x, y);
            Vector2 max = new(x + width, y + height);

            Rects.Add((min, max));
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, $"Failed to read addon rect for {addonName}");
        }
    }

    private static void AddDalamudWindowRectsFallback()
    {
       
    }
}
