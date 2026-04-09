namespace MHUpkManager.MeshImporter;

internal sealed class UE3IndexBuilder
{
    public ushort[] Build(NeutralSection section, int baseVertexIndex, IReadOnlyList<int> localIndexRemap)
    {
        ushort[] indices = new ushort[section.Indices.Count];
        for (int i = 0; i < section.Indices.Count; i += 3)
        {
            if (i + 2 >= section.Indices.Count)
                throw new InvalidOperationException("UE3 skeletal mesh sections must contain complete triangles.");

            int local0 = localIndexRemap[section.Indices[i]];
            int local1 = localIndexRemap[section.Indices[i + 1]];
            int local2 = localIndexRemap[section.Indices[i + 2]];

            int i0 = checked(baseVertexIndex + local0);
            int i1 = checked(baseVertexIndex + local1);
            int i2 = checked(baseVertexIndex + local2);
            if (i0 > ushort.MaxValue || i1 > ushort.MaxValue || i2 > ushort.MaxValue)
                throw new InvalidOperationException("UE3 skeletal mesh index buffer overflowed UInt16.");

            indices[i] = (ushort)i0;
            indices[i + 1] = (ushort)i1;
            indices[i + 2] = (ushort)i2;
        }

        return indices;
    }
}
