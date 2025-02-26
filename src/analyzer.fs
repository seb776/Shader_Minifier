﻿module Analyzer

open System.Collections.Generic
open Ast
open Options.Globals

// The module performs some static analysis on the code and stores the
// information in the AST nodes, e.g. find which variables are modified,
// which declarations can be inlined.

module private VariableInlining =

    // Return the list of variables used in the statements, with the number of references.
    let countReferences stmtList = // INCORRECT: the counts of shadowing variables are merged!
        let counts = Dictionary<string, int>()
        let collectLocalUses _ = function
            | Var v as e ->
                counts.[v.Name] <- match counts.TryGetValue(v.Name) with _, n -> n + 1
                e
            | e -> e
        for expr in stmtList do
            mapStmt (mapEnv collectLocalUses id) expr |> ignore<MapEnv * Stmt>
        counts

    let collectReferencesSet expr  = // INCORRECT: the shadowing variables are merged! they might hide a writing reference!
        let result = HashSet<Ident>()
        let collectLocalUses _ = function
            | Var v as e -> result.Add(v) |> ignore<bool>; e
            | e -> e
        mapExpr (mapEnv collectLocalUses id) expr |> ignore<Expr>
        result

    let isEffectivelyConst (ident: Ident) =
        match ident.Declaration with
        | Declaration.Variable varDecl ->
            // A variable not reassigned is effectively constant.
            not varDecl.isEverWrittenAfterDecl
        | Declaration.Func funDecl ->
            not funDecl.hasExternallyVisibleSideEffects
        | Declaration.Unknown -> Builtin.pureBuiltinFunctions.Contains ident.Name

    // Mark variables as inlinable when possible.
    // Variables are always safe to inline when all of:
    //  - the variable is used only once in the current block
    //  - the variable is not used in a sub-block (e.g. inside a loop), for runtime performance
    //  - the init value refers only to variables that are never written to, and functions that are builtin and pure
    let markSafelyInlinableLocals block =
        // Variables that are defined in this scope.
        // The boolean indicate if the variable initialization is const.
        let localDefs = Dictionary<string, (Ident * bool)>()
        // List of all expressions in the current block. Do not look in sub-blocks.
        let mutable localExpr = []
        for stmt: Stmt in block do
            match stmt with
            | Decl (_, declElts) ->
                for def in declElts do
                    // can only inline if it has a value
                    match def.init with
                    | None -> ()
                    | Some init ->
                        localExpr <- init :: localExpr
                        let deps = collectReferencesSet init
                        let isConst = deps |> Seq.forall isEffectivelyConst
                        localDefs.[def.name.Name] <- (def.name, isConst)
            | Expr e
            | Jump (_, Some e) -> localExpr <- e :: localExpr
            | Verbatim _ | Jump (_, None) | Block _ | If _| ForE _ | ForD _ | While _ | DoWhile _ | Switch _ -> ()

        let localReferences = countReferences [for e in localExpr -> Expr e]
        let allReferences = countReferences block

        for def in localDefs do
            let ident, isConst = def.Value
            if not ident.DoNotInline && not ident.ToBeInlined && not ident.VarDecl.Value.isEverWrittenAfterDecl then
                match localReferences.TryGetValue(def.Key), allReferences.TryGetValue(def.Key) with
                | (_, 1), (_, 1) when isConst -> ident.ToBeInlined <- true
                | (_, 0), (_, 0) -> ident.ToBeInlined <- true
                | _ -> ()

    let isTrivialExpr = function
        | Var v when v.Name = "true" || v.Name = "false" -> true
        | Int _
        | Float _ -> true
        | _ -> false

    // Detect if a variable can be inlined, based on its value.
    let canBeInlined (init: Expr) =
        match init with
        | e when isTrivialExpr e -> true
        | _ when not options.aggroInlining -> false
        // Allow a few things to be inlined with aggroInlining
        | Var v
        | Dot (Var v, _) -> isEffectivelyConst v
        | FunCall(Op op, args) ->
            not (Builtin.assignOps.Contains op) &&
                args |> List.forall isTrivialExpr
        | FunCall(Var fct, args) ->
            Builtin.pureBuiltinFunctions.Contains fct.Name &&
                args |> List.forall isTrivialExpr
        | _  -> false

    // Inline global or local variables, regardless of where they are used or how often they are used, when all of:
    //  - it is not external
    //  - it is never written after declaration
    //  - it is either:
    //      - an uninitialized local (remove it). this breaks the shader if the local is read.
    //      - the init value is a simple constant, or with aggro inlining, it uses only builtin functions and variables never written to.
    let markUnwrittenVariablesWithSimpleInit isTopLevel = function
        | (ty: Type, defs) when not ty.IsExternal ->
            for (def:DeclElt) in defs do
                if not def.name.DoNotInline && not def.name.VarDecl.Value.isEverWrittenAfterDecl then
                    match def.init with
                    | None ->
                        // Top-level values are special, in particular in HLSL. Keep them for now.
                        if not isTopLevel then
                            // Never-written locals without init should be unused: inline (remove) them.
                            def.name.ToBeInlined <- true
                    | Some init ->
                        if canBeInlined init then
                            // Never-written locals and globals are inlined when their value is "simple enough".
                            // This can increase non-compressed size but decreases compressed size.
                            def.name.ToBeInlined <- true
        | _ -> ()

    let markInlinableVariables li =
        let mapStmt stmt =
            match stmt with
            | Decl d -> markUnwrittenVariablesWithSimpleInit false d
            | ForD (d, _, _, _) -> markUnwrittenVariablesWithSimpleInit false d
            | Block b -> markSafelyInlinableLocals b
            | _ -> ()
            stmt
        // Visit locals
        mapTopLevel (mapEnv (fun _ -> id) mapStmt) li |> ignore<TopLevel list>
        // Visit globals
        for tl in li do
            match tl with
            | TLDecl d -> markUnwrittenVariablesWithSimpleInit true d; ()
            | _ -> ()
        ()

