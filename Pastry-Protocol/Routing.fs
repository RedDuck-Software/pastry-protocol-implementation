module Routing

open Pastry
open System
open Utils.CSharp

let sharedPrefix (a:Guid) (b:Guid) =
    let aStr = a.ToString()
    let bStr = b.ToString()

    let mutable count = 0
    for i in 0..aStr.Length - 1 do
        if aStr.[i] = bStr.[i] then 
            count <- count + 1
        else ()
    count

let strToDecimal (n:string) = BaseConversion.ArbitraryToDecimalSystem(n, Config.L)
let charToDecimal (n:char) = BaseConversion.ArbitraryToDecimalSystem(n, Config.L)

let guidToDecimal = string >> strToDecimal

let forwardTo nodeInfo = true /// stub

let onMessage (node:Node) (key:Guid) =
    if (List.head node.leaf_set).identifier <= key && key <= (List.last node.leaf_set).identifier
        then 
            let minByDiff = List.minBy (fun i -> abs ((guidToDecimal key) - (guidToDecimal i.identifier))) node.leaf_set
            forwardTo minByDiff
        else
            let l = sharedPrefix key node.node_info.identifier
            let l_in_key_value = key.ToString().[l] |> charToDecimal |> int
            match (node.routing_table.Item l).Item l_in_key_value with
            | Some peer -> forwardTo peer // forward to this address
            | None -> // rare case
                let routingTable = node.routing_table |> List.concat |> List.choose id
                let union = List.concat [node.leaf_set; routingTable; node.neighborhood_set] 
                let predicate1 = (fun (peer:NodeInfo) -> sharedPrefix peer.identifier key >= l)
                let predicate2 = (fun (peer:NodeInfo) -> 
                                    let key10 = guidToDecimal key 
                                    let peer10 = guidToDecimal peer.identifier 
                                    let node10 = guidToDecimal node.node_info.identifier
                                
                                    abs (peer10 - key10) < abs (node10 - key10)
                                )
                let T = List.find (fun i -> predicate1 i && predicate2 i) union
                forwardTo T