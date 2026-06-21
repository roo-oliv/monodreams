// Texture-array tilemap effect.
//
// NOTE (HiDef-only — not Reach): sampler2DArray / tex2DArray require Shader Model 4 and the
// HiDef graphics profile. BlazorGL/WebGL caps below SM4, so this variant cannot run under the
// Reach profile the web backend uses — it is the HiDef shader risk flagged in the porting plan.
// It is currently NOT built by any .mgcb and NOT loaded at runtime (MasterRenderSystem renders
// via BasicEffect); kept as a reference asset. A WebGL-capable path would have to drop texture
// arrays (e.g. an atlas + the simpler MonoDreams/Effect/MultiTextureEffect.fx, which IS Reach).
// The GL counterpart lives in MultiTextureEffect.glsl (reused, not regenerated).
sampler2DArray TextureArray : register(s0);

float4x4 WorldViewProjection;
float2 tileSize;
float3 tilePosition;

struct VertexShaderInput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float3 TexCoord : TEXCOORD0;
};

struct PixelShaderInput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float3 TexCoord : TEXCOORD0;
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
    float2 wrappedCoords = frac(input.TexCoord.xy) * tileSize + tilePosition.xy;
    float3 finalCoords = float3(wrappedCoords, input.TexCoord.z);
    return tex2DArray(TextureArray, finalCoords) * input.Color;
}

technique BasicTech
{
    pass Pass0
    {
        VertexShader = compile vs_4_0_level_9_1 VertexShaderFunction();
        PixelShader = compile ps_4_0_level_9_1 PixelShaderFunction();
    }
}
