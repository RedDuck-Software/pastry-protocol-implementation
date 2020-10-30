module Joining

open System.Text
open System.Linq
open System
open PastryProtocol.Types
open PastryProtocol.Utils
open PastryProtocol

let sha = System.Security.Cryptography.SHA1.Create()

let getHash (input:string) = 
    let byteHash = sha.ComputeHash(Encoding.UTF8.GetBytes(input))
    let hex = System.BitConverter.ToString(byteHash).Where(fun i -> i <> '-').ToArray() |> String 

    let length = if hex.Length > 128 / 4 then 128 / 4 else hex.Length
    let trimmedHex = hex.Substring(0, length)

    let requiredDigitsCount = 128 / Config.b

    let bigIntHash = convertBaseFrom 16 trimmedHex
    let baseHash = convertBaseTo Config.numberBase bigIntHash
    
    let remainingDigits = requiredDigitsCount - baseHash.Length

    let paddedZeros = Array.init remainingDigits (fun i -> '0') |> String
    paddedZeros + baseHash

let getNewNodeInfo routingTable neighborhoodset credentials =
    let nodeId = getHash <| convertBaseTo Config.numberBase  credentials.IPAddress
    {
        nodeInfo = { address = credentials.IPAddress; identifier = nodeId }
        routing_table = routingTable;
        leaf_set = { 
            left = Array.init (Config.leafSize / 2) (fun _ -> None); 
            right = Array.init (Config.leafSize / 2) (fun _ -> None);
        }
        neighborhood_set = neighborhoodset
    }

let bootstrapNetwork creds =  
    let routingTableColumns = Array.init Config.routingTableColumns (fun _ -> None)
    let node = getNewNodeInfo (Initialized([|routingTableColumns|])) (Array.init Config.neighborhoodSize (fun _ -> None)) creds
    TempState.nodeStates <- (node, {isLeafSetReady = false; isRoutingTableReady = false; isTablesSpread = true;}, { peers = 1 }) :: TempState.nodeStates

let join (bootNode:Node) credentials date =
    let routingTableRow = 
        match bootNode.routing_table with 
        | Initialized table -> 
            let routingTable = Array.copy <| Array.head table
            routingTable
        | Uninitialized _ -> raise <| invalidOp("node with uninitialized routingTable cannot be used as a boot node")

    let newNodeNeighbors = 
        [[|Some(bootNode.nodeInfo)|]; Array.copy bootNode.neighborhood_set] 
        |> Array.concat
        |> sortByIgnoreNone (fun i -> i.address) 
        |> Array.truncate Config.neighborhoodSize

    let newNode = getNewNodeInfo (Uninitialized([||])) newNodeNeighbors credentials
    let (_, _, network) = Routing.getNodeData bootNode.nodeInfo.identifier
    // NOTES1: neighborhood set should be modified here.. boot should update and current node should update
    // neighborhood is set in getNewNodeInfo, and it will be sent back to the node in spreadTables
    TempState.nodeStates <- (newNode, {isLeafSetReady = false; isRoutingTableReady = false; isTablesSpread = false}, { network with peers = network.peers + 1 }) :: TempState.nodeStates    

    let nodeInfo = Routing.getForwardToNode bootNode newNode.nodeInfo.identifier
    
    Routing.sendMessage bootNode newNode.nodeInfo {
        requestNumber = 1;
        prev_peer = None;
        request_initiator = bootNode.nodeInfo;
        data = RoutingTableRow(routingTableRow, 1); // if none, then the boot is the only node so only 1 row
        timestampUTC = date;
    }

    match nodeInfo with 
    | Some nextNode -> 
        let message = {
            requestNumber = 1;
            data = Join(newNode.nodeInfo.identifier);
            prev_peer = None;
            request_initiator = newNode.nodeInfo;
            Message.timestampUTC = date
        }

        Routing.sendMessage bootNode nextNode message
    | None -> // this appears to be the closest node already lol... - or right now N = 1
        Routing.sendMessage bootNode newNode.nodeInfo {
            requestNumber = 1; 
            prev_peer = None; 
            request_initiator = bootNode.nodeInfo; 
            data = LeafSet(bootNode.leaf_set);
            timestampUTC = date;
        }