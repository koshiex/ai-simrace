using SimCoach.LLM.Providers;

namespace SimCoach.LLM;

/// <summary>
/// Public startup-guard seam over the internal <see cref="SchemaFamily"/> inference (which is
/// <c>internal</c> and cannot leak across the assembly boundary): reports whether a resolved model id belongs
/// to the Gemini family, whose <c>responseSchema</c> strips array/length bound keywords — notably
/// <c>maxItems</c>. Coach's debrief guard keys on this rather than a hardcoded model list, so it tracks the
/// exact family inference the runtime translator selection uses (M28).
/// </summary>
public static class ModelSchemaFamilyGuard
{
    public static bool IsGeminiFamily(string modelId) =>
        SchemaTranslatorSelector.FamilyOf(modelId) == SchemaFamily.Gemini;
}
