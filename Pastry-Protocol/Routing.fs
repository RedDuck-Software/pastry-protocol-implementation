namespace PastryProtocol

open System

module Routing = 

    open Types
    open Utils

    let sharedPrefix (a:string) (b:string) =
        let mutable count = 0
        let mutable index = 0
        while a.[index] = b.[index] do
            count <- count + 1
            index <- index + 1

        count   

    let getRoutingTableRowsLength network = 
        let result = int <| System.Math.Log(float network.peers, float <| pown 2 Config.b)
        if result = 0 then 1 else result

    let sendMessage (toNode:NodeInfo) (message:Message) = { message = message; recipient = toNode }

        // pure
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

    let onJoinMessage currentNodeState message key date = 
        let currentNode = currentNodeState.node
        match currentNode.routing_table with 
        | Initialized routingTable ->
            let row = Array.tryItem message.requestNumber routingTable // ??? - should be fine !
            let nextNode = getForwardToNode currentNode key

            let routingMessage = row |> Option.bind (fun row -> 
                let routingTableMsg = { 
                    data = RoutingTableRow(row, message.requestNumber); 
                    prev_peer = None;
                    request_initiator = currentNode.nodeInfo; 
                    requestNumber = message.requestNumber + 1;
                    timestampUTC = date
                }
                Some(sendMessage message.request_initiator routingTableMsg))
    
            let anotherMessage = 
                match nextNode with 
                | Some nextNode -> // forward to the next node
                        let message = { 
                                        message with 
                                            prev_peer = Some(currentNode.nodeInfo); 
                                            requestNumber = message.requestNumber + 1 
                        }
                        sendMessage nextNode message
                | None -> // send leaf set as this node is the closest
                        let leafSetMessage = { 
                            requestNumber = 1; 
                            prev_peer = None; 
                            request_initiator = currentNode.nodeInfo; 
                            data = LeafSet(currentNode.leaf_set);
                            timestampUTC = date;
                        } 
                        sendMessage message.request_initiator leafSetMessage

            (currentNodeState, [|routingMessage; Some(anotherMessage)|] |> onlySome |> Array.toList)

        | Uninitialized _ -> raise <| invalidOp("uninitialized node cannot process join requests")

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
        newNode

    let isReady nodeData = 
        let isLeafsetReady = nodeData.nodeState.isLeafSetReady
        let isRoutingReady = nodeData.nodeState.isRoutingTableReady
        let isRoutingSet = isRoutingInitialized nodeData.node.routing_table

        isRoutingReady && isLeafsetReady && isRoutingSet

    let spreadTables nodeData requestDate = 
        let node = nodeData.node
        match node.routing_table with
        | Initialized _ -> 
            let allPeers = peersFromAllTables node
        
            let message = { 
                data = NewNodeState(nodeData); 
                prev_peer = None;
                requestNumber = 1;
                request_initiator = node.nodeInfo;
                timestampUTC = requestDate
            }

            let messagesToSend = allPeers |> Array.map (fun peer -> sendMessage peer message) |> Array.toList
            let newNodeState = { nodeData.nodeState with isTablesSpread = true }

            ({ nodeData with nodeState = newNodeState}, messagesToSend)
        | Uninitialized _ -> raise <| invalidOp("Cannot spread tables when routing table is uninitialized")

    // if alls rows are in place make it initialized()
    let ensureInitialized nodeData =
        let node = nodeData.node
        let nodeState = nodeData.nodeState
        match node.routing_table with
        | Initialized _ -> nodeData
        | Uninitialized uninitTable -> 
            if Array.forall (fun (i:Option<RoutingTableRow>) -> i.IsSome) uninitTable && nodeState.isRoutingTableReady
                then
                    let newTable = Array.map (fun (i:Option<RoutingTableRow>) -> i.Value) uninitTable
                    { nodeData with node = { node with routing_table = Initialized(newTable) } }
                else nodeData

    let onRoutingTableUpdate currentNodeData row rowNumber = // row number i.e not index
        let currentNode = currentNodeData.node
        let newNodeData =
            match currentNode.routing_table with 
            | Uninitialized unInitTable -> 
                let remaining = rowNumber - unInitTable.Length
                if remaining > 0 
                    then // ensure array capacity
                        if currentNodeData.nodeState.isRoutingTableReady
                            then raise <| invalidOp "routing table ready, but remaining is > 0"

                        let newRows = Array.init (remaining - 1) (fun _ -> None)
                        let newTable = (seq { 
                            yield! unInitTable; 
                            yield! newRows; 
                            yield Some(row)
                        } |> Array.ofSeq)

                        { currentNodeData with node = { currentNode with routing_table = Uninitialized(newTable) } }
                    else 
                        unInitTable.[rowNumber - 1] <- Some(row)
                        { currentNodeData with node = { currentNode with routing_table = Uninitialized(unInitTable) } }
            | Initialized initTable -> 
                initTable.[rowNumber] <- row
                { currentNodeData with node = { currentNode with routing_table = Initialized(initTable) } }

        let network = newNodeData.network
        let routingTableReady = rowNumber = getRoutingTableRowsLength network || newNodeData.nodeState.isRoutingTableReady

        let newNodeData = { newNodeData with nodeState = { newNodeData.nodeState with isRoutingTableReady = routingTableReady } }

        ensureInitialized newNodeData

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

    let leavesDataSelector i = convertBaseFrom Config.numberBase i.identifier

    let updateRoutingTableCapacity nodeData peers = 
        let node = nodeData.node
        let network = nodeData.network
        let routingTable = node.routing_table
        let newRoutingTable = 
            match routingTable with
            | Uninitialized _ -> raise <| invalidOp "cannot update routing table capacity when it's not initialized"
            | Initialized table -> 
                let rowsCount = getRoutingTableRowsLength network
                let neededRowsCount = rowsCount - (table.Length) // index = number - 1

                if neededRowsCount > 0 
                    then
                        seq { 
                            yield! table;
                            yield! Array.init neededRowsCount (fun _ -> Array.init (Config.routingTableColumns) (fun _ -> None));
                        } |> Array.ofSeq |> Initialized
                    else 
                        Initialized(table)
        { nodeData with node = { node with routing_table = newRoutingTable }; network = { network with peers = peers } }
        

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

    let onLeafsetUpdate currentNodeData updateLeafSet fromNode = // NODE1: should also include the node that sends it i.e the closest node. and the closest node should update the table too.
        let allLeaves = allLeaves updateLeafSet
        let left = getNewLeftLeaves (Array.concat [[|fromNode|]; allLeaves]) currentNodeData.node
        let right = getNewRightLeaves (Array.concat [[|fromNode|]; allLeaves]) currentNodeData.node
        let newNode = { currentNodeData.node with leaf_set = { left = left; right = right; } }
        let newNodeState = { currentNodeData.nodeState with isLeafSetReady = true }
        { currentNodeData with node = newNode; nodeState = newNodeState }

    let onNewNodeState currentNodeData (newNodeData:NodeData) = 
        // step 1 - update routing table capacity

        let newNodeNetworkData = newNodeData.network
        let currentNodeData = updateRoutingTableCapacity currentNodeData newNodeNetworkData.peers

        match newNodeData.node.routing_table with 
        | Initialized _ -> 
   
            let allNodes = peersFromAllTables newNodeData.node
                            |> Array.except [|currentNodeData.node.nodeInfo|] 
                            |> Array.append [|newNodeData.node.nodeInfo|]

            let newLeftLeaves = getNewLeftLeaves allNodes currentNodeData.node
            let newRightLeaves = getNewRightLeaves allNodes currentNodeData.node
            let newNeighbors = getNewNeighbors allNodes currentNodeData.node
            let newRoutingTable = getNewRoutingTable currentNodeData.node allNodes

            updateTables currentNodeData.node newLeftLeaves newRightLeaves newNeighbors newRoutingTable

        | Uninitialized _ -> raise <| invalidOp("uninitialized node cannot react on new nodes joining!")        

    let onMessage a : (NodeData * MessageToSend list) =
        let (currentNodeData, message) = a
        let (newNodeState, messagesToSend) =
            match message.data with
            | Join key -> onJoinMessage currentNodeData message key DateTime.UtcNow
            | RoutingTableRow (row, rowNumber) -> 
                        let newNodeData = onRoutingTableUpdate currentNodeData row rowNumber
                        (newNodeData, [])
            | LeafSet leafSet -> 
                        let newNodeData = onLeafsetUpdate currentNodeData leafSet message.request_initiator
                        (newNodeData, [])
            | NewNodeState newNode -> 
                        let newNode = onNewNodeState currentNodeData newNode
                        ({ currentNodeData with node = newNode; }, [])
            | _ -> raise <| invalidOp("message.data is invalid")
    
        let isReadyToSpread = isReady newNodeState && (not <| newNodeState.nodeState.isTablesSpread)

        let (newNodeState1, spreadTableMessages) = 
            if isReadyToSpread
                then 
                    let (nData, messages) = spreadTables newNodeState DateTime.UtcNow
                    (Some(nData), messages)
                else (None, [])

        let newNodeState = Option.defaultValue newNodeState newNodeState1
        let messagesToSend = List.append spreadTableMessages messagesToSend

        (newNodeState, messagesToSend)

// -----------------------------
// TODO: 
// 1. Spreading tables + 
// 2. Adjusting tables of nodes to correspond to the current node +
// 3. Re-fa-ctor. +
// 4. Test +
// 5. Actors
// 6. Test