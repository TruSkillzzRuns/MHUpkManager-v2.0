using System.Numerics;

namespace MHUpkManager.Retargeting;

internal sealed class RetargetPosePreviewService
{
    public RetargetMesh ApplyPose(RetargetMesh sourceMesh, RetargetPosePreset preset, Action<string> log = null)
    {
        if (sourceMesh == null)
            throw new ArgumentNullException(nameof(sourceMesh));

        RetargetMesh posedMesh = sourceMesh.DeepClone();
        posedMesh.RebuildBoneLookup();
        if (posedMesh.Bones.Count == 0)
            throw new InvalidOperationException("The selected retarget mesh does not contain any bones for pose preview.");

        Matrix4x4[] bindGlobals = posedMesh.Bones.Select(static bone => bone.GlobalTransform).ToArray();
        Matrix4x4[] posedGlobals = posedMesh.Bones.Select(static bone => bone.GlobalTransform).ToArray();
        Dictionary<string, int> boneIndexByName = BuildBoneIndexLookup(posedMesh.Bones);
        PoseFrame frame = PoseFrame.Build(posedMesh.Bones, boneIndexByName);

        int appliedOperations = ApplyPresetOperations(posedMesh.Bones, boneIndexByName, posedGlobals, preset, frame, log);

        for (int i = 0; i < posedMesh.Bones.Count; i++)
        {
            posedMesh.Bones[i].GlobalTransform = posedGlobals[i];
            if (posedMesh.Bones[i].ParentIndex >= 0 && posedMesh.Bones[i].ParentIndex < posedGlobals.Length &&
                Matrix4x4.Invert(posedGlobals[posedMesh.Bones[i].ParentIndex], out Matrix4x4 parentInverse))
            {
                posedMesh.Bones[i].LocalTransform = posedGlobals[i] * parentInverse;
            }
            else
            {
                posedMesh.Bones[i].LocalTransform = posedGlobals[i];
            }
        }

        SkinVertices(posedMesh, bindGlobals, posedGlobals);
        log?.Invoke($"Pose preview preset '{preset}' applied with {appliedOperations} bone rotation step(s).");
        return posedMesh;
    }

    private static void SkinVertices(RetargetMesh mesh, Matrix4x4[] bindGlobals, Matrix4x4[] posedGlobals)
    {
        Dictionary<string, int> boneIndexByName = BuildBoneIndexLookup(mesh.Bones);

        foreach (RetargetSection section in mesh.Sections)
        {
            foreach (RetargetVertex vertex in section.Vertices)
            {
                if (vertex.Weights.Count == 0)
                    continue;

                Vector3 posedPosition = Vector3.Zero;
                Vector3 posedNormal = Vector3.Zero;
                Vector3 posedTangent = Vector3.Zero;
                Vector3 posedBitangent = Vector3.Zero;
                float totalWeight = 0.0f;

                foreach (RetargetWeight weight in vertex.Weights)
                {
                    if (weight.Weight <= 0.0f)
                        continue;

                    if (!boneIndexByName.TryGetValue(weight.BoneName, out int boneIndex) ||
                        boneIndex < 0 ||
                        boneIndex >= bindGlobals.Length ||
                        !Matrix4x4.Invert(bindGlobals[boneIndex], out Matrix4x4 bindInverse))
                    {
                        continue;
                    }

                    Matrix4x4 skinMatrix = bindInverse * posedGlobals[boneIndex];
                    posedPosition += Vector3.Transform(vertex.Position, skinMatrix) * weight.Weight;
                    posedNormal += Vector3.TransformNormal(vertex.Normal, skinMatrix) * weight.Weight;
                    posedTangent += Vector3.TransformNormal(vertex.Tangent, skinMatrix) * weight.Weight;
                    posedBitangent += Vector3.TransformNormal(vertex.Bitangent, skinMatrix) * weight.Weight;
                    totalWeight += weight.Weight;
                }

                if (totalWeight <= 1e-5f)
                    continue;

                float normalization = 1.0f / totalWeight;
                vertex.Position = posedPosition * normalization;
                vertex.Normal = NormalizeOrFallback(posedNormal * normalization, Vector3.UnitY);
                vertex.Tangent = NormalizeOrFallback(posedTangent * normalization, Vector3.UnitX);
                vertex.Bitangent = NormalizeOrFallback(posedBitangent * normalization, Vector3.UnitZ);
            }
        }
    }

