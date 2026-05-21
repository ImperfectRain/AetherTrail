namespace AetherTrail;

public readonly record struct MapTransformSnapshot(
    uint TerritoryId,
    uint MapId,
    float SizeFactor,
    float OffsetX,
    float OffsetY,
    string TexturePath);
