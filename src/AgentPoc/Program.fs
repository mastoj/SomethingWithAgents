// Learn more about F# at http://fsharp.org
// See the 'F# Tutorial' project for more help.
open System

type Agent<'T> = MailboxProcessor<'T>

[<AutoOpen>]
module Messages = 
    type SubscriptionMonitorMessage =
        | StartMonitor
        | Reindex
        | IndexingDone of Agent<SubscriptionAgentMessage>
    and SubscriptionAgentMessage = 
        | StartSub of Agent<SubscriptionMonitorMessage>
        | Stop
        | Kill

[<AutoOpen>]
module Agent = 
    let post (agent:Agent<'T>) message = agent.Post message
    let postAsyncReply (agent:Agent<'T>) message = agent.PostAndAsyncReply message

module SubscriptionAgent = 
    type SubscriptionAgentState = 
        | CatchingUp
        | CaughtUp
        | Active
        | Stopped

    let rec loop (inbox:Agent<SubscriptionAgentMessage>) state = 
        async {
            let! msg = inbox.Receive()
            match msg with
            | StartSub agent -> 
                printfn "Agent: Starting %A" DateTime.Now
                do! Async.Sleep(5000)
                (IndexingDone inbox) |> post agent
                return! loop inbox CatchingUp
            | Stop -> 
                printfn "Agent: Stopping %A" DateTime.Now
                return! loop inbox Stopped
            | Kill -> 
                printfn "Agent: Starting %A" DateTime.Now
                ()
        }

    let createAgent() = Agent.Start(fun inbox ->
        let initState = Stopped
        loop inbox initState)

module SubscriptionMonitorAgent = 
    type MonitorState = {
        ActiveSubscriptionAgent: Agent<SubscriptionAgentMessage>
        CatchingUpSubscriptionAgent: Agent<SubscriptionAgentMessage> option
    }

    let rec loop (inbox:Agent<SubscriptionMonitorMessage>) state = 
        async {
            let! msg = inbox.Receive()
            match msg with
            | Reindex -> 
                let newAgent = SubscriptionAgent.createAgent()
                inbox |> StartSub |> post newAgent
                return! loop inbox {state with CatchingUpSubscriptionAgent = Some newAgent}
            | StartMonitor -> 
                inbox |> StartSub |> post state.ActiveSubscriptionAgent
                printfn "Monitor: starting sub"
                return! loop inbox state
            | IndexingDone agent ->
                match agent = state.ActiveSubscriptionAgent, Some agent = state.CatchingUpSubscriptionAgent with
                | true, _ -> printfn "Monitor: Acive agent done"
                | _, true -> printfn "Monitor: Catchup agent done"
                | _, _ -> printfn "Monitor: Didn't execpt this"
                return! loop inbox {state with CatchingUpSubscriptionAgent = None; ActiveSubscriptionAgent = agent }
            | _ -> ()
        }

    let createAgent() = Agent.Start(fun inbox ->
        let initState = {ActiveSubscriptionAgent = SubscriptionAgent.createAgent(); CatchingUpSubscriptionAgent = None}
        loop inbox initState)


open System
open SubscriptionMonitorAgent
[<EntryPoint>]
let main argv = 
    printfn "%A" argv

    let monitor = SubscriptionMonitorAgent.createAgent()

    let rec doStuff() = 
        let line = Console.ReadLine()
        match line with
        | "1" -> 
            StartMonitor |> post monitor
            doStuff()
        | "2" -> 
            Reindex |> post monitor
            doStuff()
        | _ -> ()

    doStuff()
    0 // return an integer exit code
