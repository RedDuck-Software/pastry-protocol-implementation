module TempState

open PastryProtocol.Types

let mutable (nodeStates:(Node * NodeMetadata * Network) list) = []