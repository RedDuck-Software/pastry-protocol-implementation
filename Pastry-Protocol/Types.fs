﻿namespace PastryProtocol

open System

module Types =

    type NodeInfo = {
        identifier: string; // SHA1 of IP Address in the base (2^b)
        address : string;
    }

    type LeafSet = {
        left : NodeInfo Option[]
        right : NodeInfo Option[]
    }

    type RoutingTableRow = NodeInfo Option []
    type NeighborhoodSet = NodeInfo Option []

    type RoutingTable = 
    | Uninitialized of Option<RoutingTableRow>[]
    | Initialized of RoutingTableRow[]

    type Node = {
        nodeInfo : NodeInfo;
        routing_table : RoutingTable;
        neighborhood_set : NeighborhoodSet;
        leaf_set : LeafSet
    }

    type MessageData = 
    | Join of key:string
    | RoutingTableRow of RoutingTableRow * rowNumber:int * isLastRow:bool
    | LeafSet of LeafSet
    | NewNodeState of Node
    | Custom of string

    type ComparisonResult =
    | LT = -1
    | Eq = 0
    | GT = 1

    type Message = {
        prev_peer: NodeInfo Option;
        request_initiator: NodeInfo;
        requestNumber: int;
        data: MessageData;
        timestampUTC: DateTime;
    }

    type Credentials = {
        IPAddress : string
    }