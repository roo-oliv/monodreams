#version 330 core

uniform sampler2D Texture;
uniform mat4 WorldViewProjection;

in vec4 vColor;
in vec2 vTexCoord;

out vec4 FragColor;

void main()
{
    FragColor = texture(Texture, vTexCoord) * vColor;
}
