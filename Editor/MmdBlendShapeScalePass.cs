using nadena.dev.ndmf;
using UnityEngine;

namespace MmdBlendShapeScaler
{
    public class MmdBlendShapeScalePass : Pass<MmdBlendShapeScalePass>
    {
        public override string DisplayName => "Scale MMD BlendShapes";

        static MmdBlendShapeScalePass()
        {
            Debug.Log("[MmdScaler] Pass static constructor called.");
        }

        protected override void Execute(BuildContext context)
        {
            var scalers = context.AvatarRootObject
                .GetComponentsInChildren<MmdBlendShapeScaler>(includeInactive: true);

            Debug.Log($"[MmdScaler] Execute started. Found {scalers.Length} scaler(s) on avatar.");

            foreach (var scaler in scalers)
            {
                if (scaler == null || scaler.Count == 0)
                {
                    Debug.Log($"[MmdScaler] Skipping scaler: {(scaler == null ? "null" : $"Count=0")}");
                    continue;
                }
                if (!scaler.IsValid)
                {
                    Debug.Log($"[MmdScaler] Skipping scaler: invalid (targetRenderer or mesh null)");
                    continue;
                }

                var renderer = scaler.targetRenderer;
                var originalMesh = renderer.sharedMesh;

                // Step 1: Clone mesh (non-destructive)
                var meshCopy = Object.Instantiate(originalMesh);

                // Step 2: Streaming rewrite
                // Read one frame -> scale if needed -> immediately AddFrame.
                // Only 3 working arrays + one lazy-allocated scaled array.
                int blendShapeCount = originalMesh.blendShapeCount;
                int vertexCount = originalMesh.vertexCount;

                // Reusable working arrays (overwritten each frame)
                var deltaV = new Vector3[vertexCount];
                var deltaN = new Vector3[vertexCount];
                var deltaT = new Vector3[vertexCount];

                // Scaled vertex array (lazy allocation, only when needed)
                Vector3[] scaledV = null;

                meshCopy.ClearBlendShapes();

                for (int i = 0; i < blendShapeCount; i++)
                {
                    string name = originalMesh.GetBlendShapeName(i);
                    int frameCount = originalMesh.GetBlendShapeFrameCount(i);

                    bool needsScale = scaler.scales.TryGetValue(name, out float scale)
                                   && Mathf.Abs(scale - 1.0f) > 0.001f;

                    // Lazy allocation of scaled array
                    if (needsScale && scaledV == null)
                        scaledV = new Vector3[vertexCount];

                    for (int f = 0; f < frameCount; f++)
                    {
                        float weight = originalMesh.GetBlendShapeFrameWeight(i, f);
                        originalMesh.GetBlendShapeFrameVertices(i, f, deltaV, deltaN, deltaT);

                        if (needsScale)
                        {
                            // Only scale vertices. Normals/tangents are NOT position deltas.
                            // Scaling them causes shading exaggeration and specular artifacts.
                            for (int v = 0; v < vertexCount; v++)
                                scaledV[v] = deltaV[v] * scale;

                            meshCopy.AddBlendShapeFrame(name, weight, scaledV, deltaN, deltaT);
                        }
                        else
                        {
                            // Direct write (deltaV/N/T are correct for this frame)
                            meshCopy.AddBlendShapeFrame(name, weight, deltaV, deltaN, deltaT);
                        }
                    }
                }

                // Step 3: Assign clone and destroy component
                renderer.sharedMesh = meshCopy;
                Debug.Log($"[MmdScaler] Processed {blendShapeCount} blendshapes for '{renderer.name}'. Scaled: {scaler.Count}.");
                Object.DestroyImmediate(scaler);
            }

            Debug.Log("[MmdScaler] Execute finished.");
        }
    }
}
