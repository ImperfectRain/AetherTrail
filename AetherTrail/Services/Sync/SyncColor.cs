using System;
using System.Numerics;

namespace AetherTrail;

public struct SyncColor
{
    public float R { get; set; }
    public float G { get; set; }
    public float B { get; set; }
    public float A { get; set; }

    public static SyncColor Default => FromVector4(new Vector4(1.0f, 0.2f, 0.85f, 1.0f));

    public static SyncColor FromVector4(Vector4 color)
    {
        return new SyncColor
        {
            R = Clamp(color.X),
            G = Clamp(color.Y),
            B = Clamp(color.Z),
            A = Clamp(color.W)
        };
    }

    public Vector4 ToVector4(float alphaMultiplier = 1.0f)
    {
        if (this.A <= 0f && this.R <= 0f && this.G <= 0f && this.B <= 0f)
            return Default.ToVector4(alphaMultiplier);

        return new Vector4(
            Clamp(this.R),
            Clamp(this.G),
            Clamp(this.B),
            Clamp(this.A * alphaMultiplier)
        );
    }

    private static float Clamp(float value)
    {
        return Math.Clamp(value, 0f, 1f);
    }
}
