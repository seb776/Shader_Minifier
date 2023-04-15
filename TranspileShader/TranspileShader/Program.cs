using System;
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
        static void TopLevel_TLVerbatim(TopLevel tl, StringBuilder sb)
        {
            var verbatim = tl as TLVerbatim;
            sb.AppendLine(verbatim.Item);
        }
        static void TopLevel_TLFunction(TopLevel tl, StringBuilder sb)
        {
            Function func = tl as Function;
            var decl = func.Item1;
            // {decl.retType.name.ToString()}
            sb.AppendLine($@"function {decl.fName.Name}(param)");
            sb.AppendLine("{");
            sb.AppendLine("\tStatements;");
            sb.AppendLine("}");
        }

        static void TopLevel(Ast.Shader shader, StringBuilder sb)
        {
            Dictionary<System.Type, Action<TopLevel, StringBuilder>> match = new Dictionary<System.Type, Action<TopLevel, StringBuilder>>
            {
                { typeof(TLVerbatim),TopLevel_TLVerbatim },
                { typeof(Function),TopLevel_TLFunction },
                { typeof(TLDecl),null },
                { typeof(TypeDecl),null },
                { typeof(Precision),null },
            };
            foreach (var topLvlStmt in shader.code)
            {
                Action<TopLevel, StringBuilder> call = null;
                if (match.TryGetValue(topLvlStmt.GetType(), out call))
                {
                    if (call != null)
                        call(topLvlStmt, sb);
                }
                
            }
        }

        static void Main(string[] args)
        {
           // if (Options.initFiles(args))
            {
                var sb = new StringBuilder();
                var ast = ShaderMinifier.minifyFiles(new string[] { "./shader.glsl" });
                TopLevel(ast.Item1.First(), sb);
                Console.WriteLine(sb.ToString());
                Console.ReadKey();
            }
        }
    }
}
