module Pastry

open System.Collections.Generic
open System

type Message = {
    mid : obj;
    numHops : int;
    requestInit : obj;
    prevPeer : obj;
    requestNumber : obj;
    receivedThrough : obj; // rt or lt??
    forcedLeafSet : bool; 
}

type Network = {
    num : float;
    rows : int;
    myBin : IDictionary<char, obj list>;
    sortedPeers: obj;
    sortedPeersMap : obj;
    rowEmptyLists : obj;
    peer : obj;
    maxRequests : int;
    trialCount : int;
    totalHops: int;
    tableUpdatedPeers : int;
    lastRequest : int;
    print : int;
    mainPID : Network;
    failedNodes: int;
}

type NodeInfo = {
    identifier: Guid;
    address : string;
}

type LeafSet = NodeInfo list
type RoutingTable = NodeInfo Option list list
type NeighborhoodSet = NodeInfo list

type Node = {
    node_info : NodeInfo;
    routing_table : RoutingTable;
    neighborhood_set : NeighborhoodSet;
    leaf_set : LeafSet
}

type Tables = {
    routingTable : obj;
    leafSet : obj;
    self : obj;
    selfAtom : obj;
    readyRT : bool;
    readyLS : bool;
    rows : obj;
    nodeActive : bool;
    requestNumber : int;
    maxRequests : obj;
    hopsPerRequestList : obj;
}