namespace PastryProtocol

open System
open System.Numerics

module Types =

    [<Struct>]
    type Network = {
        peers : int
    }

    [<Struct>]
    type NodeInfo = {
        identifier : string; // SHA1 of IP Address in the base "numberBase"
        address : BigInteger;
    }

    [<Struct>]
    type LeafSet = {
        left : NodeInfo Option[]
        right : NodeInfo Option[]
    }

    type RoutingTableRow = NodeInfo Option []
    type NeighborhoodSet = NodeInfo Option []

    type RoutingTable = 
    | Uninitialized of Option<RoutingTableRow>[]
    | Initialized of RoutingTableRow[]
    
    [<Struct>]
    type Node = {
        nodeInfo : NodeInfo;
        routing_table : RoutingTable;
        neighborhood_set : NeighborhoodSet;
        leaf_set : LeafSet;
    }

    [<Struct>]
    type NodeMetadata = {
        isTablesSpread : bool;
        isLeafSetReady : bool;
        isRoutingTableReady : bool;
    }

    type NodeData = {
        node: Node;
        nodeState: NodeMetadata;
        network: Network;
    }

    type MessageData = 
    | Join of key:string
    | RoutingTableRow of RoutingTableRow * rowNumber:int
    | LeafSet of LeafSet
    | NewNodeState of NodeData
    | BootNode of address: BigInteger
    | Custom of string

    type ComparisonResult =
    | LT = -1
    | Eq = 0
    | GT = 1

    [<Struct>]
    type Message = {
        prev_peer: NodeInfo Option;
        request_initiator: NodeInfo;
        requestNumber: int;
        data: MessageData;
        timestampUTC: DateTime;
    }

    type NodeActorUpdate = 
    | BootRequest of address:BigInteger * peersLength: int
    | Message of Message
    | SendMessageRequest of string

    type MessageToSend = {
        message: Message;
        recipient: NodeInfo;
    }

    type NetworkRequest = 
    | GetActorRef of address: string
    | NewActorRef of NodeData
    | BootNode of address: BigInteger
    | BroadcastMessage of msg: string