﻿// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.
open FSCL.KernelRunner
open FSCL.KernelRunner.MetricTools
open FSCL.KernelRunner.Metric.InstructionEnergyMetric
open FSCL.KernelRunner.Metric.TransferEnergyMetric
open FSCL.Compiler
open FSCL.Compiler.KernelLanguage

open Microsoft.FSharp.Collections
open Microsoft.FSharp.Quotations
    
// Vector addition
[<Kernel(0,0)>][<ReflectedDefinition>]
let VectorAdd(a: float32[], b: float32[], c: float32[], iter: int) =
    let gid = get_global_id(0)
    for i in 0 .. iter - 1 do
        c.[gid] <- c.[gid] + a.[gid]
    
// Matrix multiplication
[<Kernel(0,0)>][<ReflectedDefinition>]
let MatrixMult(a: float32[,], b: float32[,], c: float32[,]) =
    let x = get_global_id(0)
    let y = get_global_id(1)

    let mutable accum = 0.0f
    for k = 0 to a.GetLength(1) - 1 do
        accum <- accum + (a.[x,k] * b.[k,y])
    c.[x,y] <- accum

// Test functions
(*
let testMatrixMultEnergy() =    
    // Create insturction energy metric
    let instructionMetric = InstructionEnergyMetric("131.114.88.115") 
    instructionMetric.DumpFolder <- Some("Dump")
    instructionMetric.MinInstr <- 1
    instructionMetric.MaxInstr <- 10000
    instructionMetric.InstrStep <- (fun i -> i * 2)
    instructionMetric.MinThread <- 1L
    instructionMetric.MaxThread <- (int64)(2 <<< 10)
    instructionMetric.ThreadStep <- (fun i -> i * 2L)
    instructionMetric.PerStepDuration <- 15000.0

    let compiler = new KernelCompiler(instructionMetric)
    let runner = new KernelRunner(compiler)
    compiler.Add(<@ MatrixMult @>) |> ignore
    let matA = Array2D.create 3 2 2.0f 
    let matB = Array2D.create 32 64 2.0f
    let matC = Array2D.zeroCreate<float32> 64 64

    let iterations = 1000
    let ev = instructionMetric.Evaluate([], <@ MatrixMult @>)
    let instr = instructionMetric.Instantiate([], ev, <@ MatrixMult(matA, matB, matC) @>, ([| matA.GetLength(0); matA.GetLength(1) |], [| 8; 8 |]))
    
    let endMsg, time, iterations = Tools.GetEnergyConsumption ("131.114.88.115") ((float)instructionMetric.PerStepDuration) (fun () ->
        runner.Run(<@ MatrixMult(matA, matB, matC) @>, [| matA.GetLength(0); matA.GetLength(1) |], [| 8; 8 |]))
    let avgen = System.Double.TryParse(endMsg.Replace(",", "."))

    let fileName = "MatrixMult_Real.csv"  
    let content = ref "Instructions,AvgEnergy,Duration,Iterations;\n"
    content := !content + instr.ToString() + "," + avgen.ToString() + "," + time.ToString() + "," + iterations.ToString() + ";\n"
    System.IO.File.WriteAllText(fileName, !content)
      *)
      
