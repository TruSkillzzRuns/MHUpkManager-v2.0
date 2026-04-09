namespace MHUpkManager.MeshImporter;

internal sealed class UE3IndexBuilder
{
    public ushort[] Build(NeutralSection section, int baseVertexIndex)
    {
        ushort[] indices = new ushort[section.Indices.Count];
        for (int i = 0; i < section.Indices.Count; i += 3)
        {
            if (i + 2 >= section.Indices.Count)
                throw new InvalidOperationException("UE3 skeletal mesh sections must contain complete triangles.");

            int i0 = checked(baseVertexIndex + section.Indices[i]);
            int i1 = checked(baseVertexIndex + section.Indices[i + 2]);
            int i2 = checked(baseVertexIndex + section.Indices[i + 1]);
            if (i0 > ushort.MaxValue || i1 > ushort.MaxValue || i2 > ushort.MaxValue)
                throw new InvalidOperationException("UE3 skeletal mesh index buffer overflowed UInt16.");

            indices[i] = (ushort)i0;
            indices[i + 1] = (ushort)i1;
            indices[i + 2] = (ushort)i2;
        }

        return indices;
    }
}
