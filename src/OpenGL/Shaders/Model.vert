#version 330 core

layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec2 aTexCoord;
layout (location = 2) in vec3 aNormal;
layout (location = 3) in vec3 aTangent;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

out vec2 v_uv;
out vec3 v_normal;
out vec3 v_tangent;
out vec3 v_worldPos;

void main()
{
    vec4 worldPos = uModel * vec4(aPosition, 1.0);
    v_worldPos = worldPos.xyz;
    v_uv = aTexCoord;
    // 法线从模型空间转到世界空间（逆转置，兼容非均匀缩放）
    v_normal = normalize(mat3(transpose(inverse(uModel))) * aNormal);
    v_tangent = normalize(mat3(transpose(inverse(uModel))) * aTangent);
    gl_Position = uProjection * uView * worldPos;
}
