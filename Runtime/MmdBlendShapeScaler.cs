using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MmdBlendShapeScaler
{
    /// <summary>
    /// Stores per-MMD-blendshape scale factors for non-destructive delta scaling at build time.
    /// Only stores entries where scale ≠ 1.0. GetScale() defaults to 1.0.
    ///
    /// KEY DESIGN: Explicitly references the target SkinnedMeshRenderer.
    /// Does NOT rely on VRCAvatarDescriptor.VisemeSkinnedMesh,
    /// because MMD blendshapes may live on Body/Face/Head/any mesh.
    /// Editor Window and Build Pass use this same reference for data consistency.
    /// </summary>
    [AddComponentMenu("VRC Avatar MMD & Blink Fixer/MMD & Blink Fixer")]
    [DisallowMultipleComponent]
    public class MmdBlendShapeScaler : MonoBehaviour, VRC.SDKBase.IEditorOnly, ISerializationCallbackReceiver
    {
        public const int CURRENT_DATA_VERSION = 1;

        // ── Target Renderer (CRITICAL: do NOT rely on VisemeSkinnedMesh) ──
        [SerializeField] public SkinnedMeshRenderer targetRenderer;

        // ── Serialization ──
        [SerializeField] internal List<MmdScaleEntry> _entries = new List<MmdScaleEntry>();
        [SerializeField] internal int dataVersion = CURRENT_DATA_VERSION;

        // ── Runtime dictionary (deserialized from _entries) ──
        public Dictionary<string, float> scales = new Dictionary<string, float>();

        // ── Public API ──

        /// <summary>Set scale for an MMD blendshape. scale=1.0 removes the entry.</summary>
        public void SetScale(string mmdName, float scale)
        {
            if (Mathf.Abs(scale - 1.0f) < 0.001f)
            {
                scales.Remove(mmdName);
            }
            else
            {
                scales[mmdName] = Mathf.Clamp(scale, 0f, 2f);
            }
        }

        /// <summary>Get scale factor. Returns 1.0 if no entry exists.</summary>
        public float GetScale(string mmdName)
        {
            return scales.TryGetValue(mmdName, out float s) ? s : 1.0f;
        }

        public bool HasScale(string mmdName) => scales.ContainsKey(mmdName);
        public void RemoveScale(string mmdName) => scales.Remove(mmdName);

        public void RemoveAll()
        {
            scales.Clear();
            _entries.Clear();
        }

        public int Count => scales.Count;

        public IEnumerable<MmdScaleEntry> GetModifiedEntries()
        {
            return scales.Select(kvp => new MmdScaleEntry { name = kvp.Key, scale = kvp.Value });
        }

        public bool IsValid => targetRenderer != null && targetRenderer.sharedMesh != null;

        // ── Serialization callbacks ──

        public void OnBeforeSerialize()
        {
            _entries.Clear();
            foreach (var (name, scale) in scales)
            {
                // ★ Only store entries where scale ≠ 1.0
                if (Mathf.Abs(scale - 1.0f) > 0.001f)
                {
                    _entries.Add(new MmdScaleEntry { name = name, scale = scale });
                }
            }
        }

        public void OnAfterDeserialize()
        {
            scales = new Dictionary<string, float>();
            foreach (var entry in _entries)
            {
                if (!string.IsNullOrEmpty(entry.name))
                {
                    scales[entry.name] = Mathf.Clamp(entry.scale, 0f, 2f);
                }
            }
        }
    }

    [Serializable]
    public class MmdScaleEntry
    {
        public string name;   // e.g. "まばたき"
        public float scale;   // 0.0 - 2.0
    }
}
