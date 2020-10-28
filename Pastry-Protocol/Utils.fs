namespace PastryProtocol

open Types

module Utils =

    let onlySome a = Array.choose id a
    let allLeaves leafSet = [leafSet.left; leafSet.right] |> Array.concat |> onlySome
    let peersFromAllTables node = 
        let routingTablePeers = 
            match node.routing_table with 
            | Initialized initTable -> initTable |> Array.concat |> onlySome
            | Uninitialized unInitTable -> unInitTable |> onlySome |> Array.concat |> onlySome

        Array.concat [routingTablePeers; allLeaves node.leaf_set; onlySome node.neighborhood_set]
    let isRoutingInitialized = function
    | Initialized(_)  -> true
    | Uninitialized(_) -> false