using System.Numerics;

namespace AetherTrail;

public struct SyncVector3
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public static SyncVector3 FromVector3(Vector3 vector)
    {
        return new SyncVector3
        {
            X = vector.X,
            Y = vector.Y,
            Z = vector.Z
        };
    }

    public Vector3 ToVector3()
    {
        return new Vector3(this.X, this.Y, this.Z);
    }
}
