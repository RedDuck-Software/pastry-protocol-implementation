module PastryAPI

open Pastry
open System.Text
open Utils.CSharp
open System.Numerics
open System.Linq
open System

let pastryInit credentials application initNode =
    let nodeId = getHash credentials.IPAddress

    // send "route" message to initNode actor. 
    // initNode will route it to closest Z node. 
    // all notes on the path to Z including A (initNode) send their tables to X (this node)
    
    (*
        The new node X inspects this
        information, may request state from additional nodes, and then initializes its own state
        tables, using a procedure describe below    
    *)

    (*
        Finally, X informs any nodes that need to be aware of its arrival.
    *)

    // initNode neighborhood set = new node neighborhood set

    // last node processing request (i.e the closest node)'s leaf set = new node leaf set

    // routing table of new node:
    // 0 row - initNode 0 row
    // 1 row - the node initNode forwarded message to's 1 row
    // ...

    // initNode and new node should be in proximity

    // Finally, X transmits a copy of its resulting state to each of the nodes found in its
    // neighborhood set, leaf set, and routing table. Those nodes in turn update their own state
    // based on the information received. - how exactly? O_o

    (*
Briefly, whenever a node A provides state information to a node B, it attaches a timestamp to the message. B adjusts its own state based on this information and eventually
sends an update message to A (e.g., notifying A of its arrival). B attaches the original
timestamp, which allows A to check if its state has since changed. In the event that its
state has changed, it responds with its updated state and B restarts its operation.
    *)

    // initialize

    nodeId

// causes Pastry to route the given message to the node with nodeId numerically closest to the key,
// among all live Pastry nodes.
// TODO this should be a method of actors
let route msg key = ()