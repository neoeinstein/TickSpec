﻿module internal TickSpec.ScenarioGen

open System
open System.Collections.Generic
open System.Reflection
open System.Reflection.Emit
        
/// Defines scenario type
let defineScenarioType 
        (module_:ModuleBuilder) 
        (scenarioName) =
    module_.DefineType(
        scenarioName,
        TypeAttributes.Public ||| TypeAttributes.Class)
        
/// Defines _provider field
let defineProviderField
        (scenarioBuilder:TypeBuilder) =
    scenarioBuilder.DefineField(
        "_provider",
        typeof<IServiceProvider>,
        FieldAttributes.Private ||| FieldAttributes.InitOnly)

/// Defines Constructor
let defineCons 
        (scenarioBuilder:TypeBuilder)
        (providerField:FieldBuilder)        
        (parameters:(string * string)[]) =
    let cons = 
        scenarioBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [|typeof<System.IServiceProvider>|])    
    let gen = cons.GetILGenerator()     
    gen.Emit(OpCodes.Ldarg_0);
    gen.Emit(OpCodes.Call, typeof<obj>.GetConstructor(Type.EmptyTypes))

    // Emit provider field    
    gen.Emit(OpCodes.Ldarg_0)
    gen.Emit(OpCodes.Ldarg_1)
    gen.Emit(OpCodes.Stfld,providerField)    
    
    // Emit example parameters
    parameters |> Seq.iter (fun (name,value) ->
        let field =
            scenarioBuilder.DefineField(
                name,
                typeof<string>,
                FieldAttributes.Private ||| FieldAttributes.InitOnly)        
        gen.Emit(OpCodes.Ldarg_0)
        gen.Emit(OpCodes.Ldstr,value)
        gen.Emit(OpCodes.Stfld,field)        
    )    
    // Emit return
    gen.Emit(OpCodes.Ret)
        
/// Emits table argument    
let emitTable
        (gen:ILGenerator)
        (table:Table) =
        
    let local0 = gen.DeclareLocal(typeof<string[]>).LocalIndex
    let local1 = gen.DeclareLocal(typeof<string[][]>).LocalIndex

    // Define header
    gen.Emit(OpCodes.Ldc_I4, table.Header.Length)
    gen.Emit(OpCodes.Newarr,typeof<string>)
    gen.Emit(OpCodes.Stloc,local0)
    // Fill header
    table.Header |> Seq.iteri (fun i s ->
        gen.Emit(OpCodes.Ldloc,local0)
        gen.Emit(OpCodes.Ldc_I4, i)
        gen.Emit(OpCodes.Ldstr,s)
        gen.Emit(OpCodes.Stelem_Ref)
    )
    gen.Emit(OpCodes.Ldloc,local0)
    // Define rows
    gen.Emit(OpCodes.Ldc_I4,table.Rows.Length)
    gen.Emit(OpCodes.Newarr,typeof<string[]>)
    gen.Emit(OpCodes.Stloc,local1)
    // Fill rows
    table.Rows |> Seq.iteri (fun y row ->
        // Define row
        gen.Emit(OpCodes.Ldloc,local1)
        gen.Emit(OpCodes.Ldc_I4,y)
        gen.Emit(OpCodes.Ldc_I4,row.Length)
        gen.Emit(OpCodes.Newarr,typeof<string>)
        gen.Emit(OpCodes.Stloc,local0)
        // Fill columns
        row |> Seq.iteri (fun x col ->
            gen.Emit(OpCodes.Ldloc,local0)
            gen.Emit(OpCodes.Ldc_I4,x)
            gen.Emit(OpCodes.Ldstr,col)
            gen.Emit(OpCodes.Stelem_Ref)
        )
        gen.Emit(OpCodes.Ldloc,local0)
        gen.Emit(OpCodes.Stelem_Ref)
    )
    // Instantiate table
    gen.Emit(OpCodes.Ldloc,local1)
    let ci = 
        typeof<Table>.GetConstructor(
            [|typeof<string[]>;typeof<string[][]>|])
    gen.Emit(OpCodes.Newobj,ci)
    
