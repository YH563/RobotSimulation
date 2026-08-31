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

        if (_cache.TryGetValue(key, out var cached))
            return cached;

        // 创建新着色器并加入缓存
        string vertexSrc = ReadFileWithoutBom(vertexPath);
        string fragmentSrc = ReadFileWithoutBom(fragmentPath);

        var shader = new ShaderProgram(_gl, vertexSrc, fragmentSrc);
        _cache[key] = shader;
        return shader;
    }
    
    /// <summary>
    /// 使用路径读取着色器
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    private static string ReadFileWithoutBom(string path)
    {
        string content = File.ReadAllText(path);
        if (content.Length > 0 && content[0] == '\uFEFF')
            content = content.Substring(1);
        return content;
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