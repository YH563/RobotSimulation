using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;

namespace RobotSimulation.Core.Rendering;

public class ShaderCache : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, ShaderProgram> _cache = new();
    private bool _disposed = false;
    
    public ShaderCache(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
    }
    
    /// <summary>
    /// 创建或获取 Shader 程序对象
    /// </summary>
    /// <param name="vertexPath">顶点着色器路径</param>
    /// <param name="fragmentPath">片段着色器路径</param>
    /// <returns></returns>
    public ShaderProgram GetOrCreate(string vertexPath, string fragmentPath)
    {
        // 使用路径组合作为唯一键（确保路径一致时命中缓存）
        string key = $"{vertexPath}|{fragmentPath}";

        if (_cache.TryGetValue(key, out var shader))
            return shader;

        // 创建新着色器并加入缓存
        var newShader = new ShaderProgram(_gl, vertexPath, fragmentPath);
        _cache[key] = newShader;
        return newShader;
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        foreach (var shader in _cache.Values)
            shader.Dispose();
        _cache.Clear();
        _disposed = true;
    }
}