let markInlinableVariables = VariableInlining.markInlinableVariables

let markWrites topLevel =
    let findWrites (env: MapEnv) = function
        | Var v as e when env.isInWritePosition && v.VarDecl <> None ->
            v.VarDecl.Value.isEverWrittenAfterDecl <- true
            v.isVarWrite <- true
            e
        | FunCall(Var v, args) as e ->
            match v.Declaration with
            | Declaration.Func funcDecl when funcDecl.funcType.hasOutOrInoutParams ->
                // Writes through assignOps are already handled by mapEnv,
                // but we also need to handle variable writes through "out" or "inout" parameters.
                for arg, (ty, _) in List.zip args funcDecl.funcType.args do
                    let newEnv = if ty.isOutOrInout then {env with isInWritePosition = true} else env
                    (mapExpr newEnv arg: Expr) |> ignore<Expr>
                e
            | _ -> e
        | e -> e
    mapTopLevel (mapEnv findWrites id) topLevel |> ignore<TopLevel list>

    let findExternallyVisibleSideEffect tl =
        let mutable hasExternallyVisibleSideEffect = false
        let findSideEffects _ = function
            | Var v as e ->
                let hasSideEffect =
                    match v.Declaration with
                    | Declaration.Variable d ->
                        match d.scope with
                        | VarScope.Global -> v.isVarWrite
                        | VarScope.Parameter -> d.ty.isOutOrInout
                        | VarScope.Local -> false
                    // functions are processed in order, so this is initialized before use
                    | Declaration.Func f -> f.hasExternallyVisibleSideEffects
                    | _ -> true
                hasExternallyVisibleSideEffect <- hasExternallyVisibleSideEffect || hasSideEffect
                e
            | e -> e
        mapTopLevel (mapEnv findSideEffects id) [tl] |> ignore<TopLevel list>
        hasExternallyVisibleSideEffect

    for tl in topLevel do
        match tl with
        | Function (ft, _) ->
            match ft.fName.Declaration with
            | Declaration.Func f ->
                f.hasExternallyVisibleSideEffects <- findExternallyVisibleSideEffect tl
            | _ -> ()
        | _ -> ()

