namespace DocHub.Integrations.Embeddings;

/// <summary>Vector helpers shared by every provider implementation.</summary>
internal static class EmbeddingVector
{
    /// <summary>
    /// Scales a vector to unit length, in place.
    ///
    /// Every provider normalises before returning, so stored vectors are
    /// directly comparable no matter which model produced them, and cosine
    /// distance in Postgres reduces to a dot product. A zero vector is left
    /// alone — there is no meaningful direction to preserve.
    /// </summary>
    public static float[] Normalize(float[] vector)
    {
        double sumOfSquares = 0;
        foreach (var component in vector)
            sumOfSquares += component * component;

        if (sumOfSquares <= 0) return vector;

        var magnitude = Math.Sqrt(sumOfSquares);
        for (var i = 0; i < vector.Length; i++)
            vector[i] = (float)(vector[i] / magnitude);

        return vector;
    }
}
