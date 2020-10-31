namespace PastryProtocol

open Types
open CSharp.Utils
open System.Numerics

module Utils =
    let bigIntCompare ((a:bigint), (b:bigint)) = enum<ComparisonResult> <| a.CompareTo(b)
    let dataCompare mapper (a, b) = bigIntCompare (mapper a, mapper b)
    let onlySome a = Array.choose id a
    let allLeaves leafSet = [leafSet.left; leafSet.right] |> Array.concat |> onlySome
    let peersFromAllTables node = 
        let routingTableRows = 
            match node.routing_table with 
            | Initialized initTable -> initTable
            | Uninitialized unInitTable -> unInitTable |> onlySome
        
        let routingTable = routingTableRows |> Array.concat |> onlySome

        [routingTable; allLeaves node.leaf_set; onlySome node.neighborhood_set] 
        |> Array.concat 
        |> Array.distinctBy (fun i -> i.identifier)
    let isRoutingInitialized = function
    | Initialized(_)  -> true
    | Uninitialized(_) -> false

    let strToBigInt s = GenericBaseConverter.ConvertFromString(s, Config.numberBase)
    let toBigInt (a:char) = string a |> strToBigInt
    let convertBase fromB toB str = 
        let number = GenericBaseConverter.ConvertFromString(str, fromB)
        let newBaseString = GenericBaseConverter.ConvertToString(number, toB)
        newBaseString
    let convertBaseTo toB number = GenericBaseConverter.ConvertToString(number, toB)
    let convertBaseFrom fromB str = GenericBaseConverter.ConvertFromString(str, fromB)
    let sortByIgnoreNone dataSelector collection =
        let someValues = Array.sortBy dataSelector <| onlySome collection
        let noneCount = collection.Length - someValues.Length

        (seq {
            yield! Array.map (fun i -> Some(i)) someValues;
            for i in 1..noneCount do
                yield None
        }) |> Array.ofSeq
    let leavesDataSelector i = convertBaseFrom Config.numberBase i.identifier