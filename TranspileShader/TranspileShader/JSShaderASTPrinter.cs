using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Ast.TopLevel;
using static Ast;
using Microsoft.FSharp.Collections;
using static Ast.Stmt;

namespace TranspileShader
{
    internal class JSShaderASTPrinter : IShaderASTPrinter
    {
        static void TopLevel_TLVerbatim(TopLevel tl, StringBuilder sb)
        {
            var verbatim = tl as TLVerbatim;
            sb.AppendLine(verbatim.Item);
        }
        static void Args_Declarations(Ast.Type type, FSharpList<Ast.DeclElt> declarations, StringBuilder sb)
        {
            //sb.Append("let").Append(" ");
            foreach (var elt in declarations)
            {
                sb.Append(elt.name.Name);
                if (elt != declarations.Last())
                    sb.Append(",");
            }
        }

        static void Handle_Expr(Ast.Expr expr, StringBuilder sb)
        {
            sb.Append(" = 42");
        }
        static void Var_Declarations(Ast.Type type, FSharpList<Ast.DeclElt> declarations, StringBuilder sb)
        {
            sb.Append("let").Append(" ");
            foreach (var elt in declarations)
            {
                sb.Append(elt.name.Name);
                //Handle_Expr(elt.init, sb);
                if (elt != declarations.Last())
                    sb.Append(",");
            }
        }
        static void Stmt_Block(Stmt current, StringBuilder sb)
        {
            Block block = current as Block;
            sb.AppendLine("{");
            foreach(var stmt in block.Item)
            {
                HandleStmt(stmt, sb);
            }
            sb.AppendLine("}");
        }
        static void Stmt_Decl(Stmt current, StringBuilder sb)
        {
            Decl decl = current as Decl;
            Var_Declarations(decl.Item.Item1, decl.Item.Item2, sb);
            sb.AppendLine(";");
        }

        static void HandleStmt(Stmt current, StringBuilder sb)
        {
            Dictionary<System.Type, Action<Stmt, StringBuilder>> match = new Dictionary<System.Type, Action<Stmt, StringBuilder>>
            {
                { typeof(Block),Stmt_Block },
                { typeof(Decl),Stmt_Decl },
                { typeof(Stmt.Expr),null },
                { typeof(If),null },
                { typeof(ForD),null },
                { typeof(ForE),null },
                { typeof(While),null },
                { typeof(DoWhile),null },
                { typeof(Jump),null },
                { typeof(Verbatim),null },
                { typeof(Switch),null },
            };

            Action<Stmt, StringBuilder> call = null;
            if (match.TryGetValue(current.GetType(), out call))
            {
                if (call != null)
                    call(current, sb);
            }
        }
        static void TopLevel_TLFunction(TopLevel tl, StringBuilder sb)
        {
            Function func = tl as Function;
            var decl = func.Item1;
            sb.Append($@"function {decl.fName.Name}(");
            foreach (var arg in decl.args)
            {
                Args_Declarations(arg.Item1, arg.Item2, sb);
                if (arg != decl.args.Last())
                    sb.Append(",");
            }
            sb.AppendLine(")");
            HandleStmt(func.Item2, sb);
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
        public void Print(Ast.Shader shader, StringBuilder sb)
        {
            TopLevel(shader, sb);
        }
    }
}