let testMatMultEnergy() =    

    // Create transfer energy metric
    let transferMetric = TransferEnergyMetric("131.114.88.115") 
    transferMetric.Validate <- true
    transferMetric.DumpFolder <- Some("Dump")
    transferMetric.MinSize <- (1 <<< 10)
    transferMetric.MaxSize <- (32 <<< 20)
    transferMetric.Step <- (1 <<< 20)
    transferMetric.PerStepDuration <- 10000.0
    transferMetric.SrcInfo <- TransferEndpoint()
    transferMetric.DstInfo <- TransferEndpoint()
    transferMetric.SrcInfo.IsHostPtr <- true
    transferMetric.DstInfo.IsHostPtr <- false
    let transfProf = transferMetric.Profile(0, 0)

    // Create insturction energy metric
    let instructionMetric = InstructionEnergyMetric("131.114.88.115") 
    instructionMetric.DumpFolder <- Some("Dump")
    instructionMetric.MinInstr <- 1
    instructionMetric.MaxInstr <- 100000
    instructionMetric.InstrStep <- (fun i -> i * 2)
    instructionMetric.MinThread <- 128L
    instructionMetric.MaxThread <- 2048L
    instructionMetric.ThreadStep <- (fun i -> i * 2L)
    instructionMetric.PerStepDuration <- 10000.0
    let instrProf = instructionMetric.Profile(0, 0)

    // Compile kernel
    let compiler = new KernelCompiler(instructionMetric)
    let runner = new KernelRunner(compiler)
    compiler.Add(<@ MatrixMult @>) |> ignore
    
    let matA = Array2D.create 64 64 2.0f 
    let matB = Array2D.create 64 64 2.0f
    let matC = Array2D.zeroCreate<float32> 64 64
    let currIter = ref 500000
    
    // Setup dump files
    let instrFileName = "MatrixMultInstr.csv"  
    let instrContent = ref "Instructions,Threads,EstimatedEnergy,Watt,Duration,Iterations;\n"
    
    let transfFileName = "MatrixMultTransfer.csv"  
    let transfContent = ref "TransferSize,EnergyPerByte;\n"

    // Instantiate metrics
    let instr, memRead, memWrite = instructionMetric.Instantiate([], instructionMetric.Evaluate([], <@ MatrixMult @>), <@ MatrixMult(matA, matB, matC) @>, ([| matA.GetLength(0); matA.GetLength(1) |], [| 8; 8 |]))
    let transf = transferMetric.Instantiate([], transferMetric.Evaluate([], <@ MatrixMult @>), <@ MatrixMult(matA, matB, matC) @>, ([| matA.GetLength(0); matA.GetLength(1) |], [| 8; 8 |]))
    // Estimate instr energy
    let estimatedInstrEnergy = Tools.InterpolateResults((double)instr, 
                                                        instrProf, 
                                                        fun(currInstr, currThread, v, energyPerInstr, time, iterations) ->
                                                            ((double)currInstr, (double)time, (double)iterations))      
    // Estimate transf energy
    let estimatedTransfEnergy = Tools.InterpolateResults((double)instr, 
                                                         transfProf, 
                                                         fun(transfSize, energyPerByte) ->
                                                            ((double)transf, (double)energyPerByte, (double)1))       
    // Test real consumption   
    let endMsg, time, iterations = Tools.GetEnergyConsumption ("131.114.88.115") ((float)instructionMetric.PerStepDuration) (fun () ->
        runner.Run(<@ MatrixMult(matA, matB, matC) @>, [| matA.GetLength(0); matA.GetLength(1) |], [| 8; 8 |]))

    let watt = System.Double.TryParse(endMsg.Replace(",", "."))

    // Set file content and write
    instrContent := !instrContent + instr.ToString() + "," + ( matA.GetLength(0)* matA.GetLength(1)).ToString() + "," + estimatedInstrEnergy.ToString() + "," + watt.ToString() + "," + time.ToString() + "," + iterations.ToString() + ";\n"
    transfContent := !transfContent + transf.ToString() + "," + estimatedTransfEnergy.ToString() + ";\n"
    currIter := !currIter * 2
    
    System.IO.File.WriteAllText(instrFileName, !instrContent)
    System.IO.File.WriteAllText(transfFileName, !transfContent)

