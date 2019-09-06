module ClockStore
open Newtonsoft.Json
open System.IO

(*
# Data Representation

When data is read from or written to disk, we use the ClockEvent representation.
This represents the clock store as a series of timestamped events ordered from
oldest to most recent.

For most operations we use the Clock map representation, which groups data by
the clock entry and stores a summary of the events along with the events
themselves.
 *)

type Event =
    | StartClock of System.DateTime * string
    | StopClock of System.DateTime * string
    | ResetClock of System.DateTime * string

type Status =
    | ClockedIn
    | ClockedOut
    | ClockReset

type Clock = {
    Id: string
    Status: Status
    ElapsedSeconds: int
    Events: Event list
}

type Store = Map<string, Clock>

exception InvalidStore of System.DateTime * string

/// <summary>
/// Gets the ID of an event
/// </summary>
let event_id (evt: Event) =
    match evt with
    | StartClock (_, id)
    | StopClock (_, id)
    | ResetClock (_, id) ->
        id

/// <summary>
/// Gets the timestamp  of an event
/// </summary>
let event_timestamp (evt: Event) =
    match evt with
    | StartClock (timestamp, _)
    | StopClock (timestamp, _)
    | ResetClock (timestamp, _) ->
        timestamp

/// <summary>
/// Retrieves a Clock by its ID
/// </summary>
let fetch_clock (store: Store) (id: string) (with_default: bool) =
    if id = "" then
        Result.Error "Clock with empty ID is not valid"
    else
        match Map.tryFind id store with
        | Some clk -> Result.Ok clk
        | None ->
            if with_default then
                Result.Ok {Id=id; Status=ClockReset; ElapsedSeconds=0; Events=[]}
            else
                Result.Error (sprintf "Could not find clock with ID %s" id)

/// <summary>
/// Replaces the Clock in the store with the provided Clock
/// </summary>
let update_store (store: Store) (clk: Clock) =
    Map.add clk.Id clk store

/// <summary>
/// Finds the bounding times for each of the clock's sessions in chronological order
/// </summary>
let clock_bounding_times (clk: Clock) =
    let process_events (bounds, closed, last_start_opt, last_end_opt) evt =
        match evt with
        | StartClock (timestamp, _) ->
            if Option.isSome last_start_opt then
                (bounds, false, last_start_opt, last_end_opt)
            else
                (bounds, false, Some timestamp, last_end_opt)

        | StopClock (timestamp, _) ->
            (bounds, true, last_start_opt, Some timestamp)

        | ResetClock _ ->
            match (last_start_opt, last_end_opt) with
            | (Some last_start, Some last_end) ->
                ((last_start, last_end) :: bounds, true, None, None)

            | _ ->
                (bounds, true, None, None)

    let (bounds, closed, last_start_opt, last_end_opt) =
        clk.Events
        |> List.fold process_events ([], true, None, None)

    if closed then
        List.rev bounds
    else
        let (Some last_start) = last_start_opt
        (last_start, System.DateTime.Now) :: bounds
        |> List.rev

/// <summary>
/// Finds the starting time of each of the clock's sessions
/// </summary>
let clock_start_times (clk: Clock) =
    let process_events (is_reset, starts) evt =
        match evt with
        | StartClock (timestamp, _) ->
            if is_reset then
                (false, timestamp :: starts)
            else
                (false, starts)

        | StopClock _ -> (is_reset, starts)
        | ResetClock _ -> (true, starts)

    clk.Events
    |> List.fold process_events (true, [])
    |> snd

/// <summary>
/// Finds when a clock was last clocked in
/// </summary>
let last_clocked_in (clk: Clock) =
    clk.Events
    |> Seq.filter (fun evt ->
                   match evt with
                   | StartClock _ -> true
                   | _ -> false)
    |> Seq.map event_timestamp
    |> Seq.sortDescending
    |> Seq.tryHead

/// <summary>
/// Computes the cumulative time between start and stop events (reset events are
/// ignored). Time between the last start and the current time is also included if
/// the last event is a start.
/// </summary>
let cumulative_times (events: Event list) =
    let process_event (cum_history, cum_seconds, last_clock_in) evt =
        match evt with
        | StartClock (timestamp, _) ->
            (cum_seconds :: cum_history, cum_seconds, Some timestamp)

        | StopClock (timestamp, _) ->
            let (Some last) = last_clock_in
            let (delta: System.TimeSpan) = timestamp - last
            let cum_seconds = cum_seconds + int delta.TotalSeconds
            (cum_seconds :: cum_history, cum_seconds, None)

        | ResetClock _ ->
            (cum_seconds :: cum_history, cum_seconds, None)

    let (history_rev, _, _) =
        events
        |> List.fold process_event ([], 0, None)

    List.rev history_rev

/// <summary>
/// Returns the currently clocked in clock, or None if no clock is clocked in
/// </summary>
let currently_clocked_in (store: Store) =
    store
    |> Map.toSeq
    |> Seq.filter (fun (_, clk) -> clk.Status = ClockedIn)
    |> Seq.tryHead
    |> Option.map snd