/// Emit instance of specified type (obtained from service provider)
let emitInstance (gen:ILGenerator) (providerField:FieldBuilder) (t:Type) =     
    gen.Emit(OpCodes.Ldarg_0)
    gen.Emit(OpCodes.Ldfld,providerField)
    gen.Emit(OpCodes.Ldtoken,t)
    let getType = 
        typeof<Type>.GetMethod("GetTypeFromHandle",
            [|typeof<RuntimeTypeHandle>|])
    gen.EmitCall(OpCodes.Call,getType,null) 
    let getService =
        typeof<System.IServiceProvider>
            .GetMethod("GetService",[|typeof<Type>|])
    gen.EmitCall(OpCodes.Callvirt,getService,null)
    gen.Emit(OpCodes.Unbox_Any,t)    
                        
/// Emits type argument
let emitType (gen:ILGenerator) (t:Type) =
    gen.Emit(OpCodes.Ldtoken,t)
    let mi =
        typeof<Type>.GetMethod("GetTypeFromHandle",
            [|typeof<RuntimeTypeHandle>|])
    gen.EmitCall(OpCodes.Call,mi,null)
                        
/// Emits conversion function
let emitConvert (gen:ILGenerator) (t:Type) (x:string) =
    // Emit: System.Convert.ChangeType(arg,typeof<specified parameter>)
    gen.Emit(OpCodes.Ldstr, x)
    emitType gen t
    let invariant =
        typeof<System.Globalization.CultureInfo>.GetMethod("get_InvariantCulture")
    gen.EmitCall(OpCodes.Call,invariant,null)
    gen.Emit(OpCodes.Unbox_Any, typeof<IFormatProvider>)
    let changeType =
        typeof<Convert>.GetMethod("ChangeType",
            [|typeof<obj>;typeof<Type>;typeof<IFormatProvider>|])
    gen.EmitCall(OpCodes.Call,changeType,null)
    // Emit cast to parameter type
    gen.Emit(OpCodes.Unbox_Any, t)
    
/// Emits value    
let emitValue 
        (gen:ILGenerator) 
        (providerField:FieldBuilder)
        (parsers:IDictionary<Type,MethodInfo>) 
        (paramType:Type) 
        (arg:string) =
    let hasParser, parser = parsers.TryGetValue(paramType)
    if hasParser then           
        gen.Emit(OpCodes.Ldstr,arg)
        if not parser.IsStatic then
            emitInstance gen providerField parser.DeclaringType
        gen.EmitCall(OpCodes.Call,parser,null)  
    elif paramType = typeof<string> then
        gen.Emit(OpCodes.Ldstr,arg) // Emit string argument    
    elif paramType.IsEnum then
        // Emit: System.Enum.Parse(typeof<specified argument>,arg)
        emitType gen paramType
        gen.Emit(OpCodes.Ldstr,arg)
        let mi = 
            typeof<Enum>.GetMethod("Parse", 
                [|typeof<Type>;typeof<string>|])
        gen.EmitCall(OpCodes.Call,mi,null)
        // Emit cast to parameter type
        gen.Emit(OpCodes.Unbox_Any,paramType)   
    else
        emitConvert gen paramType arg
        
/// Emits array
let emitArray 
        (gen:ILGenerator)
        (providerField:FieldBuilder)
        (parsers:IDictionary<Type,MethodInfo>)  
        (paramType:Type) 
        (vs:string[]) =
    let t = paramType.GetElementType()
    // Define local variable
    let local = gen.DeclareLocal(paramType).LocalIndex
    // Define array
    gen.Emit(OpCodes.Ldc_I4, vs.Length)
    gen.Emit(OpCodes.Newarr,t)
    gen.Emit(OpCodes.Stloc, local)
    // Set array values
    vs |> Seq.iteri (fun i x ->
        gen.Emit(OpCodes.Ldloc, local)
        gen.Emit(OpCodes.Ldc_I4,i)
        emitValue gen providerField parsers t x
        gen.Emit(OpCodes.Stelem,t)
    )
    gen.Emit(OpCodes.Ldloc, local)
        
