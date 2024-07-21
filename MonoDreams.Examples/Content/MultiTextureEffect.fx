// HLSL for DirectX
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
