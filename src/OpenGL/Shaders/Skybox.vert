#version 330 core

layout (location = 0) in vec3 aPosition;

uniform mat4 uProjection;
uniform mat4 uView;   // 仅用其旋转部分（平移分量被 mat3 剔除）

out vec3 v_uvw;

void main()
{
    v_uvw = aPosition;
    vec4 pos = uProjection * mat4(mat3(uView)) * vec4(aPosition, 1.0);
    gl_Position = pos.xyww;   // 深度恒为 1（与背景同深）
}
