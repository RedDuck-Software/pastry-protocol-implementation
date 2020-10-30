namespace PastryProtocol

open System

module rec Routing = 

    open Types
    open Utils

    let sharedPrefix (a:string) (b:string) =
        let mutable count = 0
        let mutable index = 0
        while a.[index] = b.[index] do
            count <- count + 1
            index <- index + 1

        count   

    let getRoutingTableRows network = 
        let result = int <| System.Math.Log(float network.peers, float <| pown 2 Config.b)
        if result = 0 then 1 else result

    // stub
    // will just send message, message handling is in onmessage
    let sendMessage (fromNode:Node) (toNode:NodeInfo) (message:Message) = 
        let (receiver, _, _) = List.find (fun (i, _, _) -> i.nodeInfo = toNode) TempState.nodeStates
        Routing.onMessage receiver message
        // stub - sending to node - here will be a lot of actor code
        ()

    // that's simply routing of a message, if noteTo = key I think we don't need it. 
    // Returns Some if found something better than current node and None if current node is the closest
    let getForwardToNode currentNode key =
        let leafSet = allLeaves currentNode.leaf_set

        let withinLeafSetRange = 
            match (Array.tryHead leafSet, Array.tryLast leafSet) with 
            | (None, None) -> false
            | (Some head, Some last) -> (strToBigInt head.identifier) <= strToBigInt key && strToBigInt key <= strToBigInt last.identifier
            | _ -> true

        if withinLeafSetRange
            then 
                let minByDiff = Array.minBy (fun i -> abs (strToBigInt key - strToBigInt i.identifier)) (Array.concat [[|currentNode.nodeInfo|];leafSet])
                
                if minByDiff.identifier = currentNode.nodeInfo.identifier 
                    then None
                    else Some(minByDiff)
            else
                let l = sharedPrefix key currentNode.nodeInfo.identifier
                let l_in_key_value = key.ToString().[l] |> toBigInt |> int
            
                match currentNode.routing_table with 
                | Initialized routingTable -> 
                    let routingTableCell = (Array.tryItem l routingTable)
                                            |> Option.bind (Array.tryItem l_in_key_value) |> Option.flatten
                    match routingTableCell with
                    | Some peer ->  Some(peer) // forward to this address
                    | None -> // rare case
                        let union = peersFromAllTables currentNode
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
            let row = Array.tryItem message.requestNumber routingTable // ??? - should be fine !
            let nextNode = getForwardToNode currentNode key

            match row with 
            | None -> ()
            | Some row -> 
                let routingTableMsg = { 
                    data = RoutingTableRow(row, message.requestNumber); 
                    prev_peer = None;
                    request_initiator = currentNode.nodeInfo; 
                    requestNumber = message.requestNumber + 1;
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
                    let leafSetMessage = { 
                        requestNumber = 1; 
                        prev_peer = None; 
                        request_initiator = currentNode.nodeInfo; 
                        data = LeafSet(currentNode.leaf_set);
                        timestampUTC = date;
                    } 
                    sendMessage currentNode message.request_initiator leafSetMessage
        | Uninitialized _ -> raise <| invalidOp("uninitialized node cannot process join requests")

    let getNodeData identifier = 
        List.find (fun (i, _, _) -> i.nodeInfo.identifier = identifier) TempState.nodeStates

    let updateCurrentNodeData newNode newState newNetwork = 
        let (node, state, network) = List.find (fun (i, j, k) -> i.nodeInfo.identifier = newNode.nodeInfo.identifier) TempState.nodeStates
        TempState.nodeStates <- (newNode, newState, newNetwork) :: List.except [(node, state, network)] TempState.nodeStates
        (newNode, newState, newNetwork)

    let updateCurrentNode currentNode = 
        let (_, state, network) = getNodeData currentNode.nodeInfo.identifier
        updateCurrentNodeData currentNode state network

    let updateCurrentNodeState nodeId newState = 
        let (node, _, network) = getNodeData nodeId
        updateCurrentNodeData node newState network

    let updateCurrentNodeNetwork nodeId newNetwork = 
        let (node, state, _) = getNodeData nodeId
        updateCurrentNodeData node state newNetwork

    let setLeafsetReady identifier = 
        let (_, state, _) = getNodeData identifier
        updateCurrentNodeState identifier { state with isLeafSetReady = true }

    let setRoutingTableReady identifier = 
        let (_, state, _) = getNodeData identifier
        updateCurrentNodeState identifier { state with isRoutingTableReady = true } 

    let isRoutingTableReady node = 
        let (_, state, _) = (List.find (fun (i, _, _) -> i.nodeInfo.identifier = node.nodeInfo.identifier) TempState.nodeStates)
        state.isRoutingTableReady

    let isTablesSpread node = 
        let (_, state, _) = (List.find (fun (i, _, _) -> i.nodeInfo.identifier = node.nodeInfo.identifier) TempState.nodeStates)
        state.isTablesSpread

    let updateTables currentNode newLeftLeaves newRightLeaves newNeighbors newRoutingTable = 
        let newNode = { 
            currentNode with 
                routing_table = newRoutingTable; 
                leaf_set = { 
                            left=newLeftLeaves; 
                            right=newRightLeaves;
                };
                neighborhood_set = newNeighbors;                
        }
        updateCurrentNode newNode

    let isReady node = 
        let (_, nodeMetadata, _) = List.find (fun (i, _, _) -> i.nodeInfo.identifier = node.nodeInfo.identifier) TempState.nodeStates
        let isLeafsetReady = nodeMetadata.isLeafSetReady
        let isRoutingReady = nodeMetadata.isRoutingTableReady
        let isRoutingSet = isRoutingInitialized node.routing_table

        isRoutingReady && isLeafsetReady && isRoutingSet

    let spreadTables node requestDate = 
        match node.routing_table with
        | Initialized _ -> 
            let allPeers = peersFromAllTables node
        
            let message = { 
                data = NewNodeState(node); 
                prev_peer = None;
                requestNumber = 1;
                request_initiator = node.nodeInfo;
                timestampUTC = requestDate
            }

            for peer in allPeers do 
                sendMessage node peer message

            let (_, nodeState, _) = getNodeData node.nodeInfo.identifier
            updateCurrentNodeState node.nodeInfo.identifier { nodeState with isTablesSpread = true }
        | Uninitialized _ -> raise <| invalidOp("Cannot spread tables when routing table is uninitialized")

    // if alls rows are in place make it initialized()
    let ensureInitialized nodeId = 
        let (node, nodeState, _) = List.find(fun (i, _, _) -> i.nodeInfo.identifier = nodeId) TempState.nodeStates
        match node.routing_table with
        | Initialized _ -> ()
        | Uninitialized uninitTable -> 
            if Array.forall (fun (i:Option<RoutingTableRow>) -> i.IsSome) uninitTable && nodeState.isRoutingTableReady
                then
                    let newTable = Array.map (fun (i:Option<RoutingTableRow>) -> i.Value) uninitTable
                    updateCurrentNode { node with routing_table = Initialized(newTable) } |> ignore

    let onRoutingTableUpdate currentNode row rowNumber = // row number i.e not index
        let (currentNode, _, _) =
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
                        unInitTable.[rowNumber - 1] <- Some(row)
                        updateCurrentNode { currentNode with routing_table = Uninitialized(unInitTable) }
            | Initialized initTable -> 
                initTable.[rowNumber] <- row
                updateCurrentNode { currentNode with routing_table = Initialized(initTable) }

        let (_, _, network) = getNodeData currentNode.nodeInfo.identifier
        if rowNumber = getRoutingTableRows network
            then setRoutingTableReady currentNode.nodeInfo.identifier |> ignore

        ensureInitialized currentNode.nodeInfo.identifier |> ignore

    // dont forget to sort.
    let bigIntCompare ((a:bigint), (b:bigint)) = enum<ComparisonResult> <| a.CompareTo(b)
    let dataCompare mapper (a, b) = bigIntCompare (mapper a, mapper b)
    let updateRange (collectionSelector: Node -> NodeInfo Option []) (dataSelector: NodeInfo -> bigint) (allNodes:#seq<NodeInfo>) (currentNode: Node)  = 
        let collection = Array.copy (collectionSelector currentNode) 
        let compare = dataCompare dataSelector
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
        sortByIgnoreNone dataSelector collection

    // TODO: for leaf set, we also need to ensure that we split it by 2 (lesser and greater)
    let leavesDataSelector i = convertBaseFrom Config.numberBase i.identifier

    let updateRoutingTableCapacity identifier peers = 
        let (node, nodeState, network) = List.find (fun (i, _, _) -> i.nodeInfo.identifier = identifier) TempState.nodeStates
        let routingTable = node.routing_table
        let newRoutingTable = 
            match routingTable with
            | Uninitialized _ -> raise <| invalidOp "cannot update routing table capacity when it's not initialized"
            | Initialized table -> 
                let rowsCount = getRoutingTableRows network
                let neededRowsCount = rowsCount - (table.Length) // index = number - 1

                if neededRowsCount > 0 
                    then
                        seq { // beyond that check joining logic, routing table should have good initial size...
                            yield! table;
                            yield! Array.init neededRowsCount (fun _ -> Array.init (Config.routingTableColumns) (fun _ -> None));
                        } |> Array.ofSeq |> Initialized
                    else 
                        Initialized(table)
        updateCurrentNodeData { node with routing_table = newRoutingTable } nodeState { network with peers = peers }
        

    let getNewLeftLeaves peers node = updateRange (fun i -> i.leaf_set.left) leavesDataSelector (Array.where (fun i -> dataCompare leavesDataSelector (i, node.nodeInfo) = ComparisonResult.LT) peers) node
    let getNewRightLeaves peers node = updateRange (fun i -> i.leaf_set.right) leavesDataSelector (Array.where (fun i -> dataCompare leavesDataSelector (i, node.nodeInfo)  = ComparisonResult.GT) peers) node
    let getNewNeighbors = updateRange (fun i -> i.neighborhood_set) (fun i -> i.address)

    let getNewRoutingTable (currentNode: Node) (allNodes:#seq<NodeInfo>) =
        let mutable tableState = currentNode.routing_table
        
        for node in allNodes do
            let sharedIndex = sharedPrefix node.identifier currentNode.nodeInfo.identifier
            let row = sharedIndex
            let column = node.identifier.[sharedIndex + 1] |> toBigInt |> int

            let newTable = 
                match currentNode.routing_table with 
                | Initialized table -> 
                    match (Array.tryItem row table) |> Option.bind (Array.tryItem column) with
                    | None -> () // no such row or such column in that row. out of range, so don't insert
                    | Some(None) -> table.[row].[column] <- Some(node) 
                    | _ -> () // if it's not empty, do not replace
                    table
                | Uninitialized _ -> raise <| invalidOp("cannot update uninit table")
            tableState <- Initialized(newTable)
        tableState

    let onLeafsetUpdate currentNode updateLeafSet fromNode = // NODE1: should also include the node that sends it i.e the closest node. and the closest node should update the table too.
        let allLeaves = allLeaves updateLeafSet
        let left = getNewLeftLeaves (Array.concat [[|fromNode|]; allLeaves]) currentNode
        let right = getNewRightLeaves (Array.concat [[|fromNode|]; allLeaves]) currentNode
        updateCurrentNode { currentNode with leaf_set = { left = left; right = right; } } |> ignore
        setLeafsetReady currentNode.nodeInfo.identifier |> ignore

    let onNewNodeState currentNodeIdentifier (newNode:Node) = 
        // step 1 - update routing table capacity

        let (_, _, newNodeNetworkData) = getNodeData newNode.nodeInfo.identifier
        let (currentNode, _, _) = updateRoutingTableCapacity currentNodeIdentifier newNodeNetworkData.peers

        match newNode.routing_table with 
        | Initialized _ -> 
   
            let allNodes = peersFromAllTables newNode
                            |> Array.except [|currentNode.nodeInfo|] 
                            |> Array.append [|newNode.nodeInfo|]

            let newLeftLeaves = getNewLeftLeaves allNodes currentNode
            let newRightLeaves = getNewRightLeaves allNodes currentNode
            let newNeighbors = getNewNeighbors allNodes currentNode
            let newRoutingTable = getNewRoutingTable currentNode allNodes

            updateTables currentNode newLeftLeaves newRightLeaves newNeighbors newRoutingTable

        | Uninitialized _ -> raise <| invalidOp("uninitialized node cannot react on new nodes joining!")        

    // TODO send neighbors from boot node to this node
    let rec onMessage currentNode message = 
        match message.data with
        | Join key -> onJoinMessage currentNode message key DateTime.UtcNow
        | RoutingTableRow (row, rowNumber) -> onRoutingTableUpdate currentNode row rowNumber
        | LeafSet leafSet -> onLeafsetUpdate currentNode leafSet message.request_initiator
        | NewNodeState newNode -> onNewNodeState currentNode.nodeInfo.identifier newNode |> ignore
        | _ -> ()
    
        if isReady currentNode && (not <| isTablesSpread currentNode)
            then spreadTables currentNode DateTime.UtcNow |> ignore

// -----------------------------
// TODO: 
// 1. Spreading tables + 
// 2. Adjusting tables of nodes to correspond to the current node +
// 3. Re-fa-ctor. +
// 4. Test +
// 5. Actors
// 6. Test