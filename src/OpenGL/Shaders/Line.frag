#version 330 core

uniform vec4 uColor;
uniform int uPerVertexColor;   // 1 = use the per-vertex color g_color.

in vec4 g_color;

out vec4 out_color;

void main()
{
    out_color = (uPerVertexColor == 1) ? g_color : uColor;
}