// Create an ident.Declaration for each declaration in the file.
// Give each Ident a reference to that Declaration.
let resolve topLevel =
    let resolveExpr (env: MapEnv) = function
        | FunCall (Var v, args) as e ->
            match env.fns.TryFind (v.Name, args.Length) with
            | Some [(ft, _)] -> v.Declaration <- ft.fName.Declaration
            | None -> () // TODO: resolve builtin functions
            | _ -> () // TODO: support type-based disambiguation of user-defined function overloading
            e
        | Var v as e ->
            match env.vars.TryFind v.Name with
            | Some (_, decl) -> v.Declaration <- decl.name.Declaration
            | _ -> ()
            e
        | e -> e

    let resolveDecl scope (ty, li) =
        for elt in li do
            let varDecl = new VarDecl(ty, elt, scope)
            elt.name.Declaration <- Declaration.Variable varDecl

    let resolveStmt = function
        | Decl d as stmt -> resolveDecl VarScope.Local d; stmt
        | ForD(d, _, _, _) as stmt -> resolveDecl VarScope.Local d; stmt
        | x -> x

    let resolveGlobalsAndParameters = function
        | TLDecl decl -> resolveDecl VarScope.Global decl
        | Function (funcType, _) ->
            for decl in funcType.args do resolveDecl VarScope.Parameter decl
            funcType.fName.Declaration <- Declaration.Func (new FunDecl(funcType))
        | _ -> ()

    // First visit all declarations, creating them.
    for tl in topLevel do
        resolveGlobalsAndParameters tl
    mapTopLevel (mapEnv (fun _ -> id) resolveStmt) topLevel |> ignore<TopLevel list>
    // Then, visit all uses and associate them to their declaration.
    mapTopLevel (mapEnv resolveExpr id) topLevel |> ignore<TopLevel list>


