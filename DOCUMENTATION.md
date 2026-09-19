# Simply Networking Documentation
## Connection
This shows you how to connect to the server and join a room
```cs
using SimplyNetworking.API;
using System.Net;

// Sets the server IP:Port
NetworkManager.Initialize(IPAddress.Parse("127.0.0.1"), 8082); 

// Connects the client to the server
NetworkManager.Connect(); 

// Joins room - Client is now ready for RPC and Network Variable usage
NetworkManager.JoinRoom("roomName"); 
```

## Network Manager
With the Network Manager. It can tell you very useful things for Networking.
```cs
// BOOL Is the client the master client?
NetworkManager.MasterClient

// STRING What room is the client conencted to?
NetworkManager.Room

// UINT What is the clients global ID? (not much use really)
NetworkManager.ID
```
## Network Identity
In order to do any RPC's or Network Variables. You require at least one Network Identity. Create one by doing:
### Creating a Network Identity
```cs

// Instantiate a Network Identity and assign a unique ID, eg 0
NetworkIdentity identity = new NetworkIdentity(0);

// Add it to the manager
NetworkManager.AddIdentity(identity);
```

### RPC's
To use RPC's, you need to make sure you are, Connected to a server and in a room
#### Creating an RPC
Without Parameters
```cs
// Create the method for the RPC
void method()
{
    Console.WriteLine("RPC Ran");
}

// Bind it to an identity
identity.BindRPC("method", method);
```
With Parameters
```cs
// Create the method for the RPC
void method(int number, string text)
{
    Console.WriteLine($"RPC Ran: '{number}' '{text}'");
}

// Bind it to an identity with a unique name
identity.BindRPC<int, string>("method", method);
```

The Maximum amount of parameters for an RPC is 5.

#### Calling an RPC
Without parameters
```cs
// Call RPC by its unique name. (it can be anything, it doesn't have to match the method name)
identity.RPC("method", RpcTarget.All);
```
With Parameters
```cs
// Call RPC by its unique name with the 2 parameters.
identity.RPC("method", RpcTarget.All, 2, "Cool Text");
```

RpcTargets
```cs
RpcTarget.All    - Runs RPC on all clients
RpcTarget.Others - Runs RPC on all clients except the caller
RpcTarget.Master - Runs RPC on the master client in the room
```

### Network Variables
To use Network Variables, you need to make sure you are, Connected to a server and in a room
#### Creating a Network Variable
```cs
// Create a network variable
NetworkVariable<int> number = new NetworkVariable<int>("cool_number", 1, NetworkDelivery.Reliable);

// What does this do?
// In the <> is what type you want it to be
// "cool_number" is just a unique name for the variable
// 1 is the default value for the network variable
// NetworkDelivery is how you want it to be delivered:
//     NetworkDelivery.Unreliable is recommended if the variable will be changed a lot, can drop packets
//     NetworkDelivery.Reliable is recommended if the variable is changed less than once a second, doesn't drop packets

// Add it to a Network Identity
identity.AddNetworkVariable(number);
```
#### Changing value
```cs
// Change value (It does sync automatically)
number.Value = 5;
```







