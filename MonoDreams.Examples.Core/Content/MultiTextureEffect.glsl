#version 330

uniform sampler2DArray TextureArray;
uniform mat4 WorldViewProjection;
uniform vec2 tileSize;
uniform vec3 tilePosition;

in vec4 vColor;
in vec3 vTexCoord;

out vec4 FragColor;

void main()
{
    vec2 wrappedCoords = fract(vTexCoord.xy) * tileSize + tilePosition.xy;
    vec3 finalCoords = vec3(wrappedCoords, vTexCoord.z);
    FragColor = texture(TextureArray, finalCoords) * vColor;
}
