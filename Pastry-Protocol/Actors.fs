module Actors

open System.Text
open System.Linq
open System
open PastryProtocol.Types
open PastryProtocol.Utils
open PastryProtocol
open Akka.FSharp
open Akka.Actor
open System.Numerics

let spawnChild childActor name (mailbox : Actor<'a>) =
  spawn mailbox.Context name childActor

let locker = obj()

let log a = lock locker (fun () -> Serilog.Log.Logger.Information a )
let json = Newtonsoft.Json.JsonConvert.SerializeObject

let sha = System.Security.Cryptography.SHA1.Create()
let rnd = Random()

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

let getNodeId ipAddress = getHash <| convertBaseTo Config.numberBase ipAddress

let getNewNodeInfo routingTable neighborhoodset ipAddress =
    let nodeId = getNodeId ipAddress
    {
        nodeInfo = { address = ipAddress; identifier = nodeId }
        routing_table = routingTable;
        leaf_set = { 
            left = Array.init (Config.leafSize / 2) (fun _ -> None); 
            right = Array.init (Config.leafSize / 2) (fun _ -> None);
        }
        neighborhood_set = neighborhoodset
    }

let networkTestingActor (networkCallsLimit: int) (mailbox : Actor<'a>) = 
    let rec imp (currentNetworkCalls, totalHops) =
        actor {
            let! hops = mailbox.Receive()
            printfn "NetworkTESTING: Received #%i request, hops data: %A" (currentNetworkCalls + 1) hops

            let hops = (hops :> obj) :?> int

            let currentNetworkCalls = currentNetworkCalls + 1
            let totalHops = totalHops + hops

            if currentNetworkCalls = networkCallsLimit 
                then 
                    printfn "TEST RESULTS:\r\nMessages: %i; Total hops:%i; Average hops:%f" currentNetworkCalls totalHops (float totalHops / float currentNetworkCalls)
                    // todo stop current actor?
                else
                    return! imp (currentNetworkCalls, totalHops)
        }
    printfn "Starting TEST Actor:\r\nMessages limit: %i;" networkCallsLimit
    imp (0, 0)

let nodeActor (nodeData: NodeData) (initialActors: IActorRef list) (mailbox : Actor<'a>) =    
  let getPeerByAddress address (actors:IActorRef list) = async {
        let actor = List.tryFind (fun (i:IActorRef) -> i.Path.Name = address) actors
        match actor with 
        | None -> 
            let! objRes = ((mailbox.Context.Parent.Ask <| GetActorRef(address)) |> Async.AwaitTask)
            let res = objRes :?> IActorRef
            let newList = List.append [res] actors
            return (newList, res)
        | Some a -> return (actors, a)
    }
  
  let sendMessage actors message nodeInfo =
    let address = string nodeInfo.address
    let (newlist, actorRef) = getPeerByAddress address actors |> Async.RunSynchronously
    actorRef <! Message(message)
    newlist

  let sendMessages actors messagesToSend =
    let mutable actors = actors
    for messageToSend in messagesToSend do
        let message = messageToSend.message
        let nodeInfo = messageToSend.recipient
        actors <- sendMessage actors message nodeInfo

    actors

  let rec imp (state:NodeActorState) =
    actor {
      let nodeData = state.nodeData 
      let peers = state.peers 
      let! objMsg = mailbox.Receive()

      let lastIsInitialized = nodeData.nodeState.isTablesSpread
      let hz = "NODE------------------------------------------------------------------------------------------------------"
      log <| sprintf "%s\r\nNodeData:%s\r\n;Message:%s\r\n;PeersLength:%i\r\n%s" hz (json nodeData) (json objMsg) (peers.Length) hz
      let nodeUpdate = (objMsg :> obj) :?> Types.NodeActorUpdate
      let sendMessageToCurrentPeers = sendMessage peers

      let (nodeData, peers) = 
        match nodeUpdate with 
        | NodeHopsTestingRequest request ->
            let message = {
                request_initiator = nodeData.node.nodeInfo;
                data = Custom({ recipientKey = request.key; payload = request.data; requestId = request.hopsTestingSession.sessionKey });
                prev_peer = None;
                timestampUTC = DateTime.UtcNow;
                requestNumber = 1;
            }

            let nextNode = Domain.getNodeToForwardTo nodeData.node request.key

            match message.data with
            | Custom _ ->
                match nextNode with // none -> send to parent that this request was delivered with 1 hop
                | None -> mailbox.Context.Parent <! HopsTestingSessionUpdate({ session = request.hopsTestingSession; hopsPerCurrentRequest = 1; })
                | Some a -> sendMessage peers message a |> ignore
            | _ -> ()

            (nodeData, peers)
        | Message message ->
            let (nodeData, messagesToSend) = Domain.onMessage (nodeData, message)

            let peers = 
                match message.data with 
                | Custom msg -> 
                    if messagesToSend.Length = 0 then 
                            mailbox.Context.Parent <! HopsTestingSessionUpdate({ session = { sessionKey = msg.requestId }; hopsPerCurrentRequest = message.requestNumber; })
                            peers
                    elif messagesToSend.Length = 1 then
                        sendMessages peers messagesToSend
                    else raise <| invalidOp("cannot forward to more than 1")
                | _ -> sendMessages peers messagesToSend

            (nodeData, peers)
            
        | BootRequest (address, peers) -> 
            if not nodeData.nodeState.isTablesSpread then raise <| invalidOp("it's not initialized yet")

            let routingTableRow = 
                match nodeData.node.routing_table with 
                | Initialized table -> 
                    let routingTable = Array.copy <| Array.head table
                    routingTable
                | Uninitialized _ -> raise <| invalidOp("node with uninitialized routingTable cannot be used as a boot node")

            let newNodeNeighbors = 
                [[|Some(nodeData.node.nodeInfo)|]; Array.copy nodeData.node.neighborhood_set] 
                |> Array.concat
                |> sortByIgnoreNone (fun i -> i.address) 
                |> Array.truncate Config.neighborhoodSize

            let newNodeTemp = getNewNodeInfo (Uninitialized([||])) newNodeNeighbors address
            let newNodeMetadataTemp = {isLeafSetReady = false; isRoutingTableReady = false; isTablesSpread = false}
            let newNetworkDataTemp = { peers = peers }
            // NOTES1: neighborhood set should be modified here.. boot should update and current node should update
            // neighborhood is set in getNewNodeInfo, and it will be sent back to the node in spreadTables

            let joiningNodeData = { node = newNodeTemp; nodeState = newNodeMetadataTemp; network = newNetworkDataTemp }

            // here network has info about the new node and it will be able to receive messages (leafset etc)
            mailbox.Context.Parent.Ask(NewActorRef(joiningNodeData)) |> Async.AwaitTask |> Async.Ignore |> Async.RunSynchronously

            let date = DateTime.UtcNow
            let message = { // TODO redo these to give nodes to send to
                requestNumber = 1;
                prev_peer = None;
                request_initiator = nodeData.node.nodeInfo;
                data = RoutingTableRow(routingTableRow, 1);
                timestampUTC = date;
            }

            let newList = sendMessageToCurrentPeers message joiningNodeData.node.nodeInfo
            let nodeInfo = Domain.getNodeToForwardTo nodeData.node joiningNodeData.node.nodeInfo.identifier            
            let sendMessageToNewPeers = sendMessage newList

            let peers = 
                match nodeInfo with 
                | Some nextNode -> 
                    let message = {
                        requestNumber = 1;
                        data = Join(joiningNodeData.node.nodeInfo.identifier);
                        prev_peer = None;
                        request_initiator = joiningNodeData.node.nodeInfo;
                        Message.timestampUTC = date
                    }

                    sendMessageToNewPeers message nextNode
                | None -> // this appears to be the closest node already lol... - or right now N = 1
                    let message = {
                        requestNumber = 1; 
                        prev_peer = None; 
                        request_initiator = nodeData.node.nodeInfo;
                        data = LeafSet(nodeData.node.leaf_set);
                        timestampUTC = date;
                    }
                    sendMessageToNewPeers message joiningNodeData.node.nodeInfo 
            (nodeData, peers)
            
      log <| sprintf "%s updating state to: %s" nodeData.node.nodeInfo.identifier (json nodeData)

      if lastIsInitialized = false && nodeData.nodeState.isTablesSpread = true then
            mailbox.Context.Parent <! NodeInitialized(nodeData.node.nodeInfo.address)

      return! imp { state with nodeData = nodeData; peers = peers }
    }
  imp { nodeData = nodeData; peers = initialActors; }

let networkActor (mailbox : Actor<'a>) =
    let rec imp (peers:((IActorRef * bool) list)) = 
        actor {
            let! objMsg = mailbox.Receive()

            let hz = "NETWORK------------------------------------------------------------------------------------------------------"
            log <| sprintf "%s\r\nMessage: %s\r\n;Peers: %i\r\n;%s" hz (json objMsg) (peers.Length) hz

            let msg = (objMsg :> obj) :?> NetworkRequest

            let mutable totalLength = peers.Length
            let initedPeers = 
                peers
                |> List.filter (fun (_, isInited) -> isInited) 
                |> List.map (fun (i, _) -> i)

            let updatedPeers = 
                match msg with 
                | HopsTestingSessionUpdate upd -> 
                    mailbox.Context.Child(upd.session.sessionKey) <! upd.hopsPerCurrentRequest
                    None
                | NodeInitialized address ->
                    let (actorRef, isInited) = List.find (fun ((i:IActorRef), _) -> (BigInteger.Parse i.Path.Name) = address) peers
                    Some <| (actorRef, true) :: List.except [(actorRef, isInited)] peers
                | HopsTestingRequest request ->
                    let _ = spawnChild (networkTestingActor request.sendersCount) request.hopsTestingData.sessionKey mailbox

                    let senders = Array.init request.sendersCount (fun i -> rnd.Next(0, initedPeers.Length)) |> Array.map (fun i -> initedPeers.Item i)
                    let keys = initedPeers |> List.except senders |> List.map (fun i -> (BigInteger.Parse i.Path.Name) |> getNodeId) |> List.truncate request.sendersCount

                    for senderIndx in 0..senders.Length - 1 do
                        let key = keys.[senderIndx]
                        let sender = senders.[senderIndx]
                        let request = { 
                            hopsTestingSession = request.hopsTestingData;
                            data = (sprintf "testing node #%s" sender.Path.Name);
                            key = key;
                        }

                        sender <! NodeHopsTestingRequest(request)
                    None
                | GetActorRef id -> 
                    let (peerRef, _) = List.find (fun (i:IActorRef, _) -> i.Path.Name = id) peers
                    mailbox.Context.Sender.Tell(peerRef, mailbox.Context.Self)
                    None
                | BootNode address -> 
                    let closestPeer = 
                        initedPeers |> List.minBy (fun i -> abs ((BigInteger.Parse i.Path.Name) - address))
                    totalLength <- totalLength + 1
                    closestPeer <! BootRequest(address, totalLength)
                    None
                | NewActorRef nodeData ->         
                    let isBootstrap = peers.Length = 0
                    let newPeerActorRef = spawnChild (nodeActor nodeData initedPeers) (nodeData.node.nodeInfo.address.ToString()) mailbox
                    mailbox.Context.Sender.Tell(newPeerActorRef, mailbox.Context.Self)
                    Some <| (newPeerActorRef, isBootstrap) :: peers

            let newPeers = Option.defaultValue peers updatedPeers

            return! imp newPeers
        }
    imp ([])

let bootstrapNetwork ipAddress = 
    let routingTableColumns = Array.init Config.routingTableColumns (fun _ -> None)
    let node = getNewNodeInfo (Initialized([|routingTableColumns|])) (Array.init Config.neighborhoodSize (fun _ -> None)) ipAddress
    let system = System.create "system" <| Configuration.load ()
    let nodeState = {isLeafSetReady = false; isRoutingTableReady = false; isTablesSpread = true;}
    let network = { peers = 1 }
    let nodeData = { node = node; nodeState = nodeState; network = network }

    let networkRef = spawn system "network" networkActor

    networkRef <! NewActorRef(nodeData)
    networkRef

let joinNetwork networkRef ipAddress = networkRef <! BootNode(ipAddress)
