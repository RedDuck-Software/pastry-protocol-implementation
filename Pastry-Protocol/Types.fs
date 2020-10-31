namespace PastryProtocol

open System
open System.Numerics
open Akka.Actor

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

    type CustomMessage = {
        recipientKey : string;        
        payload : string;
        requestId : string;
    }

    type MessageData = 
    | Join of key:string
    | RoutingTableRow of RoutingTableRow * rowNumber:int
    | LeafSet of LeafSet
    | NewNodeState of NodeData
    | BootNode of address: BigInteger
    | Custom of CustomMessage

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

    type HopsTestingSession = {
        sessionKey : string
    }

    type HopsTestingSessionUpdate = {
        session : HopsTestingSession;
        hopsPerCurrentRequest : int;
    }

    type NodeHopsTestingRequest = {
        hopsTestingSession : HopsTestingSession;
        key : string;
        data : string;
    }

    type NodeActorState = {
        nodeData : NodeData;
        peers : IActorRef list;
    }

    type NodeActorUpdate = 
    | BootRequest of address:BigInteger * peersLength: int
    | Message of Message
    | NodeHopsTestingRequest of NodeHopsTestingRequest

    type MessageToSend = {
        message: Message;
        recipient: NodeInfo;
    }

    type HopsTestingRequest = {
        hopsTestingData : HopsTestingSession;
        sendersCount : int;
    }

    type NetworkRequest = 
    | NodeInitialized of address: bigint
    | GetActorRef of address: string
    | NewActorRef of NodeData
    | BootNode of address: BigInteger
    | HopsTestingRequest of HopsTestingRequest
    | HopsTestingSessionUpdate of HopsTestingSessionUpdate