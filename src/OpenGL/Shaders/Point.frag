#version 330 core

uniform vec4 uColor;
uniform int uPerVertexColor;

in vec4 v_color;

out vec4 out_color;

void main()
{
    // Circular point clipping: gl_PointCoord in [0,1]
    vec2 c = gl_PointCoord - vec2(0.5);
    if (dot(c, c) > 0.25)
        discard;
    out_color = (uPerVertexColor == 1) ? v_color : uColor;
}