    private static int ApplyPresetOperations(
        IReadOnlyList<RetargetBone> bones,
        Dictionary<string, int> boneIndexByName,
        Matrix4x4[] posedGlobals,
        RetargetPosePreset preset,
        PoseFrame frame,
        Action<string> log)
    {
        int operations = 0;

        switch (preset)
        {
            case RetargetPosePreset.BindPose:
                return 0;
            case RetargetPosePreset.APose:
                operations += RotateDescendants(bones, boneIndexByName, posedGlobals, FindBoneIndex(boneIndexByName, "g_l_shoulder", "leftshoulder"), frame.Forward, -22.0f, log);
                operations += RotateDescendants(bones, boneIndexByName, posedGlobals, FindBoneIndex(boneIndexByName, "g_r_shoulder", "rightshoulder"), frame.Forward, 22.0f, log);
                break;
            case RetargetPosePreset.TPose:
                operations += RotateDescendants(bones, boneIndexByName, posedGlobals, FindBoneIndex(boneIndexByName, "g_l_shoulder", "leftshoulder"), frame.Forward, -48.0f, log);
                operations += RotateDescendants(bones, boneIndexByName, posedGlobals, FindBoneIndex(boneIndexByName, "g_r_shoulder", "rightshoulder"), frame.Forward, 48.0f, log);
                break;
            case RetargetPosePreset.ArmsUp:
                operations += RotateDescendants(bones, boneIndexByName, posedGlobals, FindBoneIndex(boneIndexByName, "g_l_shoulder", "leftshoulder"), frame.Forward, -95.0f, log);
                operations += RotateDescendants(bones, boneIndexByName, posedGlobals, FindBoneIndex(boneIndexByName, "g_r_shoulder", "rightshoulder"), frame.Forward, 95.0f, log);
                operations += RotateDescendants(bones, boneIndexByName, posedGlobals, FindBoneIndex(boneIndexByName, "g_l_elbow", "leftelbow"), frame.Forward, -15.0f, log);
                operations += RotateDescendants(bones, boneIndexByName, posedGlobals, FindBoneIndex(boneIndexByName, "g_r_elbow", "rightelbow"), frame.Forward, 15.0f, log);
                break;
            case RetargetPosePreset.LegStep:
                operations += RotateDescendants(bones, boneIndexByName, posedGlobals, FindBoneIndex(boneIndexByName, "g_l_hip", "lefthip", "leftupleg"), frame.Right, 26.0f, log);
                operations += RotateDescendants(bones, boneIndexByName, posedGlobals, FindBoneIndex(boneIndexByName, "g_r_hip", "righthip", "rightupleg"), frame.Right, -10.0f, log);
                operations += RotateDescendants(bones, boneIndexByName, posedGlobals, FindBoneIndex(boneIndexByName, "g_l_knee", "leftknee", "leftleg"), frame.Right, 18.0f, log);
                break;
            case RetargetPosePreset.TwistCheck:
                operations += RotateAroundBoneAxis(bones, boneIndexByName, posedGlobals, FindBoneIndex(boneIndexByName, "g_l_elbow", "leftelbow", "leftforearm"), 70.0f, log);
                operations += RotateAroundBoneAxis(bones, boneIndexByName, posedGlobals, FindBoneIndex(boneIndexByName, "g_r_elbow", "rightelbow", "rightforearm"), -70.0f, log);
                break;
        }

        return operations;
    }

    private static int RotateAroundBoneAxis(
        IReadOnlyList<RetargetBone> bones,
        Dictionary<string, int> boneIndexByName,
        Matrix4x4[] posedGlobals,
        int boneIndex,
        float degrees,
        Action<string> log)
    {
        if (boneIndex < 0 || boneIndex >= bones.Count)
            return 0;

        Vector3 start = posedGlobals[boneIndex].Translation;
        int childIndex = FindPrimaryChildIndex(bones, boneIndex);
        Vector3 axis = childIndex >= 0
            ? NormalizeOrFallback(posedGlobals[childIndex].Translation - start, Vector3.UnitX)
            : Vector3.UnitX;

        return RotateDescendants(bones, boneIndexByName, posedGlobals, boneIndex, axis, degrees, log);
    }

    private static int RotateDescendants(
        IReadOnlyList<RetargetBone> bones,
        Dictionary<string, int> boneIndexByName,
        Matrix4x4[] posedGlobals,
        int boneIndex,
        Vector3 axis,
        float degrees,
        Action<string> log)
    {
        if (boneIndex < 0 || boneIndex >= bones.Count || axis.LengthSquared() <= 1e-6f || MathF.Abs(degrees) <= 1e-3f)
            return 0;

        Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI * degrees / 180.0f);
        Vector3 pivot = posedGlobals[boneIndex].Translation;

