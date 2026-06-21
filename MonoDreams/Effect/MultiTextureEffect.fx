// MultiTextureEffect.fx
//
// Cross-backend shader-model selection. The 2MGFX/KniFXC effect preprocessor exposes
// platform defines: OPENGL (DesktopGL) and __KNIFX__ (KNI/BlazorGL) both target the GL
// path, where the Reach profile maps to SM3 (vs_3_0/ps_3_0). DirectX/HiDef uses the
// feature-level-9_1 SM4 profiles. Selecting SM3 on the GL path keeps this effect inside
// the Reach feature set so it compiles for BlazorGL/WebGL (which caps below SM4).
#if OPENGL || __KNIFX__
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

sampler2D Texture;

float4x4 WorldViewProjection;

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TexCoord : TEXCOORD0;
};

PixelShaderInput VertexShaderFunction(VertexShaderInput input)
{
    PixelShaderInput output;
    output.Position = mul(input.Position, WorldViewProjection);
    output.Color = input.Color;
    output.TexCoord = input.TexCoord;
    return output;
}

float4 PixelShaderFunction(PixelShaderInput input) : SV_TARGET
{
    return tex2D(Texture, input.TexCoord) * input.Color;
}

technique BasicTech
{
    pass Pass0
    {
        VertexShader = compile VS_SHADERMODEL VertexShaderFunction();
        PixelShader = compile PS_SHADERMODEL PixelShaderFunction();
    }
}
