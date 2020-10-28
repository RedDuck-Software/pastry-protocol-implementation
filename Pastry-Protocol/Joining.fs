module Joining

open System.Text
open System.Linq
open System
open CSharp.Utils
open PastryProtocol.Types
open PastryProtocol

let sha = System.Security.Cryptography.SHA1.Create()

let getHash (input:string) = 
    let byteHash = sha.ComputeHash(Encoding.UTF8.GetBytes(input))
    let hex = System.BitConverter.ToString(byteHash).Where(fun i -> i <> '-').ToArray() |> String
    let bigIntHash = GenericBaseConverter.ConvertFromString(hex, 16) // probably it should be b? 
    let baseHash = GenericBaseConverter.ConvertToString(bigIntHash, Config.L)
    
    baseHash

let getNewNodeInfo (bootNode:Node) credentials = 
    let nodeId = getHash credentials.IPAddress

    match bootNode.routing_table with 
    | Initialized table -> 
        let routingTable = [|Some(table.First())|]

        {
            nodeInfo = { address = credentials.IPAddress; identifier = nodeId }
            routing_table = Uninitialized(routingTable);
            leaf_set = { 
                left = Array.init (Config.leafSize / 2) (fun _ -> None); 
                right = Array.init (Config.leafSize / 2) (fun _ -> None);
            }
            neighborhood_set = Array.init Config.neighborhoodSize (fun _ -> None)
        }
    | Uninitialized _ -> raise <| invalidOp("node with uninitialized routingTable cannot be used as a boot node")

let join (hostNode:Node) credentials =
    let newNode = getNewNodeInfo hostNode credentials
    let nodeInfo = Routing.getForwardToNode hostNode newNode.nodeInfo.identifier
    
    match nodeInfo with 
    | Some nextNode -> 
        let message = {
            requestNumber = 1; // first request was to this node
            data = Join(newNode.nodeInfo.identifier);
            prev_peer = None;
            request_initiator = newNode.nodeInfo;
            Message.timestampUTC = DateTime.UtcNow
        }

        Routing.sendMessage hostNode nextNode message
    | None -> // this appears to be the closest node already lol...
        Routing.sendMessage hostNode newNode.nodeInfo {
            requestNumber = 1; 
            prev_peer = None; 
            request_initiator = hostNode.nodeInfo; 
            data = LeafSet(hostNode.leaf_set);
            timestampUTC = DateTime.UtcNow;
        }