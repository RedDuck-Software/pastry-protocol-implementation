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
open PastryProtocol.Types
open System.Threading

let rnd = System.Random()

[<EntryPoint>]
let main argv =
    let numNodes = int argv.[0]
    let numRequests = int argv.[1]
    let logDirectory = Path.Combine(Path.GetTempPath(), Assembly.GetCallingAssembly().FullName)
    if not <| System.IO.Directory.Exists(logDirectory)
        then Directory.CreateDirectory(logDirectory) |> ignore

    let sessionId = Guid.NewGuid().ToString()
    let logFile = sprintf "logs-%s.json" sessionId
    let filePath = Path.Combine(logDirectory, logFile)
    File.Create(filePath).Dispose() |> ignore

    printfn "session ID: %s" sessionId

    let log = (((LoggerConfiguration()).MinimumLevel.Debug()).WriteTo).File(filePath).CreateLogger()
    Serilog.Log.Logger <- log

    ////////////////// LOGGING END //////////////////////
    ///////////////////////////////////////////////////////////////////

    let mutable s = numRequests / 2
    let mutable newNodeIpAddress = BigInteger 80100200500L
    let networkRef = Actors.bootstrapNetwork newNodeIpAddress
    
    // network join
    for i in 2..numNodes do
        System.Threading. Thread.Sleep(1000) // give it a second to initialize
        newNodeIpAddress <- newNodeIpAddress + bigint 1        
        Actors.joinNetwork networkRef newNodeIpAddress

    // test messages + calculate hops    
    let testingSessionKey = "random"
    networkRef <! HopsTestingRequest({ HopsTestingRequest.hopsTestingData = { sessionKey = testingSessionKey }; sendersCount = numRequests })

    Console.ReadLine () |> ignore
    0 // return an integer exit code