        for (int i = 0; i < bones.Count; i++)
        {
            if (!IsDescendantOf(bones, i, boneIndex))
                continue;

            posedGlobals[i] = RotateGlobalTransform(posedGlobals[i], pivot, rotation);
        }

        log?.Invoke($"Pose preview rotated {bones[boneIndex].Name} subtree by {degrees:0.#} degrees.");
        return 1;
    }

    private static Matrix4x4 RotateGlobalTransform(Matrix4x4 current, Vector3 pivot, Quaternion rotation)
    {
        Matrix4x4.Decompose(current, out Vector3 scale, out Quaternion currentRotation, out Vector3 translation);
        Vector3 rotatedOffset = Vector3.Transform(translation - pivot, rotation);
        Quaternion combinedRotation = Quaternion.Normalize(rotation * currentRotation);
        return Matrix4x4.CreateScale(scale) *
               Matrix4x4.CreateFromQuaternion(combinedRotation) *
               Matrix4x4.CreateTranslation(pivot + rotatedOffset);
    }

    private static bool IsDescendantOf(IReadOnlyList<RetargetBone> bones, int index, int ancestorIndex)
    {
        int current = index;
        while (current >= 0 && current < bones.Count)
        {
            if (current == ancestorIndex)
                return true;

            current = bones[current].ParentIndex;
        }

        return false;
    }

    private static int FindPrimaryChildIndex(IReadOnlyList<RetargetBone> bones, int parentIndex)
    {
        for (int i = 0; i < bones.Count; i++)
        {
            if (bones[i].ParentIndex == parentIndex)
                return i;
        }

        return -1;
    }

    private static Dictionary<string, int> BuildBoneIndexLookup(IReadOnlyList<RetargetBone> bones)
    {
        Dictionary<string, int> lookup = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < bones.Count; i++)
            lookup[bones[i].Name] = i;
        return lookup;
    }

    private static int FindBoneIndex(Dictionary<string, int> boneIndexByName, params string[] aliases)
    {
        foreach (string alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
                continue;

            if (boneIndexByName.TryGetValue(alias, out int exact))
                return exact;
        }

        foreach (string alias in aliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
                continue;

            string normalizedAlias = Normalize(alias);
            foreach ((string boneName, int index) in boneIndexByName)
            {
                string normalizedBone = Normalize(boneName);
                if (normalizedBone.Contains(normalizedAlias, StringComparison.OrdinalIgnoreCase) ||
                    normalizedAlias.Contains(normalizedBone, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static string Normalize(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
    {
        return value.LengthSquared() > 1e-6f ? Vector3.Normalize(value) : fallback;
    }

    private readonly record struct PoseFrame(Vector3 Up, Vector3 Right, Vector3 Forward)
    {
        public static PoseFrame Build(IReadOnlyList<RetargetBone> bones, Dictionary<string, int> boneIndexByName)
        {
            Vector3 pelvis = GetBonePosition(bones, boneIndexByName, "g_pelvis", "pelvis", "hips");
            Vector3 head = GetBonePosition(bones, boneIndexByName, "g_head", "head");
            Vector3 leftShoulder = GetBonePosition(bones, boneIndexByName, "g_l_shoulder", "leftshoulder");
            Vector3 rightShoulder = GetBonePosition(bones, boneIndexByName, "g_r_shoulder", "rightshoulder");

            Vector3 up = NormalizeOrFallback(head - pelvis, Vector3.UnitY);
            Vector3 right = NormalizeOrFallback(rightShoulder - leftShoulder, Vector3.UnitX);
            Vector3 forward = NormalizeOrFallback(Vector3.Cross(right, up), Vector3.UnitZ);

            if (Vector3.Cross(right, up).LengthSquared() <= 1e-6f)
                forward = Vector3.UnitZ;

            right = NormalizeOrFallback(Vector3.Cross(up, forward), Vector3.UnitX);
            return new PoseFrame(up, right, forward);
        }

        private static Vector3 GetBonePosition(IReadOnlyList<RetargetBone> bones, Dictionary<string, int> boneIndexByName, params string[] aliases)
        {
            int index = FindBoneIndex(boneIndexByName, aliases);
            return index >= 0 && index < bones.Count ? bones[index].GlobalTransform.Translation : Vector3.Zero;
        }
    }
}
