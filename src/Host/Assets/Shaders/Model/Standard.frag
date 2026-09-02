#version 330 core

in vec2 v_uv;
in vec3 v_normal;
in vec3 v_tangent;
in vec3 v_worldPos;

uniform vec3 uLightPos;     // 光源世界位置
uniform vec3 uLightColor;   // 光源色彩
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

    // 3. 光照计算（Blinn-Phong）
    vec3 lightDir = normalize(uLightPos - v_worldPos);
    vec3 viewDir = normalize(uViewPos - v_worldPos);
    vec3 halfDir = normalize(lightDir + viewDir);

    // 环境光（保持足够的亮度，避免背光面完全纯黑）
    float ambient = 0.3;
    // 漫反射
    float diff = max(dot(normal, lightDir), 0.0);
    // 高光（指数 32）
    float spec = pow(max(dot(normal, halfDir), 0.0), 32.0);

    vec3 lightColor = uLightColor;
    vec3 ambientColor = ambient * lightColor;
    vec3 diffuseColor = diff * lightColor;
    vec3 specularColor = spec * lightColor * 0.5; // 强度系数

    vec3 finalColor = (ambientColor + diffuseColor) * albedo.rgb + specularColor;

    out_color = vec4(finalColor, albedo.a);
}