/// <summary>
/// Clocks in a task at the given time
/// </summary>
let clock_in (store: Store) (id: string) (timestamp: System.DateTime) =
    fetch_clock store id true
    |> Result.bind (fun clk ->
        if clk.Status = ClockedIn then
            Result.Error (sprintf "Clock %s must be stopped before it can be started" id)
        else
            Result.Ok clk)

    |> Result.bind (fun clk ->
        match currently_clocked_in store with
        | None -> Result.Ok clk
        | Some other_clk ->
            Result.Error (sprintf "Cannot clock in %s while %s is already clocked in" id other_clk.Id))

    |> Result.map (fun clk ->
        let evt = StartClock (timestamp, id)
        let clk = {clk with Events=clk.Events @ [evt]
                            Status=ClockedIn}
        update_store store clk)

/// <summary>
/// Clocks out a task at the given time
/// </summary>
let clock_out (store: Store) (id: string) (timestamp: System.DateTime) =
    fetch_clock store id false
    |> Result.bind (fun clk ->
        if clk.Status <> ClockedIn then
            Result.Error (sprintf "Clock %s must be started before it can be stopped" id)
        else
            Result.Ok clk)

    |> Result.bind (fun clk ->
        match last_clocked_in clk with
        | Some start_timestamp -> Result.Ok (clk, start_timestamp)
        | None ->
            Result.Error (sprintf "Cannot find last clock in event for %s" id))

    |> Result.map (fun (clk, start_timestamp) ->
        let evt = StopClock (timestamp, id)
        let clocked_span = timestamp - start_timestamp
        let clk = {clk with Events=clk.Events @ [evt]
                            Status=ClockedOut
                            ElapsedSeconds=clk.ElapsedSeconds + int clocked_span.TotalSeconds}
        update_store store clk)

/// <summary>
/// Resets the given clock
/// </summary>
let clock_reset (store: Store) (id: string) (timestamp: System.DateTime) =
    fetch_clock store id false
    |> Result.bind (fun clk ->
        if clk.Status <> ClockedOut then
            Result.Error (sprintf "Clock %s must be started before it can be stopped" id)
        else
            Result.Ok clk)

    |> Result.map (fun clk ->
        let time = clk.ElapsedSeconds
        let evt = ResetClock (timestamp, id)
        let clk = {clk with Events=clk.Events @ [evt]
                            Status=ClockReset
                            ElapsedSeconds=0}
        (update_store store clk, time))

/// <summary>
/// Applies a event to the store
/// </summary>
let process_event (store: Store) (evt: Event) =
    match evt with
    | StartClock (timestamp, id) -> clock_in store id timestamp
    | StopClock (timestamp, id) -> clock_out store id timestamp
    | ResetClock (timestamp, id) -> Result.map fst (clock_reset store id timestamp)

/// <summary>
/// Rewrites the given clock with a new set of events
/// </summary>
let clock_rewrite (store: Store) (id: string) (events: Event list) =
    let process_events store_r evt =
        store_r
        |> Result.bind (fun store -> process_event store evt)

    let ord_events = List.sortBy event_timestamp events
    let reset_clk = {Id=id; Status=ClockReset; ElapsedSeconds=0; Events=[]}
    let reset_store = update_store store reset_clk

    List.fold process_events (Result.Ok reset_store) ord_events

/// <summary>
/// Truncates a ClockStore so that it contains only events after the given timestamp.
/// </summary>
let truncate_clockstore (store: Store) (new_start: System.DateTime) =
    let truncate_clk clk =
        // We can't simply cut off the events at the start time. There could be
        // events are required to make sense of the stream which occur on the other
        // side of the boundary, and cutting them off would cause problems. For
        // example:
        //
        //                        v cutoff
        //                        |
        // [ClockStart, ClockEnd, |  ClockStart, ClockEnd, ClockReset]
        // - Here we lose some part of the elapsed time
        //                        |
        //           [ClockStart, |  ClockEnd, ClockReset]
        // - This stream would be invalid with naive cut, since start must
        //   come before end

        let keep_start_time_opt =
            clock_bounding_times clk
            |> Seq.filter (fun (bnd_start, bnd_end) ->
                (bnd_start >= new_start) && (bnd_end >= new_start))
            |> Seq.tryHead
            |> Option.map fst

        match keep_start_time_opt with
        | Some keep_start_time ->
            let keep_events =
                clk.Events
                |> List.filter (fun evt -> event_timestamp evt >= keep_start_time)

            {clk with Events=keep_events}

        | None ->
            clk

    store
    |> Map.map (fun _ clk -> truncate_clk clk)

/// <summary>
/// Converts a ClockStore into a clock store file
/// </summary>
let serialize_clockstore (store: Store) (store_file: string) =
    let events =
        store
        |> Map.toSeq
        |> Seq.map (fun (_, clk) -> clk.Events)
        |> Seq.concat
        |> Seq.sortBy event_timestamp

    let store_contents = JsonConvert.SerializeObject(events, Formatting.Indented)
    File.WriteAllText(store_file, store_contents)

/// <summary>
/// Converts the contents of a clock store file into a ClockStore
/// </summary>
let deserialize_clockstore (store_file: string) =
    let process_events store_r evt =
        store_r
        |> Result.bind (fun store -> process_event store evt)

    let store_contents = File.ReadAllText(store_file)

    JsonConvert.DeserializeObject<Event list>(store_contents)
    |> Seq.fold process_events (Result.Ok Map.empty)
