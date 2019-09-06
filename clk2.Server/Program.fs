open AustinHarris.JsonRpc

type RpcClock = {
    Id: string
    Status: string
    ElapsedSeconds: int
}

type RpcEventIn = {
    Event: string
    Timestamp: System.DateTime
}

type RpcEventOut = {
    Event: string
    Timestamp: System.DateTime
    CumulativeSeconds: int
}

/// <summary>
/// Returns the value in an Ok result, and throws an Error as an exception
/// </summary>
let unwrap_result (result: Result<'a, string>) =
    match result with
    | Result.Ok value -> value
    | Result.Error message ->
        new System.InvalidOperationException(message)
        |> raise

/// <summary>
/// Unwraps a list of Results into either the first Error, or a list of Ok
/// </summary>
let flatten_results (results: Result<'a, 'b> list) =
    results
    |> List.fold (fun accum_r value_r ->
        match (accum_r, value_r) with
        | (Result.Ok accum, Result.Ok value) ->
            Result.Ok (value :: accum)

        | (Result.Error _, _) -> accum_r
        | (_, Result.Error err) -> Result.Error err)
                 (Result.Ok [])
    |> Result.map List.rev

/// <summary>
/// Converts a store clock type into an RpcClock for the client
/// </summary>
let to_rpc_clock (clock: ClockStore.Clock) =
    let status_text =
        match clock.Status with
        | ClockStore.ClockedIn -> "in"
        | ClockStore.ClockedOut -> "out"
        | ClockStore.ClockReset -> "reset"

    let extra_seconds =
        if status_text = "in" then
            let (Some last_start) = ClockStore.last_clocked_in clock
            let delta = System.DateTime.Now - last_start
            int delta.TotalSeconds
        else
            0

    {Id=clock.Id; Status=status_text; ElapsedSeconds=clock.ElapsedSeconds + extra_seconds}

/// <summary>
/// Converts a store event sequence into an RpcEvent for the client
/// </summary>
let to_rpc_events (events: ClockStore.Event list) =
    let process_event evt cum_seconds =
        let event_type =
            match evt with
            | ClockStore.StartClock _ -> "start"
            | ClockStore.StopClock _ -> "stop"
            | ClockStore.ResetClock _ -> "reset"

        let timestamp = ClockStore.event_timestamp evt
        {Event=event_type; Timestamp=timestamp; CumulativeSeconds=cum_seconds}

    ClockStore.cumulative_times events
    |> List.map2 process_event events

/// <summary>
/// Converts an RPC event type into a store event
/// </summary>
let from_rpc_event (id: string) (evt: RpcEventIn) =
    match evt.Event with
    | "start" ->
        ClockStore.StartClock (evt.Timestamp, id)
        |> Result.Ok

    | "stop" ->
        ClockStore.StopClock (evt.Timestamp, id)
        |> Result.Ok

    | "reset" ->
        ClockStore.ResetClock (evt.Timestamp, id)
        |> Result.Ok

    | _ ->
        Result.Error (sprintf "Event type %s is not valid" evt.Event)

/// <summary>
/// JSON-RPC wrapper for the underlying ClockStore
/// </summary>
type RpcInterface(init_store: ClockStore.Store) =
    inherit JsonRpcService()

    let mutable store = init_store

    [<JsonRpcMethod("start")>]
    member this.Start (id: string) =
        store <- unwrap_result (ClockStore.clock_in store id System.DateTime.Now)
        ClockStore.serialize_clockstore store Config.store_location

    [<JsonRpcMethod("stop")>]
    member this.Stop (id: string) =
        store <- unwrap_result (ClockStore.clock_out store id System.DateTime.Now)
        ClockStore.serialize_clockstore store Config.store_location

    [<JsonRpcMethod("finish")>]
    member this.Finish (id: string) =
        let (store', total_time) = unwrap_result (ClockStore.clock_reset store id System.DateTime.Now)
        store <- store'
        ClockStore.serialize_clockstore store Config.store_location
        total_time

    [<JsonRpcMethod("history")>]
    member this.History (id: string) =
        let history =
            ClockStore.fetch_clock store id false
            |> Result.map (fun clk -> clk.Events)
            |> unwrap_result

        to_rpc_events history

    [<JsonRpcMethod("rewrite")>]
    member this.Rewrite (id: string) (events: RpcEventIn list) =
        let store_events =
            List.map (from_rpc_event id) events
            |> flatten_results
            |> unwrap_result

        store <- unwrap_result (ClockStore.clock_rewrite store id store_events)
        ClockStore.serialize_clockstore store Config.store_location

    [<JsonRpcMethod("list")>]
    member this.List () =
        store
        |> Map.toSeq
        |> Seq.map snd
        |> Seq.map to_rpc_clock
        |> Seq.toList

    [<JsonRpcMethod("current")>]
    member this.Current () =
        match ClockStore.currently_clocked_in store with
        | Some clk -> clk.Id
        | None -> null

/// <summary>
/// Copies the input stream into the buffer
/// </summary>
let rec copy_stream (stream: System.IO.Stream)
                    (buffer: byte[])
                    (offset: int)
                    (size: int) =
    if offset < size then
        let chunk = stream.Read(buffer, offset, size - offset)
        copy_stream stream buffer (offset + chunk) size
    else
        ()

/// <summary>
/// Dispatches a single RPC request and returns the response
/// </summary>
let handle_requests (listener: System.Net.HttpListener) (rpc: RpcInterface) =
    let fail_request (ctx: System.Net.HttpListenerContext) status desc =
        ctx.Response.StatusCode <- status
        ctx.Response.StatusDescription <- desc
        ctx.Response.OutputStream.Close()

    while true do
        let context = listener.GetContext()
        if context.Request.HttpMethod  <> "POST" then
            fail_request context 405 "Illegal Method"

        elif context.Request.Url.PathAndQuery <> "/" then
            fail_request context 404 "Not Found"

        elif (context.Request.ContentType <> "application/json" &&
              context.Request.ContentType <> "application/json-rpc") then
            fail_request context 400 "Illegal Content-Type"

        else
            context.Response.ContentType <- "application/json-rpc"
            let inputBuffer = Array.create (int context.Request.ContentLength64) (byte 0)
            copy_stream context.Request.InputStream
                        inputBuffer
                        0
                        (int context.Request.ContentLength64)

            let input = System.Text.Encoding.UTF8.GetString(inputBuffer)
            JsonRpcProcessor
                .Process(input)
                .ContinueWith(fun (result: System.Threading.Tasks.Task<string>) ->
                    let result_bytes = System.Text.Encoding.UTF8.GetBytes(result.Result)
                    context.Response.ContentLength64 <- int64 (Array.length result_bytes)
                    context.Response.OutputStream.Write(result_bytes, 0, Array.length result_bytes)
                    context.Response.OutputStream.Close())
                .Wait()

[<EntryPoint>]
let main argv =
    let listener = new System.Net.HttpListener()
    listener.Prefixes.Add(sprintf "http://localhost:%d/" Config.port)
    listener.Start()

    let store =
        if System.IO.File.Exists(Config.store_location) then
            ClockStore.deserialize_clockstore Config.store_location
            |> Result.map (fun store ->
                let start_date = System.DateTime.Now.AddDays(-1.0 * Config.prune_days)
                ClockStore.truncate_clockstore store start_date)
            |> unwrap_result
        else
            Map.empty

    let rpc = new RpcInterface(store)
    handle_requests listener rpc

    0