let testVectorAddEnergy() =    
    // Create insturction energy metric
    
    let instructionMetric = InstructionEnergyMetric("131.114.88.115") 
    instructionMetric.DumpFolder <- Some("Dump")
    instructionMetric.MinInstr <- 1000000
    instructionMetric.MaxInstr <- 1000002
    instructionMetric.InstrStep <- (fun i -> i + 2)
    instructionMetric.MinThread <- 2048L
    instructionMetric.MaxThread <- 2048L
    instructionMetric.ThreadStep <- (fun i -> i * 2L)
    instructionMetric.PerStepDuration <- 10000.0
    let prof = instructionMetric.Profile(0, 0)
    
    let compiler = new KernelCompiler(instructionMetric)
    let runner = new KernelRunner(compiler)
    compiler.Add(<@ VectorAdd @>) |> ignore

    let a = Array.create (2 <<< 10) 2.0f 
    let b = Array.create (2 <<< 10) 2.0f
    let c = Array.zeroCreate<float32> (2 <<< 10)
    let currIter = ref 500000
    
    let fileName = "VectorAdd_Real.csv"  
    let content = ref "Instructions,Threads,EstimatedEnergy,Watt,Duration,Iterations;\n"
    
    while(!currIter < 500001) do
        let ev = instructionMetric.Evaluate([], <@ VectorAdd @>)
        let instr, memRead, memWrite = instructionMetric.Instantiate([], ev, <@ VectorAdd(a, b, c, !currIter) @>, ([| a.Length |], [| 128 |]))
        let mutable found = false
        let mutable findIndex = 0
        let mutable prevInstrCount = 0.0
        let mutable nextInstrCount = 0.0
        let mutable prevEnergy = 0.0
        let mutable nextEnergy = 0.0
        while(not found && findIndex < prof.Length) do 
            match prof.[findIndex] with
            | currInstr, currThread, v, energyPerInstr, time, iterations ->
                if((double)currInstr > instr) then
                    found <- true
                    nextEnergy <- (((double)time / 1000.0 / (double)iterations) * (double)v)
                    nextInstrCount <- (double)currInstr
                    if(findIndex > 0) then
                        match prof.[findIndex - 1] with
                        | currInstr, currThread, v, energyPerInstr, time, iterations ->
                            prevEnergy <- (((double)time / 1000.0 / (double)iterations) * (double)v)
                            prevInstrCount <- (double)currInstr
            findIndex <- findIndex + 1
        
        let estimateEnergy = prevEnergy + ((nextEnergy - prevEnergy) * ((double)instr - prevInstrCount) / (nextInstrCount - prevInstrCount))
        // Test real consumption   
        let endMsg, time, iterations = Tools.GetEnergyConsumption ("131.114.88.115") ((float)instructionMetric.PerStepDuration) (fun () ->
            runner.Run(<@ VectorAdd(a, b, c, !currIter) @>, [| a.Length |], [| 128 |]))

        let watt = System.Double.TryParse(endMsg.Replace(",", "."))

        content := !content + instr.ToString() + "," + a.Length.ToString() + "," + estimateEnergy.ToString() + "," + watt.ToString() + "," + time.ToString() + "," + iterations.ToString() + ";\n"
        currIter := !currIter * 2

    System.IO.File.WriteAllText(fileName, !content)
    
[<EntryPoint>]
let main argv =
    testMatMultEnergy()
    (*let runner = new KernelRunner()
    let a = Array.create 2048 2.0f
    let b = Array.create 2048 3.0f
    let c = Array.zeroCreate 2048
    let e = <@ VectorAdd(a,b,c) @>
    runner.Run(e, a.Length, 128)
      *)       
    // Test Generic types and operator overloading
    //runner.Run (<@ a + b @>, [| a.Length |], [| 128 |])
    (*
    // Test conversion with new pipeline
    //let oldel1 = FSCL.KernelBinding.Compile(<@ MatrixMult @>)
    //let oldel = FSCL.KernelBinding.Compile(<@ Reduce @>)
        
    // Dump memory transfer energy profiling
    let transferMetric = TransferEnergyMetric("131.114.88.115") 
    transferMetric.Validate <- true
    transferMetric.DumpFolder <- Some("Dump")
    transferMetric.MinSize <- (1 <<< 10)
    transferMetric.MaxSize <- (32 <<< 20)
    transferMetric.Step <- (1 <<< 20)
    transferMetric.PerStepDuration <- 20000
    transferMetric.SrcInfo <- TransferEnergy.Data.TransferEndpoint()
    transferMetric.DstInfo <- TransferEnergy.Data.TransferEndpoint()
    transferMetric.SrcInfo.IsHostPtr <- true
    transferMetric.DstInfo.IsHostPtr <- false
    for device in ComputePlatform.Platforms.[0].Devices do
        transferMetric.Profile(device) |> ignore
        
    // Test vector addition
    
               
    // Test vector reduction
    let redA = Array.create 1024 10
    let redB = Array.zeroCreate<int> 128
    let redC = Array.zeroCreate<int> 1024
    runner.Run(<@ Reduce(redA, redB, 1024, redC) @>, [| 1024 |], [| 128 |])

    // Test matrix multiplication
    let matA = Array2D.create 64 64 2.0f 
    let matB = Array2D.create 64 64 2.0f
    let matC = Array2D.zeroCreate<float32> 64 64
    runner.Run(<@ MatrixMult(matA, matB, matC) @>, 
               [| matA.GetLength(0); matA.GetLength(1) |], [| 8; 8 |])
    *)
    0
