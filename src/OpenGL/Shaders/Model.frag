#version 330 core

in vec2 v_uv;
in vec3 v_normal;
in vec3 v_tangent;
in vec3 v_worldPos;

#define MAX_LIGHTS 8

uniform int uLightCount;
uniform vec3 uLightColors[MAX_LIGHTS];
uniform vec3 uLightPositions[MAX_LIGHTS];
uniform vec3 uLightDirections[MAX_LIGHTS];
uniform int  uLightTypes[MAX_LIGHTS];
uniform float uLightIntensities[MAX_LIGHTS];

uniform vec3 uViewPos;
uniform vec3 uAmbientColor;   // 场景环境光（Renderer 每帧从 Scene.Settings 传入）
uniform vec4 uBaseColor;

uniform sampler2D uAlbedo;
uniform sampler2D uNormal;
uniform int uHasAlbedo;
uniform int uHasNormal;

out vec4 out_color;

void main()
{
    // 1. 漫反射颜色
    vec4 albedo = uHasAlbedo == 1 ? texture(uAlbedo, v_uv) : uBaseColor;
    if (albedo.a < 0.1) discard;

    // 2. 法线（有法线贴图时经 TBN 转到世界空间）
    vec3 normal = normalize(v_normal);
    if (uHasNormal == 1) {
        vec3 tangentNormal = texture(uNormal, v_uv).xyz * 2.0 - 1.0;
        vec3 T = normalize(v_tangent);
        vec3 N = normalize(v_normal);
        T = normalize(T - dot(T, N) * N);
        vec3 B = cross(N, T);
        normal = normalize(mat3(T, B, N) * tangentNormal);
    }

    vec3 viewDir = normalize(uViewPos - v_worldPos);

    // 3. 逐光源（Blinn-Phong）
    vec3 result = vec3(0.0);
    for (int i = 0; i < uLightCount; i++)
    {
        vec3 lightColor = uLightColors[i] * uLightIntensities[i];

        vec3 lightDir;
        if (uLightTypes[i] == 1)
            lightDir = normalize(-uLightDirections[i]);
        else
            lightDir = normalize(uLightPositions[i] - v_worldPos);

        float diff = max(dot(normal, lightDir), 0.0);
        vec3 halfDir = normalize(lightDir + viewDir);
        float spec = pow(max(dot(normal, halfDir), 0.0), 32.0);
        result += lightColor * (diff + spec * 0.5);
    }

    // 环境光由场景设置驱动（保证阴面不纯黑；与光源无关）
    out_color = vec4((uAmbientColor + result) * albedo.rgb, albedo.a);
}
