﻿#load "Utils.fs"
open System.IO
open System
open System.Threading
open System.Collections.Generic
open System.Net
open AgentModule

   
// ===========================================
// Agent System - Parent-Childrenc
// ===========================================

type Message =
    | Register of id:int
    | Unregister of id:int
    | SendMeessage of id:int * message:string
    | BroadcastMeessage of message:string
    | ThrowError

let cancellationToken = new CancellationTokenSource()
    
        
let errorAgent =
        Agent<int * Exception>.Start((fun inbox ->
                async {   while true do
                            let! id, error = inbox.Receive()
                            printfn "an error '%s' occurred in Agent %d" error.Message id}),
            cancellationToken.Token)

    
 
let agentChild(id:int) =
    let agent = Agent<string>.Start((fun inbox ->
        let rec loop messages = async{
            let! msg = inbox.Receive()
            if msg = "throw error" then raise(new Exception(sprintf "Error from Agent %d" id))
            else printfn "Message received Agent id [%d] - %s" id msg
            return! loop (msg::messages) }
        loop []), cancellationToken.Token)
    agent.Error.Add(fun error -> errorAgent.Post (id,error))        
    agent



let agentParent =
        Agent<Message>.Start((fun inbox ->
            let agents = new Dictionary<int, Agent<string>>(HashIdentity.Structural)    
            let cancellationToken = new CancellationTokenSource()            
            let rec loop count = async {
                let! msg = inbox.Receive()
                match msg with 
                | Register(id) -> 
                        if not <| agents.ContainsKey id then
                            let newAgentChild = agentChild(id)
                            agents.Add(id, newAgentChild)
                        return! loop (count + 1) 
                | Unregister(id) -> 
                        if agents.ContainsKey id then
                            let agentToRemove = agents.[id]
                            (agentToRemove :> IDisposable).Dispose()
                            agents.Remove(id) |> ignore
                        return! loop (count - 1)
                | SendMeessage(id, message) -> 
                    if agents.ContainsKey id then
                        let agentToSendMessage = agents.[id]
                        agentToSendMessage.Post(message)
                    return! loop count
                | BroadcastMeessage(message) ->
                    for KeyValue(id, agent) in agents do
                        agent.Post(message)
                    return! loop count 
                | ThrowError -> 
                    agents
                    |> Seq.filter(fun (KeyValue(id, _)) -> id % 2 = 0)
                    |> Seq.iter(fun (KeyValue(id, agent)) -> 
                            inbox.Post(SendMeessage(id, "throw error")))
                    return! loop count }
            loop 0), cancellationToken.Token)

             
for id in [0..100000] do
    agentParent.Post(Register(id))

agentParent.Post(SendMeessage(4, "Hello!"))
agentParent.Post(ThrowError)
agentParent.Post(SendMeessage(4, "Hello!"))
agentParent.Post(SendMeessage(7, "Hello!"))
agentParent.Post(Unregister(7))
agentParent.Post(SendMeessage(7, "Hello!"))
agentParent.Post(SendMeessage(9, "Hello!"))
cancellationToken.Cancel()
agentParent.Post(SendMeessage(9, "Hello!"))
