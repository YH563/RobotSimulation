#version 330 core

layout (location = 0) in vec3 aPosition;

uniform mat4 uAxesModel;          // 轴的世界"旋转+平移"矩阵（已剔除父级 Scale）
uniform mat4 uView;
uniform mat4 uProjection;
uniform vec3 uViewPos;

// 坐标轴"恒定屏幕尺寸"控制：
uniform vec3  uAxesOrigin;        // 轴的原点（节点世界位置）
uniform float uAxesRefLocalLen;   // 箭头在局部空间沿轴的总长（用于把几何各向同性缩放到目标长度）
uniform float uAxesRatio;         // 期望轴长 = uAxesRatio × 相机到原点距离，然后按 min/max 夹取
uniform float uAxesMinLength;     // 世界长度下限
uniform float uAxesMaxLength;     // 世界长度上限

void main()
{
    // 先用"无缩放"模型矩阵：off 只含旋转、不含父级缩放
    vec4 worldR = uAxesModel * vec4(aPosition, 1.0);
    vec3 off = worldR.xyz - uAxesOrigin;

    // 各向同性缩放：整支箭头按统一比例拉/压到目标长度，形状保持不变
    float targetLen = clamp(uAxesRatio * length(uViewPos - uAxesOrigin),
                            uAxesMinLength, uAxesMaxLength);
    float s = targetLen / uAxesRefLocalLen;

    vec3 finalPos = uAxesOrigin + off * s;
    gl_Position = uProjection * uView * vec4(finalPos, 1.0);
}
