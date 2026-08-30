using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using System.Drawing;
using StbImageSharp;

namespace RobotSimulation;

public class Program
{
    private static IWindow _window;
    private static GL _gl;
    private static uint _vao;  // 顶点数组对象，配置单
    private static uint _vbo;  // 顶点缓冲对象，仓库
    private static uint _ebo;  // 元素缓冲对象（索引缓冲对象 IBO）
    private static uint _program;  // 着色器编译得到的程序
    private static uint _texture;  // 纹理
    
    public static void Main(string[] args)
    {
        // 创建窗口选项
        WindowOptions options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(800, 600),
            Title = "My first Silk.NET application!",
            VSync = true
        };
        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Run();
    }

    private static unsafe void OnLoad()
    {
        Console.WriteLine("Hello World!");
        IInputContext input = _window.CreateInput();
        for (int i = 0; i < input.Keyboards.Count; i++)
            input.Keyboards[i].KeyDown += KeyDown;
        
        _gl = _window.CreateOpenGL();  // 创建Opengl上下文
        _vao = _gl.GenVertexArray();
        _gl.BindVertexArray(_vao);  // 创建顶点数组对象并绑定
        // The quad vertices data. Now with Texture coordinates!
        float[] vertices =
        {
        //       aPosition     | aTexCoords
            0.5f,  0.5f, 0.0f,  1.0f, 1.0f,
            0.5f, -0.5f, 0.0f,  1.0f, 0.0f,
            -0.5f, -0.5f, 0.0f,  0.0f, 0.0f,
            -0.5f,  0.5f, 0.0f,  0.0f, 1.0f
        };
        _vbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* buf = vertices)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint) (vertices.Length * sizeof(float)), buf, BufferUsageARB.StaticDraw);
        // 顶点索引
        uint[] indices =
        {
            0u, 1u, 3u,
            1u, 2u, 3u
        };
        _ebo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        fixed (uint* buf = indices)
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint) (indices.Length * sizeof(uint)), buf, BufferUsageARB.StaticDraw);
        // 顶点着色器代码
        const string vertexCode = @"
        #version 330 core

        layout (location = 0) in vec3 aPosition;
        // Add a new input attribute for the texture coordinates
        layout (location = 1) in vec2 aTextureCoord;

        // Add an output variable to pass the texture coordinate to the fragment shader
        // This variable stores the data that we want to be received by the fragment
        out vec2 frag_texCoords;

        void main()
        {
            gl_Position = vec4(aPosition, 1.0);
            // Assigin the texture coordinates without any modification to be recived in the fragment
            frag_texCoords = aTextureCoord;
        }";
        
        // 片段着色器
        const string fragmentCode = @"
        #version 330 core

        // Receive the input from the vertex shader in an attribute
        in vec2 frag_texCoords;
        uniform sampler2D uTexture;

        out vec4 out_color;

        void main()
        {
            // This will allow us to see the texture coordinates in action!
            out_color = texture(uTexture, frag_texCoords);
        }";
        
        // 创建着色器对象
        uint vertexShader = _gl.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vertexShader, vertexCode);
        
        _gl.CompileShader(vertexShader);
        _gl.GetShader(vertexShader, ShaderParameterName.CompileStatus, out int vStatus);
        if (vStatus != (int) GLEnum.True)
            throw new Exception("Vertex shader failed to compile: " + _gl.GetShaderInfoLog(vertexShader));
        
        uint fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fragmentShader, fragmentCode);

        _gl.CompileShader(fragmentShader);
        _gl.GetShader(fragmentShader, ShaderParameterName.CompileStatus, out int fStatus);
        if (fStatus != (int) GLEnum.True)
            throw new Exception("Fragment shader failed to compile: " + _gl.GetShaderInfoLog(fragmentShader));
        
        _program = _gl.CreateProgram();
        // 链接程序
        _gl.AttachShader(_program, vertexShader);
        _gl.AttachShader(_program, fragmentShader);
        _gl.LinkProgram(_program);
        _gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out int lStatus);
        if (lStatus != (int) GLEnum.True)
            throw new Exception("Program failed to link: " + _gl.GetProgramInfoLog(_program));
        _gl.UseProgram(_program);
        int location = _gl.GetUniformLocation(_program, "uTexture");
        _gl.Uniform1(location, 0);
        _gl.UseProgram(0); // 可选解绑，但保持干净
        
        // 过河拆桥
        _gl.DetachShader(_program, vertexShader);
        _gl.DetachShader(_program, fragmentShader);
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);
        
        const uint positionLoc = 0;
        _gl.EnableVertexAttribArray(positionLoc);
        _gl.VertexAttribPointer(positionLoc, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)0);
        
        const uint texCoordLoc = 1;
        _gl.EnableVertexAttribArray(texCoordLoc);
        _gl.VertexAttribPointer(texCoordLoc, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), (void*)(3 * sizeof(float)));
        
        _gl.BindVertexArray(0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, 0);
        
        _texture = _gl.GenTexture();
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _texture);
        
        // ImageResult.FromMemory reads the bytes of the .png file and returns all its information!
        ImageResult result = ImageResult.FromMemory(File.ReadAllBytes("silk.png"), ColorComponents.RedGreenBlueAlpha);
        // Define a pointer to the image data
        fixed (byte* ptr = result.Data)
            // Here we use "result.Width" and "result.Height" to tell OpenGL about how big our texture is.
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)result.Width,
                (uint)result.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
        _gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)TextureWrapMode.Repeat);
        _gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)TextureWrapMode.Repeat);
        _gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameterI(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)TextureMagFilter.Nearest);
        _gl.GenerateMipmap(TextureTarget.Texture2D);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }

    private static void OnUpdate(double deltaTime)
    {
        // Console.WriteLine("Update");
    }

    private static unsafe void OnRender(double deltaTime)
    {
        _gl.ClearColor(Color.CornflowerBlue);  // 调颜色
        _gl.Clear(ClearBufferMask.ColorBufferBit);  // 刷墙
        
        _gl.BindVertexArray(_vao);
        _gl.UseProgram(_program);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _texture);
        _gl.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, (void*) 0);
        
        // Console.WriteLine("Render!");
    }

    private static void KeyDown(IKeyboard keyboard, Key key, int keyCode)
    {
        if (key == Key.Escape)
            _window.Close();
    }
}

