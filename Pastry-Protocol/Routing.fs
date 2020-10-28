namespace PastryProtocol

open System

module Routing = 

    open Types
    open Utils
    open CSharp.Utils

    let sharedPrefix (a:string) (b:string) =
        let mutable count = 0
        for i in 0..a.Length - 1 do
            if a.[i] = b.[i] then count <- count + 1
        count
    let strToBigInt s = GenericBaseConverter.ConvertFromString(s, Config.numberBase)
    let toBigInt = string >> strToBigInt

    // stub
    // will just send message, message handling is in onmessage
    let sendMessage (fromNode:Node) (toNode:NodeInfo) (message:Message) = 
        // stub - sending to node - here will be a lot of actor code
        ()

    // that's simply routing of a message, if noteTo = key I think we don't need it. 
    // Returns Some if found something better than current node and None if current node is the closest
    let getForwardToNode currentNode key =
        let leafSet = allLeaves currentNode.leaf_set 
        if (Array.head leafSet).identifier <= key && key <= (Array.last leafSet).identifier
            then 
                let minByDiff = Array.minBy (fun i -> abs (strToBigInt key - strToBigInt i.identifier)) leafSet
            
                Some(minByDiff)
            else
                let l = sharedPrefix key currentNode.nodeInfo.identifier
                let l_in_key_value = key.ToString().[l] |> toBigInt |> int
            
                match currentNode.routing_table with 
                | Initialized routingTable -> 
                    let routingTableRow = routingTable.[l]
                    match routingTableRow.[l_in_key_value] with
                    | Some peer -> Some(peer) // forward to this address
                    | None -> // rare case
                        let union = Array.concat [leafSet; onlySome routingTableRow; onlySome currentNode.neighborhood_set]
                        let predicate1 = (fun peer -> sharedPrefix peer.identifier key >= l)
                        let predicate2 = (fun peer -> 
                                            let key10 = strToBigInt key 
                                            let peer10 = strToBigInt peer.identifier 
                                            let node10 = strToBigInt currentNode.nodeInfo.identifier
                    
                                            abs (peer10 - key10) < abs (node10 - key10))
                        let T = Array.tryFind (fun i -> predicate1 i && predicate2 i) union
                        T
                | Uninitialized _ -> raise <| invalidOp("Attempt to route a message through uninitialized node")

    let onJoinMessage currentNode message key date = 
        match currentNode.routing_table with 
        | Initialized routingTable -> 
            let row = routingTable.[message.requestNumber]    
            let nextNode = getForwardToNode currentNode key

            let routingTableMsg = { 
                data = RoutingTableRow(row, message.requestNumber, nextNode.IsNone); 
                prev_peer = None;
                request_initiator = currentNode.nodeInfo; 
                requestNumber = 1;
                timestampUTC = date
            }

            sendMessage currentNode message.request_initiator routingTableMsg
    
            match nextNode with 
            | Some nextNode -> // forward to the next node
                    let message = { 
                                    message with 
                                        prev_peer = Some(currentNode.nodeInfo); 
                                        requestNumber = message.requestNumber + 1 
                    }
                    sendMessage currentNode nextNode message
            | None -> // send leaf set as this node is the closest
                    let message = { 
                        requestNumber = 1; 
                        prev_peer = None; 
                        request_initiator = currentNode.nodeInfo; 
                        data = LeafSet(currentNode.leaf_set);
                        timestampUTC = date;
                    } 
                    sendMessage currentNode message.request_initiator message
        | Uninitialized _ -> raise <| invalidOp("uninitialized node cannot process join requests")

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
        let isRoutingSet = isRoutingInitialized node.routing_table

        isRoutingReady && isLeafsetReady && isRoutingSet

    let spreadTables node requestDate = 
        match node.routing_table with 
        | Initialized table -> 
            let routingTableElements = Array.concat table
            let allPeers = peersFromAllTables node
        
            let message = { 
                data = NewNodeState(node); 
                prev_peer = None;
                requestNumber = 1;
                request_initiator = node.nodeInfo ;
                timestampUTC = requestDate
            }

            for peer in allPeers do 
                sendMessage node peer message
        | Uninitialized _ -> raise <| invalidOp("Cannot spread tables when routing table is uninitialized")

    let onRoutingTableUpdate currentNode row rowNumber isLast =
        match currentNode.routing_table with 
        | Uninitialized unInitTable -> 
            let remaining = rowNumber - unInitTable.Length
            if remaining > 0 
                then // ensure array capacity
                    if isRoutingTableReady currentNode 
                        then raise <| invalidOp "routing table ready, but remaining is > 0"

                    let newRows = Array.init (remaining - 1) (fun _ -> None)
                    let newTable = (seq { 
                        yield! unInitTable; 
                        yield! newRows; 
                        yield Some(row)
                    } |> Array.ofSeq)

                    updateCurrentNode { currentNode with routing_table = Uninitialized(newTable) }
                else 
                    unInitTable.[rowNumber] <- Some(row)
                    updateCurrentNode { currentNode with routing_table = Uninitialized(unInitTable) }
        | Initialized initTable -> 
            initTable.[rowNumber] <- row
            updateCurrentNode { currentNode with routing_table = Initialized(initTable) }

        if isLast then setRoutingTableReady currentNode

        // if alls rows are in place make it initialized()
        match currentNode.routing_table with 
        | Initialized _ -> ()
        | Uninitialized uninitTable -> 
            if Array.forall (fun (i:Option<RoutingTableRow>) -> i.IsSome) uninitTable && isRoutingTableReady currentNode
                then 
                    let newTable = Array.map (fun (i:Option<RoutingTableRow>) -> i.Value) uninitTable
                    updateCurrentNode { currentNode with routing_table = Initialized(newTable) }
    // dont forget to sort.
    let compare (a, b) = enum<ComparisonResult> <| (strToBigInt <| a).CompareTo(strToBigInt <| b)
    let getCompare fieldSelector (a, b) = compare (fieldSelector a, fieldSelector b)
    let updateRange (collectionSelector: Node -> NodeInfo Option []) (fieldSelector: NodeInfo -> string) (allNodes:#seq<NodeInfo>) (currentNode: Node)  = 
        let collection = (collectionSelector currentNode) 
        let compare = getCompare fieldSelector
        for node in allNodes do
            if not <| Array.exists (fun (i:Option<NodeInfo>) -> 
                    match i with 
                    | Some nodeInfo -> compare (nodeInfo, node) = ComparisonResult.Eq
                    | None -> false) collection
                then
                    if Array.forall (fun (i:Option<NodeInfo>) -> i.IsSome) collection
                        then 
                            if compare (collection.[0].Value, node) = ComparisonResult.LT
                                then collection.[0] <- Some(node)
                            else if compare ((Array.last collection).Value, node) = ComparisonResult.GT
                                then collection.[collection.Length - 1] <- Some(node)
                            else () // not within the range
                        else                     
                            let noneIndex = Array.findIndex (fun (i:Option<NodeInfo>) -> i.IsNone) collection
                            collection.[noneIndex] <- Some(node)
        let someValues = Array.sortBy fieldSelector <| onlySome collection
        let noneCount = collection.Length - someValues.Length

        (seq {
            yield! Array.map (fun i -> Some(i)) someValues;
            for i in 1..noneCount do
                yield None
        }) |> Array.ofSeq

    // TODO: for leaf set, we also need to ensure that we split it by 2 (lesser and greater)
    let getNewLeftLeaves peers node = updateRange (fun i -> i.leaf_set.left) (fun i -> i.identifier) (Array.where (fun i -> compare (i.identifier, node.nodeInfo.identifier) = ComparisonResult.LT) peers) node
    let getNewRightLeaves peers node = updateRange (fun i -> i.leaf_set.left) (fun i -> i.identifier) (Array.where (fun i -> compare (i.identifier, node.nodeInfo.identifier) = ComparisonResult.GT) peers) node
    let updateNeighbors = updateRange (fun i -> i.neighborhood_set) (fun i -> i.address)

    let updateRoutingTable (currentNode: Node) (allNodes:#seq<NodeInfo>) =
        for node in allNodes do
            let sharedIndex = sharedPrefix node.identifier currentNode.nodeInfo.identifier
            let row = sharedIndex
            let column = node.identifier.[sharedIndex + 1] |> toBigInt |> int

            match currentNode.routing_table with 
            | Initialized table -> 
                match table.[row].[column] with
                | None -> table.[row].[column] <- Some(node)
                | _ -> () // if it's not empty, do not replace
            | Uninitialized _ -> raise <| invalidOp("cannot update uninit table")

    let onNewNodeState (currentNode:Node) (newNode:Node) = 
        match newNode.routing_table with 
        | Initialized table -> 
   
            let allNodes = Array.concat [Array.concat table; newNode.leaf_set.left; newNode.leaf_set.right; newNode.neighborhood_set]
                        |> Array.choose id

            let newLeftLeaves = getNewLeftLeaves allNodes currentNode
            let newRightLeaves = getNewRightLeaves allNodes currentNode
            let newNeighbors = updateNeighbors allNodes currentNode

            // TODO update all left leaves...
            // and right etc...

            updateRoutingTable currentNode allNodes
        | Uninitialized _ -> raise <| invalidOp("uninitialized node cannot react on new nodes joining!")
        ()

    let onMessage currentNode message = 
        match message.data with
        | Join key -> onJoinMessage currentNode message key DateTime.UtcNow
        | RoutingTableRow (row, rowNumber, isLast) -> onRoutingTableUpdate currentNode row rowNumber isLast
        | LeafSet leafSet -> onLeafsetUpdate currentNode leafSet
        | NewNodeState newNode -> onNewNodeState currentNode newNode
        | _ -> ()
    
        if isReady currentNode 
            then spreadTables currentNode DateTime.UtcNow
            else ()

// -----------------------------
// TODO: 
// 1. Spreading tables + 
// 2. Adjusting tables of nodes to correspond to the current node +
// 3. Re-fa-ctor.
// 4. Test
// 5. Actors
// 6. Test