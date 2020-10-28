module Joining

open Pastry
open Utils.CSharp
open System.Collections.Generic
open System.Text
open System.Linq
open System

let sha = System.Security.Cryptography.SHA1.Create()

let getHash (input:string) = 
    let byteHash = sha.ComputeHash(Encoding.UTF8.GetBytes(input))
    let hex = System.BitConverter.ToString(byteHash).Where(fun i -> i <> '-').ToArray() |> String
    let bigIntHash = GenericBaseConverter.ConvertFromString(hex, 16) // probably it should be b? 
    let baseHash = GenericBaseConverter.ConvertToString(bigIntHash, Config.L)
    
    baseHash

let getNewNodeInfo (hostNode:Node) credentials = 
    let nodeId = getHash credentials.IPAddress
    let routingTable = [|hostNode.routing_table.First()|]
    
    {
        node_info = { address = credentials.IPAddress; identifier = nodeId }
        routing_table = routingTable;
        leaf_set = Array.init Config.leafSize (fun _ -> None)
        neighborhood_set = Array.init Config.neighborhoodSize (fun _ -> None)
    }

let join (hostNode:Node) credentials =
    let newNode = getNewNodeInfo hostNode credentials
    let nodeInfo = Routing.getForwardToNode hostNode newNode.node_info.address
    
    match nodeInfo with 
    | Some nextNode -> 
        let message = {
            requestNumber = 1; // first request was to this node
            data = Join(newNode.node_info.address);
            prev_peer = hostNode.node_info;
            request_initiator = newNode.node_info;
        }

        Routing.sendMessage hostNode nextNode message
    | None -> // this appears to be the closest node..
        Routing.sendMessage hostNode newNode.node_info { 
            requestNumber = 1; 
            prev_peer = hostNode.node_info; 
            request_initiator = hostNode.node_info; 
            data = LeafSet(hostNode.leaf_set) 
        }
