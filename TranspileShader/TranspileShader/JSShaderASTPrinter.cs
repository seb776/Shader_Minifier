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
    public static class StringBuilderExt
    {
        public static StringBuilder AppendTabs(this StringBuilder self, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                self.Append("\t");
            }
            return self;
        }
    }
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
                    sb.Append(", ");
            }
        }
        static void Expr_Int(Ast.Expr expr, StringBuilder sb)
        {
            Ast.Expr.Int int_ = expr as Ast.Expr.Int;
            sb.Append(int_.Item1);
        }
        static void Expr_Op(Ast.Expr expr, StringBuilder sb)
        {
            Ast.Expr.Op op_ = expr as Ast.Expr.Op;
            sb.Append(op_.Item);
        }
        static void Expr_Var(Ast.Expr expr, StringBuilder sb)
        {
            Ast.Expr.Var var_ = expr as Ast.Expr.Var;
            sb.Append(var_.Item.Name);
        }
        static void Expr_Dot(Ast.Expr expr, StringBuilder sb)
        {
            Ast.Expr.Dot dot = expr as Ast.Expr.Dot;
            Handle_Expr(dot.Item1, sb);
            sb.Append(".");
            sb.Append(dot.Item2);
        }
        static void Expr_Cast(Ast.Expr expr, StringBuilder sb)
        {
            Ast.Expr.Cast cast = expr as Ast.Expr.Cast;
            sb.Append("(");
            sb.Append(cast.Item1.Name);
            sb.Append(")");
            Handle_Expr(cast.Item2, sb);
        }
        static void Expr_Float(Ast.Expr expr, StringBuilder sb)
        {
            Ast.Expr.Float float_ = expr as Ast.Expr.Float;
            sb.Append(float_.Item1);
        }
        static void Expr_FunCall(Ast.Expr expr, StringBuilder sb)
        {

            Ast.Expr.FunCall fun = expr as Ast.Expr.FunCall;
            var isOperator = fun.Item1 is Ast.Expr.Op;
            if (isOperator)
            {   
                if (fun.Item2.Length == 1)
                {
                    Handle_Expr(fun.Item1, sb);
                    Handle_Expr(fun.Item2.First(), sb);
                }
                if (fun.Item2.Length == 2)
                {
                    Handle_Expr(fun.Item2.First(), sb);
                    sb.Append(" ");
                    Handle_Expr(fun.Item1, sb);
                    sb.Append(" ");
                    Handle_Expr(fun.Item2.Last(), sb);
                }
            }
            else
            {
                if (fun.Item1.ToString().Contains("vec"))
                {
                    sb.Append("new ");
                }
                Handle_Expr(fun.Item1, sb);
                sb.Append("(");
                foreach (var exp in fun.Item2)
                {
                    Handle_Expr(exp, sb);
                    if (exp != fun.Item2.Last())
                        sb.Append(", ");
                }
                sb.Append(")");
            }

        }
        static void Expr_Subscript(Ast.Expr expr, StringBuilder sb)
        {
            Ast.Expr.Subscript sub = expr as Ast.Expr.Subscript;
        }
        static void Expr_VectorExp(Ast.Expr expr, StringBuilder sb)
        {
            Ast.Expr.VectorExp vec = expr as Ast.Expr.VectorExp;
            foreach (var exp in vec.Item)
            {
                Handle_Expr(exp, sb);
                if (exp != vec.Item.Last())
                    sb.Append(", ");
            }
        }
        static void Expr_VerbatimExp(Ast.Expr expr, StringBuilder sb)
        {
            Ast.Expr.VerbatimExp exp = expr as Ast.Expr.VerbatimExp;
            sb.Append(exp.Item);
        }
        static void Handle_Expr(Ast.Expr expr, StringBuilder sb)
        {
            Dictionary<System.Type, Action<Ast.Expr, StringBuilder>> match = new Dictionary<System.Type, Action<Ast.Expr, StringBuilder>>
            {
                { typeof(Ast.Expr.Int),Expr_Int },
                { typeof(Ast.Expr.Op),Expr_Op },
                { typeof(Ast.Expr.Var),Expr_Var },
                { typeof(Ast.Expr.Dot),Expr_Dot },
                { typeof(Ast.Expr.Cast),Expr_Cast },
                { typeof(Ast.Expr.Float),Expr_Float },
                { typeof(Ast.Expr.FunCall),Expr_FunCall },
                { typeof(Ast.Expr.Subscript),Expr_Subscript },
                { typeof(Ast.Expr.VectorExp),Expr_VectorExp },
                { typeof(Ast.Expr.VerbatimExp),Expr_VerbatimExp },

            };
            Action<Ast.Expr, StringBuilder> call = null;
            if (match.TryGetValue(expr.GetType(), out call))
            {
                if (call != null)
                    call(expr, sb);
            }
        }
        static void Var_Declarations(Ast.Type type, FSharpList<Ast.DeclElt> declarations, StringBuilder sb)
        {
            sb.Append("let").Append(" ");
            foreach (var elt in declarations)
            {
                sb.Append(elt.name.Name);
                sb.Append(" = ");
                Handle_Expr(elt.init.Value, sb);
                if (elt != declarations.Last())
                    sb.Append(", ");
            }
        }
        static void Stmt_Block(Stmt current, int depth, StringBuilder sb)
        {
            Block block = current as Block;
            sb.AppendTabs(depth).AppendLine("{");
            foreach(var stmt in block.Item)
            {
                HandleStmt(stmt, depth+1, sb);
            }
            sb.AppendTabs(depth).AppendLine("}");
        }
        static void Stmt_Decl(Stmt current, int depth, StringBuilder sb)
        {
            Decl decl = current as Decl;
            sb.AppendTabs(depth);
            Var_Declarations(decl.Item.Item1, decl.Item.Item2, sb);
            sb.AppendLine(";");
        }
        static void Stmt_Expr(Stmt current, int depth, StringBuilder sb)
        {
            Stmt.Expr expr = current as Stmt.Expr;
            sb.AppendTabs(depth);
            Handle_Expr(expr.Item, sb);
            sb.AppendLine(";");
        }
        static void Stmt_If(Stmt current, int depth, StringBuilder sb)
        {
            If if_ = current as If;
            sb.AppendTabs(depth).Append("if (");
            Handle_Expr(if_.Item1, sb);
            sb.AppendLine(")");
            HandleStmt(if_.Item2, depth, sb);
            //HandleStmt(if_.Item3.Value, depth, sb); // TODO
        }
        static void Stmt_ForD(Stmt current, int depth, StringBuilder sb)
        {
            Stmt.ForD ford = current as Stmt.ForD;
            sb.AppendTabs(depth).Append("for (");
            Var_Declarations(ford.Item1.Item1, ford.Item1.Item2, sb);
            sb.Append(";");
            sb.AppendLine(")");
            HandleStmt(ford.Item4, depth, sb);
            //Decl decl = current as Decl;
            //Var_Declarations(decl.Item.Item1, decl.Item.Item2, sb);
            //sb.AppendLine(";");
        }
        static void Stmt_ForE(Stmt current, int depth, StringBuilder sb)
        {
            Stmt.ForE fore = current as Stmt.ForE;
            sb.AppendTabs(depth).Append("for (");
            //Var_Declarations(fore.Item1.Item1, fore.Item1.Item2, sb);
            sb.Append(";");
            Handle_Expr(fore.Item2.Value, sb);
            sb.Append(";");
            sb.AppendLine(")");
            HandleStmt(fore.Item4, depth, sb);
            //Decl decl = current as Decl;
            //Var_Declarations(decl.Item.Item1, decl.Item.Item2, sb);
            //sb.AppendLine(";");
        }
        static void Stmt_While(Stmt current, int depth, StringBuilder sb)
        {
            Stmt.While while_ = current as Stmt.While;
            sb.AppendTabs(depth).Append("while (");
            Handle_Expr(while_.Item1, sb);
            sb.AppendLine(")");
            HandleStmt(while_.Item2, depth, sb);
        }
        static void Stmt_DoWhile(Stmt current, int depth, StringBuilder sb)
        {
            Stmt.DoWhile while_ = current as Stmt.DoWhile;
            sb.AppendTabs(depth).AppendLine("do");
            HandleStmt(while_.Item2, depth, sb);
            sb.AppendTabs(depth).Append("while (");
            Handle_Expr(while_.Item1, sb);
            sb.AppendLine(");");
        }
        static void Stmt_Jump(Stmt current, int depth, StringBuilder sb)
        {
            //Decl decl = current as Decl;
            //Var_Declarations(decl.Item.Item1, decl.Item.Item2, sb);
            //sb.AppendLine(";");
        }
        static void Stmt_Verbatim(Stmt current, int depth, StringBuilder sb)
        {
            //Decl decl = current as Decl;
            //Var_Declarations(decl.Item.Item1, decl.Item.Item2, sb);
            //sb.AppendLine(";");
        }
        static void Stmt_Switch(Stmt current, int depth, StringBuilder sb)
        {
            //Decl decl = current as Decl;
            //Var_Declarations(decl.Item.Item1, decl.Item.Item2, sb);
            //sb.AppendLine(";");
        }
        static void HandleStmt(Stmt current, int depth, StringBuilder sb)
        {
            Dictionary<System.Type, Action<Stmt, int, StringBuilder>> match = new Dictionary<System.Type, Action<Stmt, int, StringBuilder>>
            {
                { typeof(Block),Stmt_Block },
                { typeof(Decl),Stmt_Decl },
                { typeof(Stmt.Expr),Stmt_Expr },
                { typeof(If),Stmt_If },
                { typeof(ForD),Stmt_ForD },
                { typeof(ForE),Stmt_ForE },
                { typeof(While),Stmt_While },
                { typeof(DoWhile),Stmt_DoWhile },
                { typeof(Jump),Stmt_Jump },
                { typeof(Verbatim),Stmt_Verbatim },
                { typeof(Switch),Stmt_Switch },
            };

            Action<Stmt, int, StringBuilder> call = null;
            if (match.TryGetValue(current.GetType(), out call))
            {
                if (call != null)
                    call(current, depth, sb);
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
                    sb.Append(", ");
            }
            sb.AppendLine(")");
            HandleStmt(func.Item2, 0, sb);
        }

        static void TopLevel_TLDecl(TopLevel tl, StringBuilder sb)
        {
            TLDecl tldecl = tl as TLDecl; // TODO

        }
        static void TopLevel_TypeDecl(TopLevel tl, StringBuilder sb)
        {
            TypeDecl typedecl = tl as TypeDecl; // TODO

        }
        static void TopLevel(Ast.Shader shader, StringBuilder sb)
        {
            Dictionary<System.Type, Action<TopLevel, StringBuilder>> match = new Dictionary<System.Type, Action<TopLevel, StringBuilder>>
            {
                { typeof(TLVerbatim),TopLevel_TLVerbatim },
                { typeof(Function),TopLevel_TLFunction },
                { typeof(TLDecl),TopLevel_TLDecl },
                { typeof(TypeDecl),TopLevel_TypeDecl },
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
