using nadena.dev.ndmf;

[assembly: ExportsPlugin(typeof(MmdBlendShapeScaler.MmdBlendShapeScalerPlugin))]

namespace MmdBlendShapeScaler
{
    public class MmdBlendShapeScalerPlugin : Plugin<MmdBlendShapeScalerPlugin>
    {
        public override string QualifiedName => "vrc-avatar-mmd-blink-fixer";
        public override string DisplayName => "VRC Avatar MMD & Blink Fixer";

        protected override void Configure()
        {
            var seq = InPhase(BuildPhase.Transforming);
            seq.AfterPlugin("nadena.dev.modular-avatar");
            seq.Run(MmdBlendShapeScalePass.Instance);
        }
    }
}