/// Emits argument
let emitArgument
        (gen:ILGenerator)
        (providerField:FieldBuilder)
        (parsers:IDictionary<Type,MethodInfo>) 
        (arg:string,param:ParameterInfo) =
        
    let paramType = param.ParameterType                      
    if paramType.IsArray then
        let vs =
            if String.IsNullOrEmpty(arg.Trim()) then [||]
            else arg.Split [|','|] |> Array.map (fun x -> x.Trim())
        emitArray gen providerField parsers paramType vs
    else
        emitValue gen providerField parsers paramType arg                
        
/// Defines step method
let defineStepMethod
        doc        
        (scenarioBuilder:TypeBuilder)
        (providerField:FieldBuilder)
        (parsers:IDictionary<Type,MethodInfo>)
        (line:Line,mi:MethodInfo,args:string[]) =    
    /// Line number
    let n = line.Number
    /// Step method builder
    let stepMethod = 
        scenarioBuilder.DefineMethod(sprintf "%d: %s" n line.Text,
            MethodAttributes.Public,
            typeof<Void>,
             [||])
    /// Step method ILGenerator
    let gen = stepMethod.GetILGenerator()
    // Set marker in source document    
    gen.MarkSequencePoint(doc,n,1,n,line.Text.Length+1)
    // For instance methods get instance value from service provider
    if not mi.IsStatic then
        emitInstance gen providerField mi.DeclaringType
    // Emit arguments
    let ps = mi.GetParameters()
    Seq.zip args ps
    |> Seq.iter (emitArgument gen providerField parsers)
    // Emit bullets argument
    line.Bullets |> Option.iter (fun x ->
        let t = (ps.[ps.Length-1].ParameterType)
        emitArray gen providerField parsers t x
    )
    // Emit table argument
    line.Table |> Option.iter (emitTable gen)
    // Emit method invoke
    if mi.IsStatic then
        gen.EmitCall(OpCodes.Call, mi, null)
    else
        gen.EmitCall(OpCodes.Callvirt, mi, null)
    // Emit return
    gen.Emit(OpCodes.Ret);
    // Return step method
    stepMethod
    
/// Defines Run method
let defineRunMethod
    (scenarioBuilder:TypeBuilder)
    (stepMethods:seq<MethodBuilder>) =
    /// Run method to execute all scenario steps
    let runMethod =
        scenarioBuilder.DefineMethod("Run",
            MethodAttributes.Public,
            typeof<Void>,
            [||])                           
    /// Run method ILGenerator
    let gen = runMethod.GetILGenerator()
    // Execute steps
    stepMethods |> Seq.iter (fun stepMethod ->
        gen.Emit(OpCodes.Ldarg_0)
        gen.EmitCall(OpCodes.Callvirt,stepMethod,null)
    )
    // Emit return
    gen.Emit(OpCodes.Ret)
                
/// Generates Type for specified Scenario
let generateScenario 
        (module_:ModuleBuilder)
        doc
        (parsers:IDictionary<Type,MethodInfo>)
        (scenarioName,lines:(Line * MethodInfo * string[]) [],
         parameters:(string * string)[]) =
    
    let scenarioBuilder =
        defineScenarioType module_ scenarioName
    
    let providerField = defineProviderField scenarioBuilder                    
    
    defineCons scenarioBuilder providerField parameters
               
    /// Scenario step methods
    let stepMethods =
        lines 
        |> Array.map (defineStepMethod doc scenarioBuilder providerField parsers)
        
    defineRunMethod scenarioBuilder stepMethods 
    
    /// Return scenario
    scenarioBuilder.CreateType()