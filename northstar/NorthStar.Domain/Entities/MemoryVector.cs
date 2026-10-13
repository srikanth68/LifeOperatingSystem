namespace NorthStar.Domain.Entities;

// A memory's embedding. ContentKey and Model record what it was computed from, so a
// memory whose text changed, or a switch to another model, is noticed and re-embedded
// rather than matched against a stale vector.
public class MemoryVector
{
    public Guid MemoryId { get; set; }
    public string Model { get; set; } = "";
    public string ContentKey { get; set; } = "";
    public int Dimensions { get; set; }
    public byte[] Vector { get; set; } = [];   // float32, little-endian, L2-normalised
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public static byte[] Pack(float[] v)
    {
        var norm = MathF.Sqrt(v.Sum(x => x * x));
        var scale = norm > 0 ? 1f / norm : 0f;
        var bytes = new byte[v.Length * sizeof(float)];
        for (var i = 0; i < v.Length; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * sizeof(float)), v[i] * scale);
        return bytes;
    }

    public static float[] Unpack(byte[] bytes)
    {
        var v = new float[bytes.Length / sizeof(float)];
        for (var i = 0; i < v.Length; i++) v[i] = BitConverter.ToSingle(bytes, i * sizeof(float));
        return v;
    }

    // Both sides normalised, so the dot product is the cosine similarity.
    public static float Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;
        float dot = 0;
        for (var i = 0; i < a.Length; i++) dot += a[i] * b[i];
        return dot;
    }

    public static float[] Normalise(float[] v) => Unpack(Pack(v));
}
