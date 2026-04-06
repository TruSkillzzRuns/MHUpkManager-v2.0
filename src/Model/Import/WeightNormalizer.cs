namespace MHUpkManager.Model.Import;

internal sealed class WeightNormalizer
{
    public IReadOnlyList<IReadOnlyList<NormalizedWeight>> Normalize(IReadOnlyList<IReadOnlyList<RemappedWeight>> weightsByVertex)
    {
        List<IReadOnlyList<NormalizedWeight>> normalized = new(weightsByVertex.Count);
        foreach (IReadOnlyList<RemappedWeight> weights in weightsByVertex)
            normalized.Add(NormalizeVertex(weights));

        return normalized;
    }

    private static IReadOnlyList<NormalizedWeight> NormalizeVertex(IReadOnlyList<RemappedWeight> weights)
    {
        Dictionary<int, float> combined = [];
        foreach (RemappedWeight weight in weights)
        {
            if (!combined.TryAdd(weight.BoneIndex, weight.Weight))
                combined[weight.BoneIndex] += weight.Weight;
        }

        List<KeyValuePair<int, float>> ordered = [.. combined
            .Where(static x => x.Value > 0.0f)
            .OrderByDescending(static x => x.Value)
            .ThenBy(static x => x.Key)
            .Take(4)];

        if (ordered.Count == 0)
            ordered.Add(new KeyValuePair<int, float>(0, 1.0f));

        float total = ordered.Sum(static x => x.Value);
        if (total <= 0.0f)
            total = 1.0f;

        float[] normalized = ordered.Select(x => x.Value / total).ToArray();
        byte[] quantized = QuantizeTo255(normalized);

        List<NormalizedWeight> result = new(4);
        for (int i = 0; i < ordered.Count; i++)
            result.Add(new NormalizedWeight(ordered[i].Key, quantized[i]));

        while (result.Count < 4)
            result.Add(new NormalizedWeight(0, 0));

        return result;
    }

    private static byte[] QuantizeTo255(float[] values)
    {
        int[] floors = new int[values.Length];
        float[] fractions = new float[values.Length];
        int sum = 0;

        for (int i = 0; i < values.Length; i++)
        {
            float scaled = Math.Clamp(values[i], 0.0f, 1.0f) * 255.0f;
            floors[i] = (int)MathF.Floor(scaled);
            fractions[i] = scaled - floors[i];
            sum += floors[i];
        }

        int remaining = 255 - sum;
        foreach (int index in Enumerable.Range(0, values.Length).OrderByDescending(i => fractions[i]).ThenBy(i => i))
        {
            if (remaining <= 0)
                break;

            floors[index]++;
            remaining--;
        }

        return [.. floors.Select(static v => (byte)v)];
    }
}
