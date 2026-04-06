namespace MHUpkManager.MeshImporter;

internal sealed class UE3IndexBuilder
{
    public ushort[] Build(NeutralSection section, int baseVertexIndex)
    {
        ushort[] indices = new ushort[section.Indices.Count];
        for (int i = 0; i < section.Indices.Count; i++)
        {
            int value = checked(baseVertexIndex + section.Indices[i]);
            if (value > ushort.MaxValue)
                throw new InvalidOperationException("UE3 skeletal mesh index buffer overflowed UInt16.");

            indices[i] = (ushort)value;
        }

        return indices;
    }
}
