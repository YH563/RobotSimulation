#version 330 core

uniform vec4 uColor;
uniform int uPerVertexColor;   // 1 = use the per-vertex color v_color.

in vec4 v_color;

out vec4 out_color;

void main()
{
    out_color = (uPerVertexColor == 1) ? v_color : uColor;
}
