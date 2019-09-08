module RPC
open Newtonsoft.Json
open Newtonsoft.Json.Linq

type JsonRpcRequest = {
    [<JsonProperty("jsonrpc")>]
    Protocol: string

    [<JsonProperty("method")>]
    Method: string

    [<JsonProperty("params")>]
    Parameters: obj list

    [<JsonProperty("id")>]
    Id: int
}

type Clock = {
    [<JsonProperty("id")>]
    Id: string

    [<JsonProperty("status")>]
    Status: string

    [<JsonProperty("elapsed_sec")>]
    ElapsedSeconds: int
}

type EventServer = {
    [<JsonProperty("event")>]
    Event: string

    [<JsonProperty("timestamp")>]
    Timestamp: System.DateTime
}

type EventClient = {
    [<JsonProperty("event")>]
    Event: string

    [<JsonProperty("timestamp")>]
    Timestamp: System.DateTime

    [<JsonProperty("cumulative_sec")>]
    CumulativeSeconds: int
}

/// <summary>
/// Sends a JSON-RPC request to the server and returns the parsed response
/// </summary>
let call_server (endpoint: System.Uri) (func: string) (args: obj list) =
    let rpc = {
        Id=1
        Protocol="2.0"
        Method=func
        Parameters=args
    }

    let request_text = JsonConvert.SerializeObject(rpc)
    let request_bytes = System.Text.Encoding.UTF8.GetBytes(request_text)

    let request = System.Net.HttpWebRequest.CreateHttp(endpoint)
    request.ContentType <- "application/json-rpc"
    request.Accept <- "application/json; application/json-rpc"
    request.ContentLength <- int64 request_bytes.Length
    request.Method <- "POST"

    let request_stream = request.GetRequestStream()
    request_stream.Write(request_bytes, 0, request_bytes.Length)
    request_stream.Close()

    use (response: System.Net.HttpWebResponse) =
        try
            downcast request.GetResponse()
        with
        | :? System.Net.WebException as err ->
            if err.Response <> null then
                downcast err.Response
            else
                raise err

    if (response.ContentType <> "application/json" &&
            response.ContentType <> "application/json-rpc") then
        response.Close()
        Result.Error (sprintf "Received unexpected content type %s from server" response.ContentType)
    else
        use reader = new System.IO.StreamReader(response.GetResponseStream(), System.Text.Encoding.UTF8)
        let json = JObject.Parse(reader.ReadToEnd())
        if json.ContainsKey("error") then
            let error_data = json.["error"].["data"]
            match error_data with
            | :? JObject as exception_data ->
                let (message: string) = exception_data.Value("Message")
                Result.Error message

            | _ ->
                Result.Error (json.["error"].Value("message"))

        else
            Result.Ok json.["result"]

/// <summary>
/// Calls the start RPC method on the given clock
/// </summary>
let call_start (endpoint: System.Uri) (id: string) =
    let args = [id :> obj]
    call_server endpoint "start" args

/// <summary>
/// Calls the stop RPC method on the given clock
/// </summary>
let call_stop (endpoint: System.Uri) (id: string) =
    let args = [id :> obj]
    call_server endpoint "stop" args

/// <summary>
/// Calls the finish RPC method on the given clock and returns the elapsed seconds
/// </summary>
let call_finish (endpoint: System.Uri) (id: string) =
    let args = [id :> obj]
    call_server endpoint "finish" args
    |> Result.map (fun result ->
        (result.ToObject() : int))

/// <summary>
/// Calls the history RPC method on the given clock and returns the history
/// </summary>
let call_history (endpoint: System.Uri) (id: string) =
    let args = [id :> obj]
    call_server endpoint "history" args
    |> Result.map (fun result ->
        (result.ToObject() : EventClient list))

/// <summary>
/// Calls the rewrite RPC method on the given clock
/// </summary>
let call_rewrite (endpoint: System.Uri) (id: string) (events: EventServer list) =
    let args = [id :> obj; events :> obj]
    call_server endpoint "rewrite" args

/// <summary>
/// Calls the list RPC method and returns the available clocks
/// </summary>
let call_list (endpoint: System.Uri) =
    call_server endpoint "list" []
    |> Result.map (fun result ->
        (result.ToObject() : Clock list))

/// <summary>
/// Calls the current RPC method and returns the currently active clock
/// </summary>
let call_current (endpoint: System.Uri) =
    call_server endpoint "current" []
    |> Result.map (fun result ->
        if result.Type = JTokenType.Null then
            None
        else
            Some (result.ToObject() : string))
