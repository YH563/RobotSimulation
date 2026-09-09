#version 330 core

layout (location = 0) in vec3 aPosition;

uniform mat4 uProjection;
uniform mat4 uView;   // Only its rotation part is used (the translation is dropped by mat3).

out vec3 v_uvw;

void main()
{
    v_uvw = aPosition;
    vec4 pos = uProjection * mat4(mat3(uView)) * vec4(aPosition, 1.0);
    gl_Position = pos.xyww;   // Depth is always 1 (same depth as the background).
}
