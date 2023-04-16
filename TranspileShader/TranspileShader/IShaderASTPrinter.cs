using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TranspileShader
{
    internal interface IShaderASTPrinter
    {
        void Print(Ast.Shader shader, StringBuilder sb);
    }
}
