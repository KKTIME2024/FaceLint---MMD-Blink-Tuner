using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;
using MmdBlendShapeScaler;

[assembly: ExportsPlugin(typeof(MmdBlendShapeScalerPlugin))]

namespace MmdBlendShapeScaler
{
    internal static class EditorAssemblyLoadedCheck
    {
        [InitializeOnLoadMethod]
        static void OnLoad()
        {
            Debug.Log("[MmdScaler] ★ Editor assembly loaded. AssemblyInfo.cs is executing.");
        }
    }
}
