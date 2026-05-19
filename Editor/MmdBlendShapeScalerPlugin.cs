using nadena.dev.ndmf;
using UnityEngine;

namespace MmdBlendShapeScaler
{
    public class MmdBlendShapeScalerPlugin : Plugin<MmdBlendShapeScalerPlugin>
    {
        public override string QualifiedName => "mmd-blendshape-scaler";
        public override string DisplayName => "MMD BlendShape Scaler";

        protected override void Configure()
        {
            Debug.Log("[MmdScaler] Configure called — registering pass");
            var seq = InPhase(BuildPhase.Transforming);
            seq.AfterPlugin("nadena.dev.modular-avatar");
            seq.Run(MmdBlendShapeScalePass.Instance);
        }
    }
}
