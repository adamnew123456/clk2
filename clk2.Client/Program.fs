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
/// Prints the usage message to the command line
/// </summary>
let usage (error: bool) =
    if error then
        eprintf "clk2-cli URL (list | current | start ID | stop ID | finish ID | history ID | rewrite ID)\n"
        1
    else
        printf "clk2-cli URL (list | current | start ID | stop ID | finish ID | history ID | rewrite ID)\n"
        0

/// <summary>
/// Converts a value in seconds to a human-readable string value
/// </summary>
let render_secs (value: int) =
    let hours = value / (60 * 60)
    let value = value % (60 * 60)

    let minutes = value / 60
    let seconds = value % 60

    if hours > 0 then
        sprintf "%d:%02d:%02d" hours minutes seconds
    elif minutes > 0 then
        sprintf "0:%02d:%02d" minutes seconds
    else
        sprintf "0:00:%02d" seconds

let invariant_culture = System.Globalization.CultureInfo.InvariantCulture

/// <summary>
/// Converts a history event into a semicolon-separated output format
/// </summary>
let render_event (evt: RPC.EventClient) =
    sprintf "%s; %5s; %s"
            (evt.Timestamp.ToString("yyyy-MM-dd HH:mm:ss zzz", invariant_culture))
            evt.Event
            (render_secs evt.CumulativeSeconds)

/// <summary>
/// Converts a rendered event into a history event. Ignores cumulative times.
/// </summary>
let parse_event (event_str: string) =
    let parts =
        event_str.Split(';')
        |> List.ofArray

    match parts with
    | time :: evt :: _ ->
        let timestamp = System.DateTime.ParseExact(time.Trim(), "yyyy-MM-dd HH:mm:ss zzz", invariant_culture)
        Result.Ok ({Event=evt.Trim(); Timestamp=timestamp} : RPC.EventServer)

    | _ ->
        Result.Error (sprintf "Line '%s' does not contain both event timestamp and name" event_str)

[<EntryPoint>]
let main argv =
    if Array.contains "-h" argv || Array.contains "--help" argv then
        usage false
    else
        let args = List.ofArray argv
        match args with
        | url :: action :: rest ->
            let uri = new System.Uri(url)
            match (action, rest) with
            | ("list", []) ->
                match RPC.call_list uri with
                | Result.Ok clocks ->
                    clocks
                    |> List.iter (fun clock ->
                        let elapsed = render_secs clock.ElapsedSeconds
                        printf "%s\t%s\t%s\n" clock.Id clock.Status elapsed)
                    0

                | Result.Error message ->
                    eprintf "Server error: %s\n" message
                    1

            | ("current", []) ->
                match RPC.call_current uri with
                | Result.Ok (Some id) ->
                    printf "%s\n" id
                    0

                | Result.Ok None ->
                    0

                | Result.Error message ->
                    eprintf "Server error: %s\n" message
                    1

            | ("start", [id]) ->
                match RPC.call_start uri id with
                | Result.Ok _ -> 0
                | Result.Error message ->
                    eprintf "Server error: %s\n" message
                    1

            | ("stop", [id]) ->
                match RPC.call_stop uri id with
                | Result.Ok _ -> 0
                | Result.Error message ->
                    eprintf "Server error: %s\n" message
                    1

            | ("finish", [id]) ->
                match RPC.call_finish uri id with
                | Result.Ok elapsed_sec ->
                    printf "%s\n" (render_secs elapsed_sec)
                    0

                | Result.Error message ->
                    eprintf "Server error: %s\n" message
                    1

            | ("history", [id]) ->
                match RPC.call_history uri id with
                | Result.Ok history ->
                    history
                    |> List.map render_event
                    |> List.iter (printf "%s\n")
                    0

                | Result.Error message ->
                    eprintf "Server error: %s\n" message
                    1

            | ("rewrite", [id]) ->
                let result =
                    System.Console.In.ReadToEnd().Split('\r', '\n')
                    |> List.ofArray
                    |> List.map (fun (s: string) -> s.Trim())
                    |> List.filter ((<>) "")
                    |> List.map parse_event
                    |> flatten_results
                    |> Result.bind (RPC.call_rewrite uri id)

                match result with
                | Result.Ok _ ->
                    0

                | Result.Error message ->
                    eprintf "Server error: %s\n" message
                    1

            | _ -> usage true

        | _ -> usage true
