module Config

[<Literal>]
let L = 16

[<Literal>]
let b = 2

let numberBase = pown 2 b
let leafSize = pown 2 b
let neighborhoodSize = pown 2 b
let routingTableColumns = pown 2 b