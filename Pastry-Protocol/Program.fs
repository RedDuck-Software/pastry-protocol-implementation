// Learn more about F# at http://fsharp.org

open Microsoft.FSharp.Core
open System
open PastryProtocol.Utils
open System.Numerics
open Newtonsoft.Json
open Akka
open Akka.Util
open Akka.Annotations
open Akka.FSharp
open Akka.FSharp.System
open Serilog
open System.IO
open System.Reflection
open Akka.Actor

let rnd = System.Random()

[<EntryPoint>]
let main argv =
    let logDirectory = Path.Combine(Path.GetTempPath(), Assembly.GetCallingAssembly().FullName)
    if not <| System.IO.Directory.Exists(logDirectory)
        then Directory.CreateDirectory(logDirectory) |> ignore

    let sessionId = Guid.NewGuid().ToString()
    let logFile = sprintf "logs-%s.json" sessionId
    let filePath = Path.Combine(logDirectory, logFile)
    File.Create(filePath).Dispose() |> ignore

    printfn "session ID: %s" sessionId

    let log = (((LoggerConfiguration()).MinimumLevel.Debug()).WriteTo).Console().CreateLogger() //.File(filePath).CreateLogger()
    Serilog.Log.Logger <- log

    ////////////////// LOGGING END //////////////////////
    ///////////////////////////////////////////////////////////////////

    let mutable newNodeIpAddress = BigInteger 80100200500L
    let networkRef = Joining.bootstrapNetwork newNodeIpAddress
    
    for i in 1..30 do
        newNodeIpAddress <- newNodeIpAddress + bigint 1        
        Joining.joinNetwork networkRef newNodeIpAddress

    Console.ReadLine () |> ignore
    0 // return an integer exit code
