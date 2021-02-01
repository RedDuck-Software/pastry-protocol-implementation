# Pastry protocol in F#

This is an F# implementation of [the Pastry protocol](https://en.wikipedia.org/wiki/Pastry_(DHT)) 

Pastry is an overlay network and routing network for the implementation of a distributed hash table (DHT) similar to Chord. The key-value pairs are stored in a redundant peer-to-peer network of connected Internet hosts. The protocol is bootstrapped by supplying it with the IP address of a peer already in the network and from then on via the routing table which is dynamically built and repaired. It is claimed that because of its redundant and decentralized nature there is no single point of failure and any single node can leave the network at any time without warning and with little or no chance of data loss. The protocol is also capable of using a routing metric supplied by an outside program, such as ping or traceroute, to determine the best routes to store in its routing table.
