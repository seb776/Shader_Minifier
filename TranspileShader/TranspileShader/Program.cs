﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Ast;
using static Ast.TopLevel;

namespace TranspileShader
{
    internal class Program
    {


        static void Main(string[] args)
        {
            Options.init(new string[] { "--no-inlining", "--no-renaming", "--preserve-all-globals", "--preserve-externals", "--no-sequence" });
            {
                var sb = new StringBuilder();
                var ast = ShaderMinifier.minifyFiles(new string[] { "./shader.glsl" });
                IShaderASTPrinter printer = new JSShaderASTPrinter();
                printer.Print(ast.Item1.First(), sb);
                System.IO.File.WriteAllText("./shader.js", sb.ToString());
                Console.WriteLine(sb.ToString());
                Console.ReadKey();
            }
        }
    }
}
