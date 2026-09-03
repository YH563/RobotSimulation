#version 330 core

in vec2 v_uv;
in vec3 v_normal;
in vec3 v_tangent;
in vec3 v_worldPos;

#define MAX_LIGHTS 8

uniform int uLightCount;
uniform vec3 uLightColors[MAX_LIGHTS];      // 颜色
uniform vec3 uLightPositions[MAX_LIGHTS];   // 点光源位置
uniform vec3 uLightDirections[MAX_LIGHTS];  // 方向光方向（照射方向）
uniform int  uLightTypes[MAX_LIGHTS];       // 0=点光, 1=方向光
uniform float uLightIntensities[MAX_LIGHTS];

uniform vec3 uViewPos;      // 摄像机世界位置
uniform vec4 uBaseColor;    // 后备颜色

uniform sampler2D uAlbedo;
uniform sampler2D uNormal;
uniform int uHasAlbedo;
uniform int uHasNormal;

out vec4 out_color;

void main()
{
    // 1. 获取漫反射颜色
    vec4 albedo = uHasAlbedo == 1 ? texture(uAlbedo, v_uv) : uBaseColor;
    if (albedo.a < 0.1) discard; // 透明度裁剪

    // 2. 获取法线（如果存在法线贴图，则转换到世界空间）
    vec3 normal = normalize(v_normal);
    if (uHasNormal == 1) {
        // 从法线贴图采样，范围 [0,1] 映射到 [-1,1]
        vec3 tangentNormal = texture(uNormal, v_uv).xyz * 2.0 - 1.0;
        // 构建 TBN 矩阵（切线、副切线、法线）
        vec3 T = normalize(v_tangent);
        vec3 N = normalize(v_normal);
        // 重新正交化（Gram-Schmidt）
        T = normalize(T - dot(T, N) * N);
        vec3 B = cross(N, T);
        mat3 TBN = mat3(T, B, N);
        normal = normalize(TBN * tangentNormal);
    }

    vec3 viewDir = normalize(uViewPos - v_worldPos);

    // 3. 逐光源累加（Blinn-Phong）
    vec3 result = vec3(0.0);
    for (int i = 0; i < uLightCount; i++)
    {
        vec3 lightColor = uLightColors[i] * uLightIntensities[i];

        vec3 lightDir;
        if (uLightTypes[i] == 1)
            lightDir = normalize(-uLightDirections[i]); // 方向光：朝向光源 = 反方向
        else
            lightDir = normalize(uLightPositions[i] - v_worldPos); // 点光

        float diff = max(dot(normal, lightDir), 0.0);

        vec3 halfDir = normalize(lightDir + viewDir);
        float spec = pow(max(dot(normal, halfDir), 0.0), 32.0);

        result += lightColor * (diff + spec * 0.5); // 高光强度系数 0.5
    }

    // 环境光（与光源无关的常数项，保证背光面不纯黑）
    vec3 ambientColor = vec3(0.12, 0.13, 0.15);
    vec3 finalColor = (ambientColor + result) * albedo.rgb;

    out_color = vec4(finalColor, albedo.a);
}
