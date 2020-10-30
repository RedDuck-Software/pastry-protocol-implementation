// Learn more about F# at http://fsharp.org

open Microsoft.FSharp.Core
open System
open PastryProtocol.Utils
open System.Numerics
open Newtonsoft.Json

(*let rec bin rows myBin add = 
    match rows with 
    | 0 -> myBin |> Seq.map (fun x -> (x, [])) |> dict
    | _ -> 
        let newBin = String.concat myBin add
        bin (rows - 1) newBin add

let entry num maxRequests failurePercentage =
    let rows = match (ceil >> round >> int) <| log num / log 16.0 with 
                | 0 -> 0
                | rows -> rows
    let failedNodes = num * failurePercentage / 100.0 |> round |> int
    let myBin = bin rows "" ( "0123456789ABCDEF".Split "" )
    let network = { Unchecked.defaultof<Network> with 
                        num = num;
                        myBin = myBin;
                        rows = rows;
                        failedNodes = failedNodes;
                        maxRequests = maxRequests;
                        lastRequest = 0;
                }
    //initNetwork network
    network*)

let rnd = System.Random()

[<EntryPoint>]
let main argv =
    let bootIpAddress = BigInteger 80100200500L
    Joining.bootstrapNetwork { IPAddress = bootIpAddress }
    
    for i in 1..10 do
        let newNodeIpAddress = bootIpAddress + BigInteger i
        let (boot, _, _) = TempState.nodeStates |> List.minBy (fun (i, _, _) -> abs <| i.nodeInfo.address - newNodeIpAddress)
        
        printfn "-------------------------------------------------------------------------------"
        printfn "Node %i has joined. New State: %s" (i - 1) <| JsonConvert.SerializeObject(TempState.nodeStates)
        printfn "-------------------------------------------------------------------------------"
        Joining.join boot { IPAddress = newNodeIpAddress } DateTime.UtcNow

    0 // return an integer exit code
