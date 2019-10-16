open AustinHarris.JsonRpc

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
let handle_requests (listener: System.Net.HttpListener) (rpc: RPC.Wrapper) =
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
let main _ =
    let listener = new System.Net.HttpListener()
    listener.Prefixes.Add(sprintf "http://localhost:%d/" Config.port)
    listener.Start()

    let store =
        if System.IO.File.Exists(Config.store_location) then
            ClockStore.deserialize_clockstore Config.store_location
            |> Result.map (fun store ->
                let start_date = System.DateTime.Now.AddDays(-1.0 * Config.prune_days)
                ClockStore.truncate_clockstore store start_date)
            |> RPC.unwrap_result
        else
            Map.empty

    ClockStore.serialize_clockstore store Config.store_location
    let rpc = new RPC.Wrapper(store)
    handle_requests listener rpc

    0
