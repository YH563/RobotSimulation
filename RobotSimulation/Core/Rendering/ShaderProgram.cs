using Silk.NET.OpenGL;
using System.Numerics;
using System.IO;
using System.Text;

namespace RobotSimulation.Core.Rendering;

public class ShaderProgram : IDisposable
{
    internal readonly GL _gl;
    private readonly uint _handle;
    private bool _disposed = false;

    public ShaderProgram(GL gl, string vertexSrc, string fragmentSrc)
    {
        _gl = gl;
        
        // 编译顶点着色器
        uint vertex = _gl.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vertex, vertexSrc);
        _gl.CompileShader(vertex);
        CheckShaderError(vertex, "VERTEX");
        
        // 编译片段着色器
        uint fragment = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fragment, fragmentSrc);
        _gl.CompileShader(fragment);
        CheckShaderError(fragment, "FRAGMENT");
        
        // 链接程序
        _handle = _gl.CreateProgram();
        _gl.AttachShader(_handle, vertex);
        _gl.AttachShader(_handle, fragment);
        _gl.LinkProgram(_handle);
        CheckProgramError();
        
        // 清除中间着色器对象
        _gl.DetachShader(_handle, vertex);
        _gl.DetachShader(_handle, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
    }
    
    public void Use() => _gl.UseProgram(_handle);
    
    public void SetUniform(string name, int value)
    {
        int loc = _gl.GetUniformLocation(_handle, name);
        if (loc != -1) _gl.Uniform1(loc, value);
    }

    public void SetUniform(string name, float value)
    {
        int loc = _gl.GetUniformLocation(_handle, name);
        if (loc != -1) _gl.Uniform1(loc, value);
    }

    public void SetUniform(string name, Vector2 value)
    {
        int loc = _gl.GetUniformLocation(_handle, name);
        if (loc != -1) _gl.Uniform2(loc, value);
    }

    public void SetUniform(string name, Vector3 value)
    {
        int loc = _gl.GetUniformLocation(_handle, name);
        if (loc != -1) _gl.Uniform3(loc, value);
    }

    public void SetUniform(string name, Vector4 value)
    {
        int loc = _gl.GetUniformLocation(_handle, name);
        if (loc != -1) _gl.Uniform4(loc, value);
    }

    public void SetUniform(string name, Matrix4x4 value)
    {
        int loc = _gl.GetUniformLocation(_handle, name);
        // 保持 transpose = false：
        // System.Numerics.Matrix4x4 采用行主序存储 + 行向量约定（v' = v·M）。
        // 以 false 直接传入时，GLSL 将行主序解释为列主序，得到 M^T；
        // 列向量变换 M^T·p 与行向量变换 p·M 结果一致，几何与法线矩阵自洽。
        if (loc != -1) unsafe { _gl.UniformMatrix4(loc, 1, false, (float*)&value); }
    }
    
    /// <summary>
    /// 检查着色器编译错误
    /// </summary>
    /// <param name="shader">Shader 句柄</param>
    /// <param name="type"></param>
    /// <exception cref="Exception"></exception>
    private void CheckShaderError(uint shader, string type)
    {
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status != (int)GLEnum.True)
            throw new Exception($"{type} shader error: {_gl.GetShaderInfoLog(shader)}");
    }
    
    /// <summary>
    /// 检查程序链接错误
    /// </summary>
    /// <exception cref="Exception"></exception>
    private void CheckProgramError()
    {
        _gl.GetProgram(_handle, ProgramPropertyARB.LinkStatus, out int status);
        if (status != (int)GLEnum.True)
            throw new Exception($"Program link error: {_gl.GetProgramInfoLog(_handle)}");
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && _handle != 0)
        {
            _gl.DeleteProgram(_handle);
            _disposed = true;
        }
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

