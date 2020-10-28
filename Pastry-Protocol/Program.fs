// Learn more about F# at http://fsharp.org

open Microsoft.FSharp.Core
open Utils.CSharp

let rec bin rows myBin add = 
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
    network

[<EntryPoint>]
let main argv =
    let hash1 = PastryAPI.getHash "abc"
    let hash2 = PastryAPI.getHash "abl"
    let base1 = GenericBaseConverter.ConvertFromString(hash1, 16)
    let base2 = GenericBaseConverter.ConvertFromString(hash2, 16)

    0 // return an integer exit code