module private FunctionInlining =

    // Gets the list of call sites in this function
    type CallSite = {
        ident: Ident
        varsInScope: string list
        prototype: string * int
    }
    let findCallSites block =
        let callSites = List()
        let collect (mEnv : MapEnv) = function
            | FunCall (Var id, argExprs) as e ->
                callSites.Add { ident = id; varsInScope = mEnv.vars.Keys |> Seq.toList; prototype = (id.Name, argExprs.Length) }
                e
            | e -> e
        mapStmt (mapEnv collect id) block |> ignore<MapEnv * Stmt>
        callSites |> Seq.toList

    type FuncInfo = {
        func: TopLevel
        funcType: FunctionType
        body: Stmt
        name: string
        callSites: CallSite list // calls to other user-defined functions, from inside this function.
    }
    let findFuncInfos code =
        let functions = code |> List.choose (function
            | Function(funcType, block) as f -> Some (funcType, funcType.fName.Name, block, f)
            | _ -> None)
        let funcInfos = functions |> List.map (fun (funcType, name, block, f) ->
            let callSites = findCallSites block
                            // only return calls to user-defined functions
                            |> List.filter (fun callSite -> functions |> List.exists (fun (ft,_,_,_) -> callSite.prototype = ft.prototype))
            { FuncInfo.func = f; funcType = funcType; name = name; callSites = callSites; body = block })
        funcInfos

    // To ensure correctness, we verify if it's safe to inline.
    //
    // [A] Only inline a function if it never refers to a global by a name function or variable that is shadowed by a local variable in scope at the call site.
    // [B] Only inline a function if it has only one call site.
    //     Exception: if the body is "trivial" it will be inlined at all call sites.
    // [C] Only inline a function if it is a single expression return.
    //     This also ensures the function does not declare any locals.
    // [D] Only inline a function if it uses its 'in' parameters at most once.
    //     No attempt is made to inline in other cases. For example, it would be correct to inline
    //     when an 'in' parameter is read more than once but is an expression without side-effects,
    //     or when the parameter is written but the argument is a lvalue that doesn't make any side effect and is not used after the call site.
    //     Repeating the expression could increase the shader size or decrease run time performance.
    // [E] Only inline a function if its 'in' parameters are never written to (through assignOps or calling an out or inout function or operator).
    //     No attempt is made to find if the passed argument is an lvalue that's never used after calling the function to inline.
    //     No attempt is made to copy the argument into a newly declared local variable at the call site to get correct writing semantics.
    // [F] Only inline a function if it has no 'out' or 'inout' parameters.
    //     'out' or 'inout' parameters must be lvalues, which simplifies things. But there are problems to watch out for.
    //     Evaluating them could have side effects (e.g. a[b++]), which is a problem if they are used more than once.
    //     If the 'out' parameters are read from, inlining can change the behavior.
    //     It's fine if 'out' parameters are written in more than one place.
    let verifyArgsUses func callSites =
        let argUsageCounts = Dictionary<string, int>()
        let mutable shadowedGlobal = false
        let mutable argIsWritten = false

        let visitArgUses _ = function
            | Var id as e when id.VarDecl <> None ->
                match id.VarDecl.Value.scope with
                | VarScope.Local ->
                    failwith "There shouldn't be any locals in a function with a single return statement."
                | VarScope.Parameter ->
                    argIsWritten <- argIsWritten || id.VarDecl.Value.isEverWrittenAfterDecl
                    argUsageCounts.[id.Name] <- match argUsageCounts.TryGetValue(id.Name) with _, n -> n + 1
                | VarScope.Global ->
                    shadowedGlobal <- shadowedGlobal || (callSites |> List.exists (fun callSite ->
                        callSite.varsInScope |> List.contains id.Name))
                e
            | e -> e
        mapTopLevel (mapEnv visitArgUses id) [func] |> ignore<TopLevel list>

        let argsAreUsedAtMostOnce = not (argUsageCounts.Values |> Seq.exists (fun n -> n > 1))
        let ok =
            argsAreUsedAtMostOnce && // [D]
            not argIsWritten && // [E]
            not shadowedGlobal // [A]
        ok

    let tryMarkFunctionToInline funcInfo callSites =
        if not funcInfo.funcType.fName.DoNotInline && verifyArgsUses funcInfo.func callSites then
            // Mark both the call site (so that simplifyExpr can remove it) and the function (to remember to remove it).
            // We cannot simply rely on unused functions removal, because it might be disabled through its own flag.
            for callSite in callSites do
                callSite.ident.ToBeInlined <- true
            funcInfo.funcType.fName.ToBeInlined <- true

    let markInlinableFunctions code =
        let funcInfos = findFuncInfos code
        for funcInfo in funcInfos do
            let canBeRenamed = not (options.noRenamingList |> List.contains funcInfo.name) // noRenamingList includes "main"
            let isExternal = options.hlsl && funcInfo.funcType.semantics <> []
            let isOverloadedAmbiguously = funcInfos |> List.except [funcInfo] |> List.exists (fun f -> f.funcType.prototype = funcInfo.funcType.prototype)
            if canBeRenamed && not isExternal && not isOverloadedAmbiguously then
                if not funcInfo.funcType.hasOutOrInoutParams then // [F]
                    // Find calls to this function. This works because we checked that the function is not overloaded ambiguously.
                    let callSites = funcInfos |> List.collect (fun n -> n.callSites)
                                              |> List.filter (fun callSite -> callSite.prototype = funcInfo.funcType.prototype)
                    if callSites.Length > 0 then // Unused function elimination is not handled here
                        match funcInfo.body with
                        | Jump (JumpKeyword.Return, Some body)
                        | Block [Jump (JumpKeyword.Return, Some body)] -> // [C]
                            if callSites.Length = 1 || VariableInlining.isTrivialExpr body then // [B]
                                tryMarkFunctionToInline funcInfo callSites
                        | _ -> ()

let markInlinableFunctions = FunctionInlining.markInlinableFunctions
