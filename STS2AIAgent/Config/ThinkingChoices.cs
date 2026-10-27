using STS2AIAgent.Localization;

namespace STS2AIAgent.Config;

/// <summary>
/// The thinking settings as the settings form offers them: the stored value and the words a player
/// reads for it.
/// </summary>
/// <remarks>
/// The drop-downs used to list the stored values themselves -- <c>auto</c>, <c>deepseek</c>,
/// <c>reasoning_effort</c>, <c>prompt</c>, <c>off</c>/<c>low</c>/<c>medium</c>/<c>high</c> -- and the
/// endpoint's "思考协议" and the model's "思考方式" showed the same four words with nothing to say how
/// they differ. The model's choice wins; its <c>auto</c> defers to the endpoint, and the endpoint's
/// <c>auto</c> infers from the model name (see <c>ThinkingRequestBuilder</c>). The labels say that.
/// Methods rather than fields, so a language change is read on the next build of the form.
/// </remarks>
internal static class ThinkingChoices
{
    /// <summary>The endpoint's request format for thinking.</summary>
    public static IReadOnlyList<(string Id, string Label)> EndpointProtocols() => new[]
    {
        ("auto", Loc.T("自动（按模型名判断）")),
        ("deepseek", Loc.T("DeepSeek 格式")),
        ("reasoning_effort", Loc.T("OpenAI 格式（reasoning_effort）")),
        ("prompt", Loc.T("不发思考参数"))
    };

    /// <summary>The model's own choice; <c>auto</c> hands the decision to its endpoint.</summary>
    public static IReadOnlyList<(string Id, string Label)> ModelModes() => new[]
    {
        ("auto", Loc.T("跟随端点")),
        ("deepseek", Loc.T("DeepSeek 格式")),
        ("reasoning_effort", Loc.T("OpenAI 格式（reasoning_effort）")),
        ("prompt", Loc.T("不发思考参数"))
    };

    /// <summary>How hard the model is asked to think.</summary>
    public static IReadOnlyList<(string Id, string Label)> Intensities() => new[]
    {
        ("off", Loc.T("关闭")),
        ("low", Loc.T("低")),
        ("medium", Loc.T("中")),
        ("high", Loc.T("高"))
    };
}
