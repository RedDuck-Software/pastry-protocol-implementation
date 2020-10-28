module Routing

open Pastry
open Utils.CSharp

let sharedPrefix (a:string) (b:string) =
    let mutable count = 0
    for i in 0..a.Length - 1 do
        if a.[i] = a.[i] then 
            count <- count + 1
        else ()
    count

let strToBigInt s = GenericBaseConverter.ConvertFromString(s, (pown 2 Config.b))
let charToBigInt (c:char) = string c |> strToBigInt

// stub
// will just send message, message handling is in onmessage
let sendMessage (fromNode:Node) (toNode:NodeInfo) (message:Message) = 
    // stub - sending to node - here will be a lot of actor code
    ()

// that's simply routing of a message, if noteTo = key I think we don't need it. 
// Returns Some if found something better than current node and None if current node is the closest
let getForwardToNode (currentNode:Node) (key:string) =
    let leafSet = currentNode.leaf_set |> Array.choose id
    if (Array.head leafSet).identifier <= key && key <= (Array.last leafSet).identifier
        then 
            let minByDiff = Array.minBy (fun i -> abs ((strToBigInt key) - (strToBigInt i.identifier))) leafSet
            
            Some(minByDiff)
        else
            let l = sharedPrefix key currentNode.node_info.identifier
            let l_in_key_value = key.ToString().[l] |> charToBigInt |> int
            
            match (currentNode.routing_table.[l]).Value.[l_in_key_value] with // should not be none, if it's none then it's exceptional case
            | Some peer -> Some(peer) // forward to this address
            | None -> // rare case
                let routingTable = Array.map (fun (i:Option<RoutingTableRow>) -> i.Value) currentNode.routing_table |> Array.concat |> Array.choose id
                let union = Array.concat [leafSet; routingTable; currentNode.neighborhood_set |> Array.choose id]
                let predicate1 = (fun (peer:NodeInfo) -> sharedPrefix peer.identifier key >= l)
                let predicate2 = (fun (peer:NodeInfo) -> 
                                    let key10 = strToBigInt key 
                                    let peer10 = strToBigInt peer.identifier 
                                    let node10 = strToBigInt currentNode.node_info.identifier
                                
                                    abs (peer10 - key10) < abs (node10 - key10))
                let T = Array.tryFind (fun i -> predicate1 i && predicate2 i) union
                T

let onJoinMessage currentNode message key = 
    let row = currentNode.routing_table.[message.requestNumber].Value
    
    let nextNode = getForwardToNode currentNode key

    let routingTableMsg = { 
        data = RoutingTableRow(row, message.requestNumber, nextNode.IsNone); 
        prev_peer = currentNode.node_info; 
        request_initiator = currentNode.node_info; 
        requestNumber = 1
    }

    sendMessage currentNode message.request_initiator routingTableMsg
    
    match nextNode with 
    | Some nextNode -> sendMessage currentNode nextNode { message with prev_peer = currentNode.node_info; requestNumber = message.requestNumber + 1 }
    | None -> sendMessage currentNode message.request_initiator { Message.requestNumber = 1; Message.prev_peer = currentNode.node_info; Message.request_initiator = currentNode.node_info; Message.data = LeafSet(currentNode.leaf_set) } // send leaf set as this node is the closest

let updateCurrentNode currentNode = () // update state in the actor

let setLeafsetReady _ = ()

let onLeafsetUpdate currentNode leafSet = 
    updateCurrentNode { currentNode with leaf_set = leafSet }
    setLeafsetReady currentNode

let setRoutingTableReady _ = () // todo set that routing table is ready (but maybe not all have arrived yet)
let isRoutingTableReady node = true

let isReady node = 
    let isLeafsetReady = true
    let isRoutingReady = isRoutingTableReady node
    let isRoutingSet = Array.forall (fun (i:Option<RoutingTableRow>) -> i.IsSome) node.routing_table

    isRoutingReady && isLeafsetReady && isRoutingSet

let spreadTables node = 
    if Array.exists (fun (i: RoutingTableRow Option) -> i.IsNone) node.routing_table 
        then raise <| invalidOp "None row in routing_table while spreading tables"
        else ()

    let routingTableElements = Array.map (fun (i: RoutingTableRow Option) -> i.Value) >> Array.concat 
                                <| node.routing_table   
    let nodes = Array.concat [routingTableElements; node.leaf_set; node.neighborhood_set]
                |> Array.choose id

    // send the state to all peers
    Array.iter (fun peer -> 
            sendMessage node peer { 
                data = NewNodeState(node); 
                prev_peer = node.node_info;
                requestNumber = 1;
                request_initiator = node.node_info 
            }) nodes



let onRoutingTableUpdate currentNode row rowNumber isLast = 
    let remaining = rowNumber - currentNode.routing_table.Length
    let table = if remaining > 0 then // ensure array capacity
                    if isRoutingTableReady currentNode 
                        then raise <| invalidOp "routing table ready, but remainig is > 0" 
                        else ()
                    Array.init rowNumber (fun ind -> 
                        if ind < currentNode.routing_table.Length 
                        then currentNode.routing_table.[ind] 
                        else None)
                    else currentNode.routing_table
    table.[rowNumber] <- row
    updateCurrentNode { currentNode with routing_table = table }
    if isLast then 
        setRoutingTableReady currentNode 
    else ()

