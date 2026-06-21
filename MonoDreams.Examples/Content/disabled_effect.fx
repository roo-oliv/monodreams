// Cross-backend shader-model selection. OPENGL (DesktopGL) and __KNIFX__ (KNI/BlazorGL)
// both compile the GL path, where SM3 keeps the effect inside the Reach feature set so it
// runs on BlazorGL/WebGL (which caps below SM4). DirectX/HiDef uses feature-level-9_1 SM4.
#if OPENGL || __KNIFX__
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#else
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

Texture2D SpriteTexture;

sampler2D SpriteTextureSampler = sampler_state
{
	Texture = <SpriteTexture>;
};

struct VertexShaderOutput
{
	float4 Position : SV_POSITION;
	float4 Color : COLOR0;
	float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : COLOR
{
	float4 texColor = tex2D(SpriteTextureSampler,input.TextureCoordinates) * input.Color;
	float gray = (texColor.r + texColor.g + texColor.b) / 3.0;
	return float4(gray, gray, gray, texColor.a);
}

technique SpriteDrawing
{
	pass P0
	{
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};