#version 330 core

uniform vec4 uColor;

out vec4 out_color;

void main()
{
    // 圆点裁剪：gl_PointCoord ∈ [0,1]
    vec2 c = gl_PointCoord - vec2(0.5);
    if (dot(c, c) > 0.25)
        discard;
    out_color = uColor;
}