let updateRange (fieldSelector: Node -> NodeInfo Option []) (comparator: (NodeInfo * NodeInfo) -> ComparisonResult) (splitIntoHalves:bool) (currentNode: Node) (allNodes:#seq<NodeInfo>) = 
    let field = (fieldSelector currentNode)
    for node in allNodes do
        if not <| Array.exists (fun (i:Option<NodeInfo>) -> 
                i.IsSome 
                && comparator (i.Value, node) = ComparisonResult.Eq) field
            then ()
            else 
                if Array.forall (fun (i:Option<NodeInfo>) -> i.IsSome) field
                    then 
                        if comparator (node, field.[0].Value) = ComparisonResult.GT
                            then field.[0] <- Some(node)
                        else if comparator (node, (Array.last field).Value) = ComparisonResult.LT
                            then field.[field.Length - 1] <- Some(node)
                        else () // not within the range
                    else                     

                        if splitIntoHalves 
                            then
                                let rightHalfIndexStart = Config.leafSize / 2
                                let isRightHalf = comparator (node, currentNode.node_info) = ComparisonResult.GT
                                if isRightHalf
                                    then 
                                        let noneIndex = Array.findIndexBack (fun (i:Option<NodeInfo>) -> i.IsNone) field
                                        if noneIndex >= rightHalfIndexStart 
                                            then
                                                field.[noneIndex] <- Some(node)
                                            else () // do not insert.
                                    else 
                                        let noneIndex = Array.findIndex (fun (i:Option<NodeInfo>) -> i.IsNone) field
                                        if noneIndex < rightHalfIndexStart 
                                            then
                                                field.[noneIndex] <- Some(node)
                                            else () // do not insert.
                            else 
                                let noneIndex = Array.findIndex (fun (i:Option<NodeInfo>) -> i.IsNone) field
                                field.[noneIndex] <- Some(node)
    ()    

// TODO: for leaf set, we also need to ensure that we split it by 2 (lesser and greater)
let updateLeaf = updateRange (fun i -> i.leaf_set) (fun (a, b) -> enum<ComparisonResult> <| (strToBigInt a.identifier).CompareTo(strToBigInt b.identifier)) true
let updateNeighbors = updateRange (fun i -> i.neighborhood_set) (fun (a, b) -> enum<ComparisonResult> <| (strToBigInt a.address).CompareTo(strToBigInt b.address)) false

(*let updateLeaf (currentNode: Node) (allNodes:#seq<NodeInfo>) = 
    for node in allNodes do
        if not <| Array.exists (fun (i:Option<NodeInfo>) -> 
                i.IsSome 
                && i.Value.address = node.address) currentNode.leaf_set
            then ()
            else 
                if Array.forall (fun (i:Option<NodeInfo>) -> i.IsSome) currentNode.leaf_set
                    then 
                        if strToBigInt node.address > strToBigInt currentNode.leaf_set.[0].Value.address
                            then currentNode.leaf_set.[0] <- Some(node)
                        else if strToBigInt node.address < strToBigInt (Array.last currentNode.leaf_set).Value.address
                            then currentNode.leaf_set.[currentNode.leaf_set.Length - 1] <- Some(node)
                        else () // not within the range
                    else 
                        let noneIndex = Array.findIndex (fun (i:Option<NodeInfo>) -> i.IsNone) currentNode.leaf_set
                        currentNode.leaf_set.[noneIndex] <- Some(node)
    ()
*)

let updateRoutingTable (currentNode: Node) (allNodes:#seq<NodeInfo>) =
    for node in allNodes do
        let sharedIndex = sharedPrefix node.identifier currentNode.node_info.identifier
        let row = sharedIndex
        let column = node.identifier.[sharedIndex + 1] |> charToBigInt |> int

        match currentNode.routing_table.[row].Value.[column] with // should not be None, if it is - it's an exception
        | None -> currentNode.routing_table.[row].Value.[column] <- Some(node)
        | _ -> () // if it's not empty, do not replace

let onNewNodeState (currentNode:Node) (newNode:Node) = 
    if Array.exists (fun (i: RoutingTableRow Option) -> i.IsNone) newNode.routing_table 
        then raise <| invalidOp "None row in routing_table while spreading tables"
        else ()
    let routingTableElements = Array.map (fun (i: RoutingTableRow Option) -> i.Value) >> Array.concat 
                                <| newNode.routing_table
    let allNodes = Array.concat [routingTableElements; newNode.leaf_set; newNode.neighborhood_set]
                |> Array.choose id

    updateLeaf currentNode allNodes
    updateNeighbors currentNode allNodes
    updateRoutingTable currentNode allNodes
    
    ()

let onMessage currentNode message = 
    match message.data with
    | Join key -> onJoinMessage currentNode message key
    | RoutingTableRow (row, rowNumber, isLast) -> onRoutingTableUpdate currentNode (Some(row)) rowNumber isLast
    | LeafSet leafSet -> onLeafsetUpdate currentNode leafSet
    | NewNodeState newNode -> onNewNodeState currentNode newNode
    | _ -> ()
    
    if isReady currentNode 
        then spreadTables currentNode 
        else ()

// -----------------------------
// TODO: 
// 1. Spreading tables + 
// 2. Adjusting tables of nodes to correspond to the current node +
// 3. Re-fa-ctor.
// 4. Test
// 5. Actors
// 6. Test