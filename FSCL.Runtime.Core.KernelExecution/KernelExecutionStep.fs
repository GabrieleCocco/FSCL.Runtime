namespace FSCL.Runtime.KernelExecution

open System
open FSCL.Compiler
open FSCL.Runtime

[<Step("FSCL_RUNTIME_EXECUTION_STEP")>]
type KernelExecutionStep(tm: TypeManager,
                         processors: ICompilerStepProcessor list) = 
    inherit CompilerStep<KernelExecutionInput * BufferPoolManager, KernelExecutionOutput>(tm, processors)

    let mutable bufferPoolManager = null
    member this.BufferPoolManager
        with get() =
            bufferPoolManager
        and private set(v) =
            bufferPoolManager <- v

    member this.TryProcess(input:KernelExecutionInput) =
        let mutable index = 0
        let mutable output = None
        while (output.IsNone) && (index < processors.Length) do
            output <- processors.[index].Execute(input, this) :?> KernelExecutionOutput option
            index <- index + 1
        output
        
    member this.Process(input:KernelExecutionInput) =
        let mutable index = 0
        let mutable output = None
        while (output.IsNone) && (index < processors.Length) do
            output <- processors.[index].Execute(input, this) :?> KernelExecutionOutput option
            index <- index + 1
        if output.IsNone then
            raise (KernelExecutionException("The runtime is not able to determine the way to execute kernel " + input.Node.KernelID.Name))
        output.Value

    override this.Run((input, pool)) =
        this.BufferPoolManager <- pool
        let cg = this.Process(input)
        cg